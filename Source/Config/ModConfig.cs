using System;
using BepInEx.Configuration;
using UnityEngine;

namespace WorkableBoombox.Config
{
    public static class ModConfig
    {
        // General
        public static ConfigEntry<bool> EnableMod;

        // Keybindings
        public static ConfigEntry<KeyCode> ToggleKey;
        public static ConfigEntry<KeyCode> NextTrackKey;
        public static ConfigEntry<KeyCode> LoopModeKey;
        public static ConfigEntry<KeyCode> ShuffleKey;
        public static ConfigEntry<KeyCode> VolumeKey;
        public static ConfigEntry<KeyCode> PlaylistKey;
        public static ConfigEntry<float> ScrubStepSeconds;
        public static ConfigEntry<float> VolumeStepPercent;

        // Audio & Visual Settings
        public static ConfigEntry<float> BoomboxVolume;
        public static ConfigEntry<float> AudioMaxDistance;
        public static ConfigEntry<bool> EnableCameraShake;
        public static ConfigEntry<bool> EnableVisualPulsing;
        public static ConfigEntry<bool> EnableLightMusic;

        // UI & Overlay Settings
        public static ConfigEntry<float> OverlayPositionX;
        public static ConfigEntry<float> OverlayPositionY;

        // Playback Defaults
        public static ConfigEntry<int> DefaultLoopMode;
        public static ConfigEntry<bool> DefaultShuffle;

        // Shop / Item Settings
        public static ConfigEntry<int> ShopPrice;
        public static ConfigEntry<string> CustomItemName;
        public static ConfigEntry<bool> AlwaysSpawnInTruck;

        public static void Initialize(ConfigFile config)
        {
            // General
            EnableMod = config.Bind("General", "EnableMod", true, "Enable/disable the Workable Boombox mod");

            // Keybindings
            ToggleKey = config.Bind("Controls", "ToggleKey", KeyCode.E, "Toggle playback on/off key");
            NextTrackKey = config.Bind("Controls", "NextTrackKey", KeyCode.F, "Next track key ([Hold F] + Scroll to scrub time)");
            LoopModeKey = config.Bind("Controls", "LoopModeKey", KeyCode.R, "Cycle loop mode key (Off -> Playlist -> Single)");
            ShuffleKey = config.Bind("Controls", "ShuffleKey", KeyCode.T, "Toggle shuffle playback key");
            VolumeKey = config.Bind("Controls", "VolumeKey", KeyCode.V, "Volume key: [V] - Mute, [Hold V + Scroll] - Adjust volume");
            PlaylistKey = config.Bind("Controls", "PlaylistKey", KeyCode.P, "Open playlist menu key");
            ScrubStepSeconds = config.Bind("Controls", "ScrubStepSeconds", 5f, "Seconds to scrub per mouse wheel step");
            VolumeStepPercent = config.Bind("Controls", "VolumeStepPercent", 5f, "Volume percentage change per mouse wheel step (1-20%)");

            // Audio & Visuals
            BoomboxVolume = config.Bind("Audio", "BoomboxVolume", 1.0f, "Boombox master volume (from 0.0 to 1.0)");
            AudioMaxDistance = config.Bind("Audio", "AudioMaxDistance", 25.0f, "Maximum audio hearing distance in meters");
            EnableCameraShake = config.Bind("Visuals", "EnableCameraShake", true, "Enable camera shake when standing close to loud music");
            EnableVisualPulsing = config.Bind("Visuals", "EnableVisualPulsing", true, "Enable boombox speakers and body pulsing to the music beat");
            EnableLightMusic = config.Bind("Visuals", "EnableLightMusic", true, "Enable boombox disco lighting and speaker glow / Свето-музыка");

            // UI & Overlay
            OverlayPositionX = config.Bind("UI", "OverlayPositionX", 0.5f, "Horizontal position of HUD overlay: 0.0 (left) ... 0.5 (center) ... 1.0 (right)");
            OverlayPositionY = config.Bind("UI", "OverlayPositionY", 0.6f, "Vertical position of HUD overlay: 0.0 (top) ... 0.5 (center) ... 1.0 (bottom)");

            // Playback Defaults
            DefaultLoopMode = config.Bind("Playback", "DefaultLoopMode", 1, "Default loop mode: 0 = Off (stops at end), 1 = Entire Playlist, 2 = Single Track");
            DefaultShuffle = config.Bind("Playback", "DefaultShuffle", true, "Whether to shuffle playlist tracks by default");

            // Shop / Item
            ShopPrice = config.Bind("Shop", "ShopPrice", 15, "Purchase price of the boombox in the shop (default: 15)");
            CustomItemName = config.Bind("Shop", "CustomItemName", "Workable Boombox", "In-game display name for the item");
            AlwaysSpawnInTruck = config.Bind("Spawn", "AlwaysSpawnInTruck", true, "Always automatically spawn a boombox inside the truck");
        }
    }
}
