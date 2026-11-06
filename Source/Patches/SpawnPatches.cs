using System;
using HarmonyLib;
using UnityEngine;
using WorkableBoombox.Items;

namespace WorkableBoombox.Patches
{
    [HarmonyPatch]
    public static class SpawnPatches
    {
        [HarmonyPatch(typeof(REPOLib.Modules.NetworkPrefabs), "SpawnNetworkPrefab", new Type[] { typeof(PrefabRef), typeof(Vector3), typeof(Quaternion), typeof(byte), typeof(object[]) })]
        [HarmonyPostfix]
        public static void Postfix_SpawnNetworkPrefab(ref GameObject __result)
        {
            if (__result != null && !__result.activeSelf)
            {
                __result.SetActive(true);
            }
        }

        public static void RepoAdminMenu_SpawnItem_Postfix(Item _item)
        {
            if (PlayerAvatar.instance != null)
            {
                Vector3 playerPos = PlayerAvatar.instance.transform.position;
                CustomBoomboxItem[] allBoomboxes = Resources.FindObjectsOfTypeAll<CustomBoomboxItem>();
                foreach (CustomBoomboxItem b in allBoomboxes)
                {
                    if (b != null && !b.gameObject.activeSelf && b.gameObject != WorkableBoombox.Items.BoomboxItemBuilder.RegisteredPrefab)
                    {
                        if (Vector3.Distance(b.transform.position, playerPos) < 5.0f)
                        {
                            b.gameObject.SetActive(true);
                        }
                    }
                }
            }
        }
    }
}
