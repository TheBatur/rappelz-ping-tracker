// UI text. English by default, Turkish when Windows' display language is Turkish.
using System.Globalization;

namespace RappelzPing
{
    static class T
    {
        public static bool Turkish = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr";

        static string S(string en, string tr) { return Turkish ? tr : en; }

        public static string NoGame { get { return S("no game", "oyun yok"); } }
        public static string NotConnected { get { return S("offline", "bağlantı yok"); } }
        public static string Measuring { get { return S("measuring", "ölçülüyor"); } }
        public static string NoAdmin { get { return S("no admin", "izin yok"); } }

        public static string TipNoGame { get { return S("Rappelz (" + PingMeter.ProcessName + ".exe) is not running",
                                                        "Rappelz (" + PingMeter.ProcessName + ".exe) açık değil"); } }
        public static string TipNotConnected { get { return S("The game is running but not connected to a server",
                                                              "Oyun açık ama bir sunucuya bağlı değil"); } }
        public static string TipMeasuring { get { return S("Connection found, waiting for the first sample",
                                                           "Bağlantı bulundu, ilk ölçüm bekleniyor"); } }
        public static string TipNoAdmin { get { return S("Run Rappelz Ping as administrator",
                                                         "Rappelz Ping'i yönetici olarak çalıştır"); } }
        public static string TipServer { get { return S("Server ", "Sunucu "); } }

        public static string MenuReset { get { return S("Reset position", "Konumu sıfırla"); } }
        public static string MenuUsage { get { return S("Send anonymous usage stats", "Anonim kullanım sayısı gönder"); } }
        public static string MenuAbout { get { return S("About / source code", "Hakkında / kaynak kod"); } }
        public static string MenuClose { get { return S("Close", "Kapat"); } }

        public static string ConsentTitle { get { return "Rappelz Ping"; } }
        public static string ConsentText
        {
            get
            {
                return S(
                    "Rappelz Ping sends anonymous usage counts so we can see how many people use it:\n" +
                    "a random install ID, the app version, how long it runs and a ping summary " +
                    "(lowest / average / highest and the game server address).\n\n" +
                    "Your IP address, name, computer details and game account are never stored.\n" +
                    "You can turn this off at any time from the right-click menu.",
                    "Rappelz Ping, kaç kişinin kullandığını görebilmemiz için anonim kullanım sayısı gönderir:\n" +
                    "rastgele bir kurulum kimliği, sürüm, ne kadar süre açık kaldığı ve ping özeti " +
                    "(en düşük / ortalama / en yüksek ve oyun sunucusunun adresi).\n\n" +
                    "IP adresin, adın, bilgisayar bilgilerin ve oyun hesabın asla saklanmaz.\n" +
                    "Bunu istediğin zaman sağ tık menüsünden kapatabilirsin.");
            }
        }
        public static string ConsentOk { get { return S("OK", "Tamam"); } }
        public static string ConsentNo { get { return S("Don't send", "Gönderme"); } }
    }
}
