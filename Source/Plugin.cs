using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using WorkableBoombox.Audio;
using WorkableBoombox.Config;
using WorkableBoombox.Interaction;
using WorkableBoombox.Items;
using WorkableBoombox.Playlists;
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
        public const string ModVersion = "1.1.3";

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

                // Патч для RepoAdminMenu если он установлен
                try
                {
                    Type itemUtilType = AccessTools.TypeByName("RepoAdminMenu.Utils.ItemUtil");
                    if (itemUtilType != null)
                    {
                        var spawnMethod = AccessTools.Method(itemUtilType, "spawnItem");
                        var postfixMethod = AccessTools.Method(typeof(WorkableBoombox.Patches.SpawnPatches), nameof(WorkableBoombox.Patches.SpawnPatches.RepoAdminMenu_SpawnItem_Postfix));
                        if (spawnMethod != null && postfixMethod != null)
                        {
                            HarmonyInstance.Patch(spawnMethod, postfix: new HarmonyMethod(postfixMethod));
                            Log.LogInfo("[Harmony] Патч для RepoAdminMenu.ItemUtil.spawnItem успешно применен.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.LogWarning($"[Harmony] Не удалось пропатчить RepoAdminMenu: {ex.Message}");
                }

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
            systemsHost.AddComponent<YouTubeAudioProvider>();
            systemsHost.AddComponent<PlaylistManager>();
            systemsHost.AddComponent<BoomboxPlaylistUI>();
            systemsHost.AddComponent<BoomboxHUD>();
            systemsHost.AddComponent<BoomboxInteraction>();
            systemsHost.AddComponent<BoomboxTruckSpawner>();

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
