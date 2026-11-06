using System;
using UnityEngine;
using WorkableBoombox.Config;
using WorkableBoombox.Items;
using WorkableBoombox.Localization;
using WorkableBoombox.UI;

namespace WorkableBoombox.Interaction
{
    public class BoomboxInteraction : MonoBehaviour
    {
        public static BoomboxInteraction Instance { get; private set; }

        private const float MaxInteractionDistance = 4.0f;

        private CustomBoomboxItem currentTargetBoombox;
        private bool isHoldingNextTrackKey;
        private bool didScrubDuringHold;
        private bool isHoldingVolumeKey;
        private bool didScrollVolumeDuringHold;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            FindTargetBoombox();
            HandleInput();
            UpdateHUD();
        }

        private void FindTargetBoombox()
        {
            // 1. Проверяем, держит ли локальный игрок бумбокс прямо сейчас в руках
            foreach (CustomBoomboxItem b in CustomBoomboxItem.ActiveBoomboxes)
            {
                if (b == null || b.PhysGrabObject == null) continue;

                if (b.PhysGrabObject.grabbed && b.PhysGrabObject.playerGrabbing != null)
                {
                    foreach (PhysGrabber grabber in b.PhysGrabObject.playerGrabbing)
                    {
                        if (grabber != null && grabber.isLocal)
                        {
                            SetTarget(b);
                            return;
                        }
                    }
                }
            }

            // 2. Если не держит в руках - ищем взглядом через Raycast
            Camera cam = Camera.main;
            if (cam == null)
            {
                ClearTarget();
                return;
            }

            Ray ray = new Ray(cam.transform.position, cam.transform.forward);
            int mask = ~LayerMask.GetMask("Ignore Raycast");

            if (Physics.Raycast(ray, out RaycastHit hit, MaxInteractionDistance, mask, QueryTriggerInteraction.Ignore))
            {
                CustomBoomboxItem boombox = hit.collider.GetComponentInParent<CustomBoomboxItem>();
                if (boombox != null)
                {
                    SetTarget(boombox);
                    return;
                }
            }

            ClearTarget();
        }

        private void SetTarget(CustomBoomboxItem boombox)
        {
            currentTargetBoombox = boombox;
            UpdateHUD();
        }

        private void ClearTarget()
        {
            if (currentTargetBoombox != null)
            {
                currentTargetBoombox = null;
                if (BoomboxHUD.Instance != null)
                {
                    BoomboxHUD.Instance.SetHoverPrompt(false);
                }
            }
        }

        private void UpdateHUD()
        {
            if (currentTargetBoombox == null)
            {
                return;
            }

            if (BoomboxHUD.Instance != null)
            {
                string status;
                if (currentTargetBoombox.IsPlaying)
                {
                    string trackFileName = currentTargetBoombox.GetCurrentTrackDisplayName();
                    if (!string.IsNullOrEmpty(trackFileName))
                    {
                        if (trackFileName.Length > 45)
                        {
                            trackFileName = trackFileName.Substring(0, 42) + "...";
                        }
                        status = $"{BoomboxLocalization.StatusPlaying}: {trackFileName}";
                    }
                    else
                    {
                        status = BoomboxLocalization.StatusPlaying;
                    }
                }
                else
                {
                    status = BoomboxLocalization.StatusOff;
                }

                string loopStr = currentTargetBoombox.GetLoopModeString();
                string shuffleStr = currentTargetBoombox.GetShuffleString();
                int volumePercent = Mathf.RoundToInt(currentTargetBoombox.CurrentVolume * 100f);
                bool isMuted = currentTargetBoombox.IsMuted;
                BoomboxHUD.Instance.SetHoverPrompt(true, status, loopStr, shuffleStr, volumePercent, isMuted);
            }
        }

        private void HandleInput()
        {
            if (currentTargetBoombox == null)
            {
                isHoldingNextTrackKey = false;
                didScrubDuringHold = false;
                isHoldingVolumeKey = false;
                didScrollVolumeDuringHold = false;
                return;
            }

            KeyCode toggleKey = ModConfig.ToggleKey.Value;
            KeyCode nextKey = ModConfig.NextTrackKey.Value;
            KeyCode loopKey = ModConfig.LoopModeKey.Value;
            KeyCode shuffleKey = ModConfig.ShuffleKey.Value;
            KeyCode volumeKey = ModConfig.VolumeKey.Value;

            // 1. Включение / Выключение [E]
            if (Input.GetKeyDown(toggleKey))
            {
                currentTargetBoombox.TogglePlay();
                UpdateHUD();
            }

            // 2. Смена режима зацикливания [R] (Выкл -> Цикл -> Единый цикл)
            if (Input.GetKeyDown(loopKey))
            {
                currentTargetBoombox.CycleLoopMode();
                UpdateHUD();
            }

            // 3. Переключение перемешивания [T] (Вкл / Выкл)
            if (Input.GetKeyDown(shuffleKey))
            {
                currentTargetBoombox.ToggleShuffle();
                UpdateHUD();
            }

            // 4. Отслеживание зажатия [F]
            if (Input.GetKeyDown(nextKey))
            {
                isHoldingNextTrackKey = true;
                didScrubDuringHold = false;
            }

            // 5. Перемотка: зажат [F] + колесико мыши
            if (isHoldingNextTrackKey)
            {
                float scrollDelta = Input.mouseScrollDelta.y;
                if (Mathf.Abs(scrollDelta) > 0.01f)
                {
                    didScrubDuringHold = true;
                    float scrubSeconds = scrollDelta * ModConfig.ScrubStepSeconds.Value;
                    currentTargetBoombox.ScrubTime(scrubSeconds);
                }
            }

            // 6. Одиночное нажатие [F] (если не было скролла)
            if (Input.GetKeyUp(nextKey))
            {
                if (isHoldingNextTrackKey && !didScrubDuringHold)
                {
                    currentTargetBoombox.NextTrack();
                    UpdateHUD();
                }

                isHoldingNextTrackKey = false;
                didScrubDuringHold = false;
            }

            // 7. Отслеживание зажатия [V] для громкости
            if (Input.GetKeyDown(volumeKey))
            {
                isHoldingVolumeKey = true;
                didScrollVolumeDuringHold = false;
            }

            // 8. Регулировка громкости: зажат [V] + колесико мыши
            if (isHoldingVolumeKey)
            {
                float scrollDelta = Input.mouseScrollDelta.y;
                if (Mathf.Abs(scrollDelta) > 0.01f)
                {
                    didScrollVolumeDuringHold = true;
                    float step = (ModConfig.VolumeStepPercent != null ? ModConfig.VolumeStepPercent.Value : 5f) / 100f;
                    float volumeDelta = (scrollDelta > 0 ? 1f : -1f) * step;
                    currentTargetBoombox.AdjustVolume(volumeDelta);
                    UpdateHUD();
                }
            }

            // 9. Одиночное нажатие [V]: Mute / Unmute
            if (Input.GetKeyUp(volumeKey))
            {
                if (isHoldingVolumeKey && !didScrollVolumeDuringHold)
                {
                    currentTargetBoombox.ToggleMute();
                    UpdateHUD();
                }

                isHoldingVolumeKey = false;
                didScrollVolumeDuringHold = false;
            }
        }
    }
}
