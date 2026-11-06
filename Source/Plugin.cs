using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using WorkableBoombox.Audio;
using WorkableBoombox.Config;
using WorkableBoombox.Interaction;
using WorkableBoombox.Items;
using WorkableBoombox.UI;

namespace WorkableBoombox
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    [BepInDependency("REPOLib", BepInDependency.DependencyFlags.HardDependency)]
    [BepInProcess("REPO.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGUID = "com.bendy659.workableboombox";
        public const string ModName = "Workable Boombox";
        public const string ModVersion = "1.0.0";

        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }
        public static Harmony HarmonyInstance { get; private set; }

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            // 1. Инициализация конфига
            ModConfig.Initialize(Config);

            if (!ModConfig.EnableMod.Value)
            {
                Log.LogInfo($"{ModName} отключен в конфигурации.");
                return;
            }

            // 2. Harmony патчи
            try
            {
                HarmonyInstance = new Harmony(ModGUID);
                HarmonyInstance.PatchAll();
                Log.LogInfo("[Harmony] Патчи успешно применены.");
            }
            catch (Exception ex)
            {
                Log.LogError($"[Harmony] Ошибка применения патчей: {ex}");
            }

            // 3. Создание вспомогательных систем
            GameObject systemsHost = new GameObject("WorkableBoombox_SystemsHost");
            DontDestroyOnLoad(systemsHost);

            systemsHost.AddComponent<BoomboxAudioManager>();
            systemsHost.AddComponent<GitHubTrackDownloader>();
            systemsHost.AddComponent<BoomboxHUD>();
            systemsHost.AddComponent<BoomboxInteraction>();

            // 4. Запуск фоновой синхронизации с GitHub (если настроено)
            if (ModConfig.GitHubSyncEnabled.Value)
            {
                GitHubTrackDownloader.Instance.StartSync();
            }

            // 5. Инициализация сборщика предметов
            BoomboxItemBuilder.Initialize();
            StartCoroutine(BoomboxItemBuilder.SearchRoutine());

            Log.LogInfo($"{ModName} v{ModVersion} успешно загружен!");
        }
    }
}
