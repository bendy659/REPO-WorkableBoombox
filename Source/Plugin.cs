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
        public const string ModVersion = "1.1.4";

        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }
        public static Harmony HarmonyInstance { get; private set; }

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            // 1. Config initialization
            ModConfig.Initialize(Config);

            if (!ModConfig.EnableMod.Value)
            {
                Log.LogInfo($"{ModName} is disabled in config.");
                return;
            }

            // 2. Harmony patches
            try
            {
                HarmonyInstance = new Harmony(ModGUID);
                HarmonyInstance.PatchAll();

                // Patch for RepoAdminMenu if present
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
                            Log.LogInfo("[Harmony] Patch for RepoAdminMenu.ItemUtil.spawnItem applied.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.LogWarning($"[Harmony] Failed to patch RepoAdminMenu: {ex.Message}");
                }

                Log.LogInfo("[Harmony] Patches applied successfully.");
            }
            catch (Exception ex)
            {
                Log.LogError($"[Harmony] Error applying patches: {ex}");
            }

            // 3. Systems host
            GameObject systemsHost = new GameObject("WorkableBoombox_SystemsHost");
            DontDestroyOnLoad(systemsHost);

            systemsHost.AddComponent<BoomboxAudioManager>();
            systemsHost.AddComponent<YouTubeAudioProvider>();
            systemsHost.AddComponent<PlaylistManager>();
            systemsHost.AddComponent<BoomboxPlaylistUI>();
            systemsHost.AddComponent<BoomboxHUD>();
            systemsHost.AddComponent<BoomboxInteraction>();
            systemsHost.AddComponent<BoomboxTruckSpawner>();

            // 4. Boombox item builder
            BoomboxItemBuilder.Initialize();
            StartCoroutine(BoomboxItemBuilder.SearchRoutine());

            Log.LogInfo($"{ModName} v{ModVersion} loaded successfully!");
        }
    }
}
