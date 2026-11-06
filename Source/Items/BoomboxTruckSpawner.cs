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

        private float checkCooldown = 2.0f;
        private bool hasSpawnedForCurrentTruck;

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
            checkCooldown = 1.8f; // Даем сцене и префабам 1.8 секунды на полную инициализацию
        }

        private void Update()
        {
            if (hasSpawnedForCurrentTruck)
            {
                return;
            }

            if (ModConfig.AlwaysSpawnInTruck != null && !ModConfig.AlwaysSpawnInTruck.Value)
            {
                return;
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

            // Спавнить в сеть должен только хост (MasterClient) либо игрок в одиночной игре
            if (!SemiFunc.IsMasterClientOrSingleplayer())
            {
                hasSpawnedForCurrentTruck = true;
                return;
            }

            Vector3? truckPos = GetTruckSpawnPosition(out Quaternion truckRot);
            if (!truckPos.HasValue)
            {
                return;
            }

            // Проверяем, есть ли уже активный бумбокс рядом с платформой трака (радиус 4.5м)
            bool alreadyInTruck = false;
            foreach (CustomBoomboxItem b in CustomBoomboxItem.ActiveBoomboxes)
            {
                if (b != null && b.gameObject.activeInHierarchy && b.gameObject != BoomboxItemBuilder.RegisteredPrefab)
                {
                    if (Vector3.Distance(b.transform.position, truckPos.Value) < 4.5f)
                    {
                        alreadyInTruck = true;
                        break;
                    }
                }
            }

            if (!alreadyInTruck)
            {
                SpawnBoomboxInTruck(truckPos.Value, truckRot);
            }

            hasSpawnedForCurrentTruck = true;
        }

        public static Vector3? GetTruckSpawnPosition(out Quaternion rotation)
        {
            rotation = Quaternion.identity;

            ExtractionPoint ext = UnityEngine.Object.FindObjectOfType<ExtractionPoint>();
            if (ext != null)
            {
                if (ext.platform != null)
                {
                    rotation = ext.platform.rotation;
                    return ext.platform.position + ext.platform.up * 0.45f + ext.platform.right * 0.85f;
                }
                if (ext.safetySpawn != null)
                {
                    rotation = ext.safetySpawn.rotation;
                    return ext.safetySpawn.position + Vector3.up * 0.25f + ext.safetySpawn.right * 0.6f;
                }
            }

            TruckSafetySpawnPoint sp = TruckSafetySpawnPoint.instance ?? UnityEngine.Object.FindObjectOfType<TruckSafetySpawnPoint>();
            if (sp != null)
            {
                rotation = sp.transform.rotation;
                return sp.transform.position + Vector3.up * 0.25f + sp.transform.right * 0.6f;
            }

            TruckDoor door = UnityEngine.Object.FindObjectOfType<TruckDoor>();
            if (door != null)
            {
                rotation = door.transform.rotation;
                return door.transform.position + door.transform.forward * 1.5f + Vector3.up * 0.25f;
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
