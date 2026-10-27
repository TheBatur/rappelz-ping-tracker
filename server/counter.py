#!/usr/bin/env python3
"""Anonymous usage counter for Rappelz Ping.

Standard library only. Runs on 127.0.0.1 behind a reverse proxy (Caddy).

Stored:     random install ID, random session ID, app version, timestamps,
            session length, ping summary (min/avg/max), game server address,
            country code.
Not stored: IP addresses. The client IP is only used, in memory, to look up
            the country when a session starts and for a one-minute rate limit.
            It is never written to disk or logs.
"""
import bisect
import gzip
import html
import ipaddress
import json
import os
import re
import secrets
import sqlite3
import threading
import time
import urllib.request
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

DATA_DIR = os.environ.get("RP_DATA", "/var/lib/rappelzping")
PORT = int(os.environ.get("RP_PORT", "3030"))
DB_PATH = os.path.join(DATA_DIR, "counter.db")
KEY_PATH = os.path.join(DATA_DIR, "stats_key")
GEO_PATH = os.path.join(DATA_DIR, "dbip-country-lite.csv.gz")
GEO_URL = "https://download.db-ip.com/free/dbip-country-lite-{month}.csv.gz"

UUID_RE = re.compile(r"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")
VERSION_RE = re.compile(r"^[0-9]{1,3}(\.[0-9]{1,4}){1,3}$")
SERVER_RE = re.compile(r"^[0-9a-fA-F.:\[\]]{3,60}$")
EVENTS_PER_MINUTE = 30  # per client IP

lock = threading.Lock()
STATS_KEY = ""


# ---------------------------------------------------------------- database
def db():
    c = sqlite3.connect(DB_PATH, timeout=10)
    c.execute("PRAGMA journal_mode=WAL")
    return c


def db_init():
    with db() as c:
        c.executescript(
            """
            CREATE TABLE IF NOT EXISTS installs (
                id TEXT PRIMARY KEY,
                first_seen INTEGER NOT NULL,
                last_seen INTEGER NOT NULL,
                country TEXT
            );
            CREATE TABLE IF NOT EXISTS sessions (
                id TEXT PRIMARY KEY,
                install_id TEXT NOT NULL,
                version TEXT,
                started INTEGER NOT NULL,
                last_seen INTEGER NOT NULL,
                seconds INTEGER NOT NULL DEFAULT 0,
                ping_min INTEGER, ping_avg INTEGER, ping_max INTEGER, ping_samples INTEGER,
                game_server TEXT,
                country TEXT
            );
            CREATE INDEX IF NOT EXISTS sessions_started ON sessions(started);
            CREATE INDEX IF NOT EXISTS sessions_last_seen ON sessions(last_seen);
            """
        )


def stats_key():
    if not os.path.exists(KEY_PATH):
        with open(KEY_PATH, "w") as f:
            f.write(secrets.token_urlsafe(24))
        os.chmod(KEY_PATH, 0o600)
    with open(KEY_PATH) as f:
        return f.read().strip()


