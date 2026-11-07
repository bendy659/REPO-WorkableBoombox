using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;
using WorkableBoombox.Config;
using WorkableBoombox.Localization;

namespace WorkableBoombox.Items
{
    public static class BoomboxItemBuilder
    {
        public static bool IsRegistered { get; private set; }
        public static GameObject RegisteredPrefab { get; private set; }
        public static Item RegisteredItemSO { get; private set; }
        public static Sprite CustomBoomboxSprite { get; private set; }

        public static Sprite GetBoomboxSprite()
        {
            if (CustomBoomboxSprite != null)
            {
                return CustomBoomboxSprite;
            }

            try
            {
                string iconPath = Path.Combine(Paths.PluginPath, "WorkableBoombox", "icon.png");
                if (!File.Exists(iconPath))
                {
                    string assemblyDir = Path.GetDirectoryName(typeof(BoomboxItemBuilder).Assembly.Location);
                    if (!string.IsNullOrEmpty(assemblyDir))
                    {
                        iconPath = Path.Combine(assemblyDir, "icon.png");
                    }
                }

                if (File.Exists(iconPath))
                {
                    byte[] bytes = File.ReadAllBytes(iconPath);
                    Texture2D tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
                    if (ImageConversion.LoadImage(tex, bytes))
                    {
                        CustomBoomboxSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                        CustomBoomboxSprite.name = "WorkableBoombox_Icon";
                        Plugin.Log.LogInfo("[BoomboxItemBuilder] Спрайт иконки инвентаря успешно создан из icon.png!");
                        return CustomBoomboxSprite;
                    }
                }
                else
                {
                    Plugin.Log.LogWarning($"[BoomboxItemBuilder] icon.png не найден для спрайта инвентаря: {iconPath}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[BoomboxItemBuilder] Ошибка при создании спрайта иконки: {ex}");
            }

            return null;
        }

        public static void Initialize()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            TryFindAndRegister();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!IsRegistered)
            {
                TryFindAndRegister();
            }
        }

        public static IEnumerator SearchRoutine()
        {
            while (!IsRegistered)
            {
                TryFindAndRegister();
                yield return new WaitForSeconds(1.5f);
            }
        }

        public static bool TryFindAndRegister()
        {
            if (IsRegistered)
            {
                return true;
            }

            GameObject original = FindVanillaBoomboxPrefab();
            if (original != null)
            {
                return TryRegisterFromOriginal(original);
            }

            return false;
        }

        public static GameObject FindVanillaBoomboxPrefab()
        {
            // 1. Поиск через RunManager.instance
            if (RunManager.instance != null)
            {
                List<Level> levels = new List<Level>();
                if (RunManager.instance.levels != null) levels.AddRange(RunManager.instance.levels);
                if (RunManager.instance.levelArena != null) levels.AddRange(RunManager.instance.levelArena);
                if (RunManager.instance.levelCurrent != null) levels.Add(RunManager.instance.levelCurrent);
                if (RunManager.instance.levelLobby != null) levels.Add(RunManager.instance.levelLobby);

                foreach (Level lvl in levels)
                {
                    if (lvl == null || lvl.ValuablePresets == null) continue;
                    foreach (LevelValuables preset in lvl.ValuablePresets)
                    {
                        GameObject found = SearchInPreset(preset);
                        if (found != null)
                        {
                            Plugin.Log.LogInfo($"[BoomboxItemBuilder] Найден префаб бумбокса в RunManager ({lvl.name})!");
                            return found;
                        }
                    }
                }
            }

            // 2. Поиск через LevelValuables в памяти
            LevelValuables[] allPresets = Resources.FindObjectsOfTypeAll<LevelValuables>();
            foreach (LevelValuables preset in allPresets)
            {
                GameObject found = SearchInPreset(preset);
                if (found != null)
                {
                    Plugin.Log.LogInfo("[BoomboxItemBuilder] Найден префаб бумбокса в LevelValuables!");
                    return found;
                }
            }

            // 3. Поиск экземпляра ValuableBoombox в памяти
            ValuableBoombox[] loadedBoomboxes = Resources.FindObjectsOfTypeAll<ValuableBoombox>();
            foreach (ValuableBoombox candidate in loadedBoomboxes)
            {
                if (candidate != null && candidate.gameObject != null)
                {
                    Plugin.Log.LogInfo($"[BoomboxItemBuilder] Найден экземпляр ValuableBoombox ({candidate.gameObject.name})!");
                    return candidate.gameObject;
                }
            }

            return null;
        }

