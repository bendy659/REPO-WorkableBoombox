using System;
using UnityEngine;

namespace WorkableBoombox.Items
{
    public static class BoomboxStateMemory
    {
        public static bool HasSavedState { get; private set; }
        public static int SavedTrackIndex { get; private set; } = -1;
        public static float SavedTime { get; set; }
        public static float SavedVolume { get; private set; } = 1.0f;
        public static bool SavedIsMuted { get; private set; }
        public static CustomBoomboxItem.LoopMode SavedLoopMode { get; private set; } = CustomBoomboxItem.LoopMode.Playlist;
        public static bool SavedShuffle { get; private set; } = true;
        public static bool SavedIsPlaying { get; private set; }

        public static bool SavedIsPlaylistMode { get; private set; }
        public static string SavedPlaylistOwner { get; private set; }
        public static string SavedPlaylistName { get; private set; }

        public static void Save(CustomBoomboxItem boombox)
        {
            if (boombox == null)
            {
                return;
            }

            HasSavedState = true;
            SavedTrackIndex = boombox.CurrentTrackIndex;
            SavedTime = boombox.GetCurrentPlaybackTime();
            SavedVolume = boombox.CurrentVolume;
            SavedIsMuted = boombox.IsMuted;
            SavedLoopMode = boombox.CurrentLoopMode;
            SavedShuffle = boombox.ShuffleEnabled;
            SavedIsPlaying = boombox.IsPlaying;

            SavedIsPlaylistMode = boombox.IsPlaylistMode;
            SavedPlaylistOwner = boombox.ActivePlaylistOwner;
            SavedPlaylistName = boombox.ActivePlaylistName;
        }

        public static void Restore(CustomBoomboxItem boombox)
        {
            if (!HasSavedState || boombox == null)
            {
                return;
            }

            boombox.RestoreSavedState(
                SavedTrackIndex,
                SavedTime,
                SavedVolume,
                SavedIsMuted,
                SavedLoopMode,
                SavedShuffle,
                SavedIsPlaying,
                SavedIsPlaylistMode,
                SavedPlaylistOwner,
                SavedPlaylistName
            );
        }
    }
}
