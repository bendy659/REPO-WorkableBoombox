using System.Collections.Generic;
using HarmonyLib;
using WorkableBoombox.Items;

namespace WorkableBoombox.Patches
{
    [HarmonyPatch]
    public static class ShopPatches
    {
        [HarmonyPatch(typeof(ShopManager), "GetAllItemsFromStatsManager")]
        [HarmonyPostfix]
        public static void GetAllItemsFromStatsManagerPostfix(ShopManager __instance)
        {
            if (__instance == null || BoomboxItemBuilder.RegisteredItemSO == null || __instance.potentialItems == null)
            {
                return;
            }

            // Гарантируем, что бумбокс всегда присутствует в списке доступных для спавна товаров магазина
            if (!__instance.potentialItems.Contains(BoomboxItemBuilder.RegisteredItemSO))
            {
                __instance.potentialItems.Add(BoomboxItemBuilder.RegisteredItemSO);
                Plugin.Log.LogInfo("[Shop] Workable Boombox гарантированно добавлен в ассортимент магазина (potentialItems).");
            }
        }

        [HarmonyPatch(typeof(PunManager), "SpawnShopItem")]
        [HarmonyPrefix]
        public static void SpawnShopItemPrefix(ItemVolume itemVolume, List<Item> itemList)
        {
            if (itemVolume == null || itemList == null || BoomboxItemBuilder.RegisteredItemSO == null)
            {
                return;
            }

            // Если на полку планируется спавн бумбокса, динамически подгоняем его объем под полку (small или medium)
            if (itemList.Contains(BoomboxItemBuilder.RegisteredItemSO))
            {
                bool isCompatibleShelf = itemVolume.itemVolume == SemiFunc.itemVolume.small || itemVolume.itemVolume == SemiFunc.itemVolume.medium;
                if (isCompatibleShelf)
                {
                    BoomboxItemBuilder.RegisteredItemSO.itemVolume = itemVolume.itemVolume;
                }
            }
        }
    }
}