# ---------------------------------------------------------------- country (DB-IP Lite, CC BY 4.0)
class Geo:
    def __init__(self):
        self.v4 = ([], [], [])  # range start, range end, country
        self.v6 = ([], [], [])
        self.month = None

    def load(self):
        if not os.path.exists(GEO_PATH):
            return
        v4, v6 = ([], [], []), ([], [], [])
        with gzip.open(GEO_PATH, "rt", encoding="ascii", errors="ignore") as f:
            for line in f:
                p = line.strip().split(",")
                if len(p) != 3:
                    continue
                try:
                    a, b = ipaddress.ip_address(p[0]), ipaddress.ip_address(p[1])
                except ValueError:
                    continue
                t = v4 if a.version == 4 else v6
                t[0].append(int(a)); t[1].append(int(b)); t[2].append(p[2][:2])
        self.v4, self.v6 = v4, v6
        self.month = time.strftime("%Y-%m", time.gmtime(os.path.getmtime(GEO_PATH)))

    def country(self, ip):
        try:
            a = ipaddress.ip_address(ip)
        except ValueError:
            return None
        starts, ends, codes = self.v4 if a.version == 4 else self.v6
        n = int(a)
        i = bisect.bisect_right(starts, n) - 1
        if i >= 0 and n <= ends[i] and codes[i] not in ("ZZ", ""):
            return codes[i]
        return None

    def refresh_if_needed(self):
        """DB-IP publishes a new free file every month."""
        month = time.strftime("%Y-%m", time.gmtime())
        if self.month == month:
            return
        try:
            tmp = GEO_PATH + ".new"
            req = urllib.request.Request(GEO_URL.format(month=month), headers={"User-Agent": "rappelzping-counter"})
            with urllib.request.urlopen(req, timeout=60) as r, open(tmp, "wb") as f:
                while chunk := r.read(1 << 16):
                    f.write(chunk)
            os.replace(tmp, GEO_PATH)
            self.load()
        except Exception as e:  # keep using the old file
            print("geo refresh failed:", e, flush=True)


geo = Geo()


def geo_loop():
    while True:
        geo.refresh_if_needed()
        time.sleep(24 * 3600)


# ---------------------------------------------------------------- rate limit (memory only)
hits = {}
hits_minute = 0


