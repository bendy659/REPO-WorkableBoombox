using System;
using BepInEx.Configuration;
using UnityEngine;

namespace WorkableBoombox.Config
{
    public static class ModConfig
    {
        // General
        public static ConfigEntry<bool> EnableMod;
        public static ConfigEntry<string> Language;

        // Keybindings
        public static ConfigEntry<KeyCode> ToggleKey;
        public static ConfigEntry<KeyCode> NextTrackKey;
        public static ConfigEntry<KeyCode> LoopModeKey;
        public static ConfigEntry<KeyCode> ShuffleKey;
        public static ConfigEntry<KeyCode> VolumeKey;
        public static ConfigEntry<float> ScrubStepSeconds;
        public static ConfigEntry<float> VolumeStepPercent;

        // Audio & Visual Settings
        public static ConfigEntry<float> BoomboxVolume;
        public static ConfigEntry<float> AudioMaxDistance;
        public static ConfigEntry<bool> EnableCameraShake;
        public static ConfigEntry<bool> EnableVisualPulsing;

        // UI & Overlay Settings
        public static ConfigEntry<float> OverlayPositionX;
        public static ConfigEntry<float> OverlayPositionY;

        // Playback Defaults
        public static ConfigEntry<int> DefaultLoopMode;
        public static ConfigEntry<bool> DefaultShuffle;

        // Shop / Item Settings
        public static ConfigEntry<int> ShopPrice;
        public static ConfigEntry<string> CustomItemName;

        // GitHub Sync Settings
        public static ConfigEntry<bool> GitHubSyncEnabled;
        public static ConfigEntry<string> GitHubSource;

        public static void Initialize(ConfigFile config)
        {
            // General
            EnableMod = config.Bind("General", "EnableMod", true, "Включить/выключить мод Workable Boombox / Enable mod");
            Language = config.Bind("General", "Language", "Auto", "Язык интерфейса: Auto (автоопределение из игры), Russian, English");

            // Keybindings
            ToggleKey = config.Bind("Controls", "ToggleKey", KeyCode.E, "Клавиша включения/выключения воспроизведения / Toggle play key");
            NextTrackKey = config.Bind("Controls", "NextTrackKey", KeyCode.F, "Клавиша переключения трека / Next track key");
            LoopModeKey = config.Bind("Controls", "LoopModeKey", KeyCode.R, "Клавиша смены режима зацикливания / Cycle loop mode key");
            ShuffleKey = config.Bind("Controls", "ShuffleKey", KeyCode.T, "Клавиша включения/выключения перемешивания / Toggle shuffle key");
            VolumeKey = config.Bind("Controls", "VolumeKey", KeyCode.V, "Клавиша управления громкостью: [V] - Mute, [Зажать V + Колёсико] - Регулировка / Volume key");
            ScrubStepSeconds = config.Bind("Controls", "ScrubStepSeconds", 5f, "Секунд для перемотки колесиком мыши / Scrub step seconds");
            VolumeStepPercent = config.Bind("Controls", "VolumeStepPercent", 5f, "Процент изменения громкости за один щелчок колесика мыши (1-20%) / Volume step percent");

            // Audio & Visuals
            BoomboxVolume = config.Bind("Audio", "BoomboxVolume", 1.0f, "Громкость воспроизведения бумбокса (от 0.0 до 1.0) / Boombox volume");
            AudioMaxDistance = config.Bind("Audio", "AudioMaxDistance", 25.0f, "Максимальная дистанция слышимости музыки (в метрах) / Audio max distance");
            EnableCameraShake = config.Bind("Visuals", "EnableCameraShake", true, "Включить/выключить тряску камеры при нахождении рядом с громкой музыкой / Enable camera shake");
            EnableVisualPulsing = config.Bind("Visuals", "EnableVisualPulsing", true, "Включить/выключить пульсацию колонок и корпуса бумбокса в такт звуку / Enable boombox pulsing");

            // UI & Overlay
            OverlayPositionX = config.Bind("UI", "OverlayPositionX", 0.5f, "Горизонтальное положение оверлея подсказок: 0.0 (слева / align-start) ... 0.5 (по центру) ... 1.0 (справа / align-end)");
            OverlayPositionY = config.Bind("UI", "OverlayPositionY", 0.6f, "Вертикальное положение оверлея подсказок: 0.0 (сверху / align-start) ... 0.5 (по центру) ... 1.0 (снизу / align-end)");

            // Playback Defaults
            DefaultLoopMode = config.Bind("Playback", "DefaultLoopMode", 1, "Режим зацикливания по умолчанию: 0 = Выкл (останавливается в конце), 1 = Весь плейлист, 2 = Один трек");
            DefaultShuffle = config.Bind("Playback", "DefaultShuffle", true, "Перемешивать ли треки по умолчанию (случайный порядок)");

            // Shop / Item
            ShopPrice = config.Bind("Shop", "ShopPrice", 45, "Цена покупки бумбокса в магазине");
            CustomItemName = config.Bind("Shop", "CustomItemName", "Workable Boombox", "Название предмета в игре");

            // GitHub Sync
            GitHubSyncEnabled = config.Bind("GitHub", "GitHubSyncEnabled", true, "Автоматически синхронизировать треки с GitHub при запуске игры");
            GitHubSource = config.Bind("GitHub", "GitHubSource", "", "Inline-ссылка в формате: owner/repo/token (например: bendy659/repo-workableboombox-tracks/github_pat_...). Треки качаются напрямую из корня репо без подпапок.");
        }

        public static bool TryParseGitHubSource(string input, out string owner, out string repo, out string token)
        {
            owner = null;
            repo = null;
            token = null;

            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            string clean = input.Trim().TrimEnd('/');
            if (clean.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
            {
                clean = clean.Substring("https://github.com/".Length);
            }

            string[] parts = clean.Split(new[] { '/' }, 3);
            if (parts.Length >= 2)
            {
                owner = parts[0].Trim();
                repo = parts[1].Trim();
                token = parts.Length >= 3 ? parts[2].Trim() : null;
                return !string.IsNullOrEmpty(owner) && !string.IsNullOrEmpty(repo);
            }

            return false;
        }
    }
}
