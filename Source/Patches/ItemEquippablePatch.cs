using HarmonyLib;
using UnityEngine;
using WorkableBoombox.Items;

namespace WorkableBoombox.Patches
{
    [HarmonyPatch(typeof(ItemEquippable))]
    public static class ItemEquippablePatch
    {
        [HarmonyPatch("SetRotation")]
        [HarmonyPostfix]
        public static void SetRotationPostfix(ItemEquippable __instance)
        {
            if (__instance == null) return;
            CustomBoomboxItem boombox = __instance.GetComponent<CustomBoomboxItem>();
            if (boombox == null) return;

            Transform vision = null;
            if (SemiFunc.PlayerAvatarLocal() != null && SemiFunc.PlayerAvatarLocal().PlayerVisionTarget != null)
            {
                vision = SemiFunc.PlayerAvatarLocal().PlayerVisionTarget.VisionTransform;
            }

            Vector3 lookDir = vision != null ? vision.forward : (Camera.main != null ? Camera.main.transform.forward : Vector3.forward);
            lookDir.y = 0f;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                Quaternion uprightFacingPlayer = Quaternion.LookRotation(-lookDir.normalized, Vector3.up);
                if (boombox.PhysGrabObject != null)
                {
                    boombox.PhysGrabObject.transform.rotation = uprightFacingPlayer;
                    if (boombox.PhysGrabObject.rb != null)
                    {
                        boombox.PhysGrabObject.rb.rotation = uprightFacingPlayer;
                    }
                }
            }
        }

        [HarmonyPatch("RPC_CompleteUnequip")]
        [HarmonyPostfix]
        public static void CompleteUnequipPostfix(ItemEquippable __instance)
        {
            if (__instance == null) return;
            CustomBoomboxItem boombox = __instance.GetComponent<CustomBoomboxItem>();
            if (boombox == null) return;

            boombox.OnUnequippedFromHotbar();
        }
    }
}