def rate_limited(ip):
    global hits, hits_minute
    minute = int(time.time() // 60)
    with lock:
        if minute != hits_minute:
            hits, hits_minute = {}, minute
        hits[ip] = hits.get(ip, 0) + 1
        return hits[ip] > EVENTS_PER_MINUTE


# ---------------------------------------------------------------- events
def int_in(v, lo, hi):
    if isinstance(v, bool) or not isinstance(v, (int, float)):
        return None
    v = int(v)
    return v if lo <= v <= hi else None


def record_event(data, ip):
    install = str(data.get("install", "")).lower()
    session = str(data.get("session", "")).lower()
    if not UUID_RE.match(install) or not UUID_RE.match(session):
        return False
    version = str(data.get("version", ""))
    version = version if VERSION_RE.match(version) else None
    seconds = int_in(data.get("seconds"), 0, 7 * 24 * 3600) or 0
    ping = data.get("ping") if isinstance(data.get("ping"), dict) else {}
    pmin, pavg, pmax = (int_in(ping.get(k), 0, 60000) for k in ("min", "avg", "max"))
    samples = int_in(ping.get("samples"), 0, 10 ** 9)
    server = str(data.get("server") or "")
    server = server if SERVER_RE.match(server) else None
    now = int(time.time())

    with lock, db() as c:
        row = c.execute("SELECT country FROM sessions WHERE id=?", (session,)).fetchone()
        if row is None:
            country = geo.country(ip) if ip else None  # the IP is dropped right here
            c.execute(
                "INSERT INTO sessions(id,install_id,version,started,last_seen,seconds,"
                "ping_min,ping_avg,ping_max,ping_samples,game_server,country) VALUES(?,?,?,?,?,?,?,?,?,?,?,?)",
                (session, install, version, now - seconds, now, seconds, pmin, pavg, pmax, samples, server, country),
            )
        else:
            country = row[0]
            c.execute(
                "UPDATE sessions SET last_seen=?, seconds=MAX(seconds,?),"
                " ping_min=COALESCE(?,ping_min), ping_avg=COALESCE(?,ping_avg), ping_max=COALESCE(?,ping_max),"
                " ping_samples=COALESCE(?,ping_samples), game_server=COALESCE(?,game_server)"
                " WHERE id=? AND install_id=?",
                (now, seconds, pmin, pavg, pmax, samples, server, session, install),
            )
        c.execute(
            "INSERT INTO installs(id,first_seen,last_seen,country) VALUES(?,?,?,?)"
            " ON CONFLICT(id) DO UPDATE SET last_seen=excluded.last_seen,"
            " country=COALESCE(excluded.country, installs.country)",
            (install, now, now, country),
        )
    return True


# ---------------------------------------------------------------- stats page
TEXT = {
    "en": {
        "title": "Rappelz Ping · usage", "users": "unique users (all time)", "now": "open right now",
        "d1": "last 24 hours", "d7": "last 7 days", "d30": "last 30 days", "launches": "launches",
        "total": "total usage", "avg": "average session", "versions": "Versions (last 30 days, users)",
        "version": "Version", "user": "Users", "countries": "Countries (users)", "country": "Country",
        "servers": "Game servers (last 30 days)", "server": "Server", "sessions": "Sessions",
        "avgping": "Avg ping ms", "minping": "Best ms", "empty": "No data yet", "h": "h", "m": "min",
        "foot": "Users on a VPN show up in the VPN's country. IP addresses are not stored.",
    },
    "tr": {
        "title": "Rappelz Ping · kullanım", "users": "farklı kullanıcı (toplam)", "now": "şu an açık",
        "d1": "son 24 saat", "d7": "son 7 gün", "d30": "son 30 gün", "launches": "açılış",
        "total": "toplam kullanım", "avg": "ortalama oturum", "versions": "Sürümler (son 30 gün, kullanıcı)",
        "version": "Sürüm", "user": "Kullanıcı", "countries": "Ülkeler (kullanıcı)", "country": "Ülke",
        "servers": "Oyun sunucuları (son 30 gün)", "server": "Sunucu", "sessions": "Oturum",
        "avgping": "Ort. ping ms", "minping": "En iyi ms", "empty": "Henüz veri yok", "h": "sa", "m": "dk",
        "foot": "VPN kullananlar VPN'in ülkesinde görünür. IP adresleri saklanmaz.",
    },
}


def stats_page(lang):
    t = TEXT[lang]
    now = int(time.time())
    day = 24 * 3600

    def duration(s):
        s = int(s or 0)
        h, m = s // 3600, (s % 3600) // 60
        return f"{h} {t['h']} {m} {t['m']}" if h else f"{m} {t['m']}"

    with db() as c:
        one = lambda sql, *a: c.execute(sql, a).fetchone()[0]
        active = lambda since: one("SELECT COUNT(DISTINCT install_id) FROM sessions WHERE last_seen>=?", now - since)
        cards = [
            (t["users"], one("SELECT COUNT(*) FROM installs")),
            (t["now"], one("SELECT COUNT(*) FROM sessions WHERE last_seen>=?", now - 11 * 60)),
            (t["d1"], active(day)), (t["d7"], active(7 * day)), (t["d30"], active(30 * day)),
            (t["launches"], one("SELECT COUNT(*) FROM sessions")),
            (t["total"], duration(one("SELECT COALESCE(SUM(seconds),0) FROM sessions"))),
            (t["avg"], duration(one("SELECT COALESCE(AVG(seconds),0) FROM sessions WHERE seconds>0"))),
        ]
        versions = c.execute(
            "SELECT COALESCE(version,'?'), COUNT(DISTINCT install_id) FROM sessions WHERE last_seen>=?"
            " GROUP BY 1 ORDER BY 2 DESC", (now - 30 * day,)).fetchall()
        countries = c.execute(
            "SELECT COALESCE(country,'?'), COUNT(*) FROM installs GROUP BY 1 ORDER BY 2 DESC LIMIT 25").fetchall()
        servers = c.execute(
            "SELECT game_server, COUNT(*), CAST(ROUND(AVG(ping_avg)) AS INTEGER), MIN(ping_min) FROM sessions"
            " WHERE game_server IS NOT NULL AND started>=? GROUP BY 1 ORDER BY 2 DESC LIMIT 10",
            (now - 30 * day,)).fetchall()

    def table(title, rows, cols):
        if not rows:
            return f"<h2>{title}</h2><p class=muted>{t['empty']}</p>"
        head = "".join(f"<th>{html.escape(c)}</th>" for c in cols)
        body = "".join("<tr>" + "".join(
            f"<td>{html.escape(str(v if v is not None else '–'))}</td>" for v in r) + "</tr>" for r in rows)
        return f"<h2>{title}</h2><table><tr>{head}</tr>{body}</table>"

    cards_html = "".join(f"<div class=card><b>{html.escape(str(v))}</b><span>{k}</span></div>" for k, v in cards)
    stamp = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M UTC")
    return f"""<!doctype html><html lang={lang}><head><meta charset=utf-8>
<meta name=viewport content="width=device-width,initial-scale=1"><meta name=robots content=noindex>
<title>{t['title']}</title><style>
body{{font:15px/1.5 system-ui,"Segoe UI",sans-serif;background:#121214;color:#e6e6e8;margin:0;padding:24px 16px}}
main{{max-width:760px;margin:auto}} h1{{font-size:20px;margin:0 0 16px}}
h2{{font-size:15px;margin:28px 0 8px;color:#a0a0a8}}
.cards{{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:10px}}
.card{{background:#1d1d21;border-radius:8px;padding:12px 14px}}
.card b{{display:block;font-size:24px;color:#50dc6e}} .card span{{color:#a0a0a8;font-size:13px}}
table{{border-collapse:collapse;width:100%}} td,th{{text-align:left;padding:6px 8px;border-bottom:1px solid #2a2a30}}
th{{color:#a0a0a8;font-weight:500}} .muted,footer{{color:#77777f;font-size:13px}} footer{{margin-top:32px}}
a{{color:#a0a0a8}}
</style></head><body><main>
<h1>{t['title']}</h1><div class=cards>{cards_html}</div>
{table(t['versions'], versions, [t['version'], t['user']])}
{table(t['countries'], countries, [t['country'], t['user']])}
{table(t['servers'], servers, [t['server'], t['sessions'], t['avgping'], t['minping']])}
<footer>{stamp} · {t['foot']} <a href="https://db-ip.com">IP Geolocation by DB-IP</a> (CC BY 4.0)</footer>
</main></body></html>"""


# ---------------------------------------------------------------- HTTP
class Handler(BaseHTTPRequestHandler):
    server_version = "rp"
    sys_version = ""

    def log_message(self, *args):  # no access log, so no IPs
        pass

    def reply(self, code, body=b"", ctype="text/plain; charset=utf-8"):
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def client_ip(self):
        return self.headers.get("X-Real-IP") or self.client_address[0]

    def do_POST(self):
        if self.path != "/api/event":
            return self.reply(404)
        size = int(self.headers.get("Content-Length") or 0)
        if size <= 0 or size > 2048:
            return self.reply(413)
        ip = self.client_ip()
        if rate_limited(ip):
            return self.reply(429)
        try:
            data = json.loads(self.rfile.read(size))
            ok = isinstance(data, dict) and record_event(data, ip)
        except Exception:
            ok = False
        self.reply(204 if ok else 400)

    def do_GET(self):
        if self.path == "/health":
            return self.reply(200, b"ok")
        if STATS_KEY and secrets.compare_digest(self.path.encode(), f"/s/{STATS_KEY}".encode()):
            lang = "tr" if (self.headers.get("Accept-Language") or "").lower().startswith("tr") else "en"
            return self.reply(200, stats_page(lang).encode("utf-8"), "text/html; charset=utf-8")
        self.reply(404)


if __name__ == "__main__":
    os.makedirs(DATA_DIR, exist_ok=True)
    db_init()
    STATS_KEY = stats_key()
    geo.load()
    threading.Thread(target=geo_loop, daemon=True).start()
    print(f"rappelzping counter on 127.0.0.1:{PORT}", flush=True)
    ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
