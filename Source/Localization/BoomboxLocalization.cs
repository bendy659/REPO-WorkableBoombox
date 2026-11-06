using System;
using System.Globalization;
using WorkableBoombox.Config;

namespace WorkableBoombox.Localization
{
    public static class BoomboxLocalization
    {
        public static bool IsRussian
        {
            get
            {
                string cfg = ModConfig.Language != null ? ModConfig.Language.Value : "Auto";
                if (string.Equals(cfg, "Russian", StringComparison.OrdinalIgnoreCase) || string.Equals(cfg, "ru", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.Equals(cfg, "English", StringComparison.OrdinalIgnoreCase) || string.Equals(cfg, "en", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Auto: определяем язык игры через LocalizationManager
                if (LocalizationManager.instance != null && LocalizationManager.instance.ActiveLocale != null)
                {
                    string native = LocalizationManager.instance.ActiveLocale.NativeName;
                    if (!string.IsNullOrEmpty(native) && native.IndexOf("рус", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }

                    var locRef = LocalizationManager.instance.ActiveLocale.LocaleReference;
                    if (locRef != null && locRef.Identifier.Code != null && locRef.Identifier.Code.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                string sysLang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                return string.Equals(sysLang, "ru", StringComparison.OrdinalIgnoreCase);
            }
        }

        public static string BoomboxTitle => IsRussian ? "БУМБОКС" : "BOOMBOX";
        public static string StatusPlaying => IsRussian ? "Играет" : "Playing";
        public static string StatusOff => IsRussian ? "Выключен" : "Off";

        public static string LoopModeOff => IsRussian ? "Выкл" : "Off";
        public static string LoopModePlaylist => IsRussian ? "Цикл" : "Playlist";
        public static string LoopModeSingle => IsRussian ? "Единый цикл" : "Single";

        public static string ShuffleOn => IsRussian ? "Вкл" : "On";
        public static string ShuffleOff => IsRussian ? "Выкл" : "Off";

        public static string NotificationLoop(string mode) => IsRussian ? $"Режим зацикливания: [{mode}]" : $"Loop Mode: [{mode}]";
        public static string NotificationShuffle(bool enabled) => IsRussian ? $"Смешивание треков: [{(enabled ? "ВКЛ (Смешивание)" : "ВЫКЛ (По порядку)")}]" : $"Track Shuffle: [{(enabled ? "ON (Shuffle)" : "OFF (Sequential)")}]";
        public static string NotificationVolume(int percent, bool muted) => muted ? (IsRussian ? "[ 🔇 Без звука ]" : "[ 🔇 Muted ]") : (IsRussian ? $"[ 🔊 Громкость: {percent}% ]" : $"[ 🔊 Volume: {percent}% ]");

        public static string PromptRow1(string eKey, string fKey, string pKey) => IsRussian ? $"[{eKey}] Вкл/Выкл   |   [{fKey}] След. трек   |   [{pKey}] Плейлисты" : $"[{eKey}] Toggle On/Off   |   [{fKey}] Next Track   |   [{pKey}] Playlists";
        public static string PromptRow2(string rKey, string loopStr, string tKey, string shuffleStr) => IsRussian ? $"[{rKey}] Зацикливание: {loopStr}   |   [{tKey}] Смешивание: {shuffleStr}" : $"[{rKey}] Loop: {loopStr}   |   [{tKey}] Shuffle: {shuffleStr}";
        public static string PromptRow3(string vKey, int volumePercent, bool isMuted) => IsRussian ? $"[{vKey}] Звук: {(isMuted ? "Без звука" : volumePercent + "%")}   |   [Зажать {vKey} + Колёсико] Громкость" : $"[{vKey}] Sound: {(isMuted ? "Muted" : volumePercent + "%")}   |   [Hold {vKey} + Scroll] Volume";
        public static string PromptRow4(string fKey) => IsRussian ? $"[Зажать {fKey} + Колёсико] Перемотка" : $"[Hold {fKey} + Scroll] Scrub Time";

        public static string ItemDescription => IsRussian ? "Рабочий портативный бумбокс для музыки." : "A working portable boombox that plays custom music.";
    }
}