        private static GameObject SearchInPreset(LevelValuables preset)
        {
            if (preset == null) return null;

            List<List<PrefabRef>> lists = new List<List<PrefabRef>>
            {
                preset.medium, preset.small, preset.tiny, preset.big, preset.wide, preset.tall, preset.veryTall
            };

            foreach (var list in lists)
            {
                if (list == null) continue;
                foreach (PrefabRef pRef in list)
                {
                    if (pRef == null) continue;
                    if (IsBoomboxRef(pRef))
                    {
                        GameObject prefab = pRef.Prefab;
                        if (prefab != null)
                        {
                            return prefab;
                        }
                    }
                }
            }

            return null;
        }

        private static bool IsBoomboxRef(PrefabRef pRef)
        {
            if (pRef == null) return false;

            string name = pRef.PrefabName;
            if (!string.IsNullOrEmpty(name) && name.IndexOf("Boombox", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string path = pRef.ResourcePath;
            if (!string.IsNullOrEmpty(path) && path.IndexOf("Boombox", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        public static bool TryRegisterFromOriginal(GameObject original)
        {
            if (IsRegistered || original == null)
            {
                return false;
            }

            try
            {
                Plugin.Log.LogInfo($"[BoomboxItemBuilder] Клонирование префаба '{original.name}'...");

                GameObject customPrefab = UnityEngine.Object.Instantiate(original);
                customPrefab.name = "WorkableBoomboxItem";
                UnityEngine.Object.DontDestroyOnLoad(customPrefab);
                customPrefab.transform.position = new Vector3(0f, 500f, 0f);

                // Сохраняем ссылки на динамики и свет
                ValuableBoombox oldBoombox = customPrefab.GetComponent<ValuableBoombox>();
                Transform sp1 = oldBoombox != null ? oldBoombox.speaker1 : null;
                Transform sp2 = oldBoombox != null ? oldBoombox.speaker2 : null;
                Light lt1 = oldBoombox != null ? oldBoombox.light1 : null;
                Light lt2 = oldBoombox != null ? oldBoombox.light2 : null;

                // Выключаем световые источники по умолчанию перед началом воспроизведения
                if (lt1 != null)
                {
                    lt1.enabled = false;
                    lt1.intensity = 0f;
                }
                if (lt2 != null)
                {
                    lt2.enabled = false;
                    lt2.intensity = 0f;
                }
                if (sp1 != null)
                {
                    Renderer r1 = sp1.GetComponent<Renderer>();
                    if (r1 != null && r1.material != null) r1.material.SetColor("_EmissionColor", Color.black);
                }
                if (sp2 != null)
                {
                    Renderer r2 = sp2.GetComponent<Renderer>();
                    if (r2 != null && r2.material != null) r2.material.SetColor("_EmissionColor", Color.black);
                }

                // Удаляем ванильную логику ловушки и мусора
                Trap oldTrap = customPrefab.GetComponent<Trap>();
                ValuableObject oldVal = customPrefab.GetComponent<ValuableObject>();

                if (oldBoombox != null) UnityEngine.Object.DestroyImmediate(oldBoombox);
                if (oldTrap != null) UnityEngine.Object.DestroyImmediate(oldTrap);
                if (oldVal != null) UnityEngine.Object.DestroyImmediate(oldVal);

                // Сетевая синхронизация через PhotonTransformView
                PhotonTransformView ptv = customPrefab.GetComponent<PhotonTransformView>();
                if (ptv == null)
                {
                    ptv = customPrefab.AddComponent<PhotonTransformView>();
                }

                PhotonView pv = customPrefab.GetComponent<PhotonView>();
                if (pv != null && ptv != null)
                {
                    if (pv.ObservedComponents == null)
                    {
                        pv.ObservedComponents = new List<Component>();
                    }
                    if (!pv.ObservedComponents.Contains(ptv))
                    {
                        pv.ObservedComponents.Add(ptv);
                    }
                }

                // PhysGrabObjectImpactDetector критически важен: PhysGrabber.GridObjectsInstantiate требует его наличия при взятии в руки
                PhysGrabObjectImpactDetector impact = customPrefab.GetComponent<PhysGrabObjectImpactDetector>();
                if (impact == null)
                {
                    impact = customPrefab.AddComponent<PhysGrabObjectImpactDetector>();
                }
                impact.destroyDisable = false; // Отключаем на шаблоне, чтобы не триггерить анти-воид телепорт
                impact.isValuable = false;
                impact.isCart = false;
                impact.durability = float.MaxValue;
                impact.fragility = 0f;

                PhysGrabObject physGrab = customPrefab.GetComponent<PhysGrabObject>();
                if (physGrab != null)
                {
                    physGrab.enabled = false; // Отключаем PhysGrabObject на мастер-шаблоне
                }

                // Присваиваем шаблон ДО добавления CustomBoomboxItem, чтобы Awake() распознал мастер-префаб
                RegisteredPrefab = customPrefab;

                // Добавляем CustomBoomboxItem
                CustomBoomboxItem boomboxComp = customPrefab.AddComponent<CustomBoomboxItem>();
                boomboxComp.Speaker1 = sp1;
                boomboxComp.Speaker2 = sp2;
                boomboxComp.Light1 = lt1;
                boomboxComp.Light2 = lt2;

                if (sp1 != null) boomboxComp.SpeakerRenderer1 = sp1.GetComponent<Renderer>();
                if (sp2 != null) boomboxComp.SpeakerRenderer2 = sp2.GetComponent<Renderer>();
                boomboxComp.PhysGrabObject = physGrab;
                boomboxComp.enabled = true;

                // Деактивируем мастер-шаблон
                customPrefab.SetActive(false);

                // Настраиваем ItemAttributes и проверки комнат/магазина
                ItemAttributes attr = customPrefab.GetComponent<ItemAttributes>();
                if (attr == null)
                {
                    attr = customPrefab.AddComponent<ItemAttributes>();
                }

                RoomVolumeCheck roomCheck = customPrefab.GetComponent<RoomVolumeCheck>();
                if (roomCheck == null)
                {
                    roomCheck = customPrefab.AddComponent<RoomVolumeCheck>();
                }

                Item itemSO = ScriptableObject.CreateInstance<Item>();
                itemSO.name = "WorkableBoombox";
                itemSO.itemName = ModConfig.CustomItemName.Value;
                itemSO.description = BoomboxLocalization.ItemDescription;
                itemSO.itemType = SemiFunc.itemType.tool;
                itemSO.physicalItem = true;
                itemSO.maxAmountInShop = 2;
                itemSO.maxPurchase = false;
                itemSO.status = Item.Status.Finalized;
                itemSO.disabled = false;
                itemSO.minPlayerCount = 1;
                itemSO.itemSecretShopType = SemiFunc.itemSecretShopType.none;
                itemSO.itemVolume = SemiFunc.itemVolume.medium;

                Value priceVal = ScriptableObject.CreateInstance<Value>();
                // В R.E.P.O. ShopManager.itemValueMultiplier = 4.0, и расчет цены на ценнике: (value * multiplier) / 1000.
                // Чтобы итоговая цена на ценнике и в магазине была равна ModConfig.ShopPrice (например, $45):
                // basePrice = (ShopPrice / 4.0f) * 1000f = ShopPrice * 250f.
                float targetShopPrice = Mathf.Max(1f, (float)ModConfig.ShopPrice.Value);
                float basePrice = targetShopPrice * 250f;
                priceVal.valueMin = basePrice;
                priceVal.valueMax = basePrice;
                itemSO.value = priceVal;

                attr.item = itemSO;
                RegisteredItemSO = itemSO;

                // Возможность экипировки в карман (ItemEquippable)
                ItemEquippable equippable = customPrefab.GetComponent<ItemEquippable>();
                if (equippable == null)
                {
                    equippable = customPrefab.AddComponent<ItemEquippable>();
                }

                // Назначаем иконку предмета для инвентаря
                Sprite iconSprite = GetBoomboxSprite();
                if (iconSprite != null)
                {
                    attr.icon = iconSprite;
                    equippable.ItemIcon = iconSprite;
                }

                // Регистрация в REPOLib
                PrefabRef registeredRef = REPOLib.Modules.Items.RegisterItem(attr);

                RegisteredPrefab = customPrefab;
                IsRegistered = true;

                // Регистрируем в NetworkPrefabs под всеми возможными идентификаторами для Photon / RepoAdminMenu / REPOLib
                REPOLib.Modules.NetworkPrefabs.RegisterNetworkPrefab("WorkableBoomboxItem", customPrefab);
                REPOLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(itemSO.name, customPrefab);
                if (registeredRef != null)
                {
                    if (!string.IsNullOrEmpty(registeredRef.PrefabName))
                    {
                        REPOLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(registeredRef.PrefabName, customPrefab);
                    }
                    if (!string.IsNullOrEmpty(registeredRef.ResourcePath))
                    {
                        REPOLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(registeredRef.ResourcePath, customPrefab);
                    }
                }
                Plugin.Log.LogInfo($"[BoomboxItemBuilder] Кастомный предмет '{itemSO.itemName}' успешно зарегистрирован! (PrefabRef: {(registeredRef != null ? registeredRef.PrefabName : "ok")})");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[BoomboxItemBuilder] Ошибка при регистрации предмета: {ex}");
                return false;
            }
        }
    }
}
