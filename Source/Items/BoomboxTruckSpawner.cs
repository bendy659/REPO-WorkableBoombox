#pragma warning disable CS0618
using System;
using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;
using WorkableBoombox.Config;

namespace WorkableBoombox.Items
{
    public class BoomboxTruckSpawner : MonoBehaviour
    {
        public static BoomboxTruckSpawner Instance { get; private set; }

        private float checkCooldown = 1.5f;
        private bool hasSpawnedForCurrentTruck;
        private Level lastKnownLevel;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            hasSpawnedForCurrentTruck = false;
            checkCooldown = 1.5f;
        }

        private void Update()
        {
            // Отслеживаем смену уровня в RunManager (Lobby -> Run -> Shop -> etc.)
            if (RunManager.instance != null && RunManager.instance.levelCurrent != lastKnownLevel)
            {
                lastKnownLevel = RunManager.instance.levelCurrent;
                hasSpawnedForCurrentTruck = false;
                checkCooldown = 1.5f;
            }

            if (hasSpawnedForCurrentTruck)
            {
                return;
            }

            if (ModConfig.AlwaysSpawnInTruck != null && !ModConfig.AlwaysSpawnInTruck.Value)
            {
                return;
            }

            // Не спавним бумбокс в главном меню или меню выбора лобби
            if (SemiFunc.IsMainMenu() || SemiFunc.RunIsLobbyMenu())
            {
                return;
            }

            // Проверяем готовность сети
            if (SemiFunc.IsMultiplayer())
            {
                if (!PhotonNetwork.InRoom)
                {
                    // Еще подключаемся к комнате — ждем завершения подключения
                    return;
                }

                if (!PhotonNetwork.IsMasterClient)
                {
                    // Клиенты не спавнят сетевые предметы
                    hasSpawnedForCurrentTruck = true;
                    return;
                }
            }

            checkCooldown -= Time.deltaTime;
            if (checkCooldown > 0f)
            {
                return;
            }

            checkCooldown = 2.0f;

            if (!BoomboxItemBuilder.IsRegistered || BoomboxItemBuilder.RegisteredPrefab == null)
            {
                return;
            }

            Vector3? truckPos = GetTruckSpawnPosition(out Quaternion truckRot);
            if (!truckPos.HasValue)
            {
                return;
            }

            // Проверяем, есть ли уже активный бумбокс рядом с платформой трака (радиус 5.0м)
            bool alreadyInTruck = false;
            foreach (CustomBoomboxItem b in CustomBoomboxItem.ActiveBoomboxes)
            {
                if (b != null && b.gameObject.activeInHierarchy && b.gameObject != BoomboxItemBuilder.RegisteredPrefab)
                {
                    if (Vector3.Distance(b.transform.position, truckPos.Value) < 5.0f)
                    {
                        alreadyInTruck = true;
                        break;
                    }
                }
            }

            if (alreadyInTruck)
            {
                hasSpawnedForCurrentTruck = true;
                return;
            }

            GameObject spawned = SpawnBoomboxInTruck(truckPos.Value, truckRot);
            if (spawned != null)
            {
                hasSpawnedForCurrentTruck = true;
            }
        }

        public static Vector3? GetTruckSpawnPosition(out Quaternion rotation)
        {
            rotation = Quaternion.identity;

            // 1. Приоритет: точка спавна игроков внутри грузовика
            TruckSafetySpawnPoint sp = TruckSafetySpawnPoint.instance ?? UnityEngine.Object.FindObjectOfType<TruckSafetySpawnPoint>();
            if (sp != null)
            {
                rotation = sp.transform.rotation * Quaternion.Euler(0f, 180f, 0f);
                return sp.transform.position + Vector3.up * 0.25f;
            }

            // 2. Дверь грузовика
            TruckDoor door = UnityEngine.Object.FindObjectOfType<TruckDoor>();
            if (door != null)
            {
                rotation = door.transform.rotation * Quaternion.Euler(0f, 180f, 0f);
                return door.transform.position + door.transform.forward * 1.5f + Vector3.up * 0.25f;
            }

            // 3. Запасной вариант: платформа эвакуации
            ExtractionPoint ext = UnityEngine.Object.FindObjectOfType<ExtractionPoint>();
            if (ext != null)
            {
                if (ext.platform != null)
                {
                    rotation = ext.platform.rotation * Quaternion.Euler(0f, 180f, 0f);
                    // Спавним прямо на поверхность платформы (0.08f над центроидом), исключая зависание в воздухе на клиентах
                    return ext.platform.position + ext.platform.up * 0.08f + ext.platform.right * 0.85f;
                }
                if (ext.safetySpawn != null)
                {
                    rotation = ext.safetySpawn.rotation * Quaternion.Euler(0f, 180f, 0f);
                    return ext.safetySpawn.position + Vector3.up * 0.08f + ext.safetySpawn.right * 0.6f;
                }
            }

            return null;
        }

        public static GameObject SpawnBoomboxInTruck(Vector3 position, Quaternion rotation)
        {
            if (BoomboxItemBuilder.RegisteredPrefab == null)
            {
                return null;
            }

            try
            {
                GameObject spawned = null;
                if (BoomboxItemBuilder.RegisteredItemSO != null)
                {
                    spawned = REPOLib.Modules.Items.SpawnItem(BoomboxItemBuilder.RegisteredItemSO, position, rotation);
                }

                if (spawned == null)
                {
                    if (SemiFunc.IsMultiplayer() && PhotonNetwork.IsMasterClient)
                    {
                        spawned = PhotonNetwork.InstantiateRoomObject("WorkableBoomboxItem", position, rotation, 0, null);
                    }
                    else
                    {
                        spawned = UnityEngine.Object.Instantiate(BoomboxItemBuilder.RegisteredPrefab, position, rotation);
                    }
                }

                if (spawned != null)
                {
                    if (!spawned.activeSelf)
                    {
                        spawned.SetActive(true);
                    }
                    Plugin.Log.LogInfo($"[BoomboxTruckSpawner] Бумбокс успешно заспавнен в грузовике на позиции: {position}");
                    return spawned;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[BoomboxTruckSpawner] Ошибка при спавне бумбокса в грузовике: {ex}");
            }

            return null;
        }
    }
}
