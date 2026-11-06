using System;
using HarmonyLib;
using UnityEngine;
using WorkableBoombox.Items;

namespace WorkableBoombox.Patches
{
    [HarmonyPatch(typeof(ValuableBoombox))]
    public static class ValuableBoomboxPatch
    {
        [HarmonyPatch("Start")]
        [HarmonyPostfix]
        public static void StartPostfix(ValuableBoombox __instance)
        {
            if (__instance == null)
            {
                return;
            }

            // Проверяем, добавлен ли уже наш компонент
            CustomBoomboxItem customItem = __instance.GetComponent<CustomBoomboxItem>();
            if (customItem == null)
            {
                customItem = __instance.gameObject.AddComponent<CustomBoomboxItem>();
                customItem.Speaker1 = __instance.speaker1;
                customItem.Speaker2 = __instance.speaker2;
                customItem.Light1 = __instance.light1;
                customItem.Light2 = __instance.light2;
                customItem.PhysGrabObject = __instance.GetComponent<PhysGrabObject>();

                if (__instance.speaker1 != null)
                {
                    customItem.SpeakerRenderer1 = __instance.speaker1.GetComponent<Renderer>();
                }

                if (__instance.speaker2 != null)
                {
                    customItem.SpeakerRenderer2 = __instance.speaker2.GetComponent<Renderer>();
                }

                Plugin.Log.LogInfo("[ValuableBoomboxPatch] Успешно улучшен найденный ванильный бумбокс в шахте!");
            }
        }

        [HarmonyPatch("Update")]
        [HarmonyPrefix]
        public static bool UpdatePrefix(ValuableBoombox __instance)
        {
            // Если на объекте есть наш кастомный скрипт, глушим ванильный Update (чтобы не играли старые звуки и не тряслась камера)
            CustomBoomboxItem customItem = __instance.GetComponent<CustomBoomboxItem>();
            if (customItem != null)
            {
                return false;
            }

            return true;
        }
    }
}
