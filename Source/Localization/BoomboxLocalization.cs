using System;

namespace WorkableBoombox.Localization
{
    public static class BoomboxLocalization
    {
        public static bool IsRussian => false;

        public static string BoomboxTitle => "BOOMBOX";
        public static string StatusPlaying => "Playing";
        public static string StatusOff => "Off";

        public static string LoopModeOff => "Off";
        public static string LoopModePlaylist => "Playlist";
        public static string LoopModeSingle => "Single";

        public static string ShuffleOn => "On";
        public static string ShuffleOff => "Off";

        public static string NotificationLoop(string mode) => $"Loop Mode: [{mode}]";
        public static string NotificationShuffle(bool enabled) => $"Shuffle: [{(enabled ? "ON" : "OFF")}]";
        public static string NotificationVolume(int percent, bool muted) => muted ? "[ 🔇 Muted ]" : $"[ 🔊 Volume: {percent}% ]";

        public static string PromptRow1(string eKey, string fKey, string pKey) => $"[{eKey}] Play/Pause   |   [{fKey}] Next Track   |   [{pKey}] Playlists";
        public static string PromptRow2(string rKey, string loopStr, string tKey, string shuffleStr) => $"[{rKey}] Loop: {loopStr}   |   [{tKey}] Shuffle: {shuffleStr}";
        public static string PromptRow3(string vKey, int volumePercent, bool isMuted) => $"[{vKey}] Mute ({(isMuted ? "Muted" : volumePercent + "%")})   |   [Hold {vKey} + Scroll] Volume";
        public static string PromptRow4(string fKey) => $"[Hold {fKey} + Scroll] Scrub Time (-/+ 5s)";

        public static string ItemDescription => "A workable portable boombox that plays custom music.";
    }
}
