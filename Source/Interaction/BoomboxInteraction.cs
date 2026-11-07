using System;
using UnityEngine;
using UnityEngine.SceneManagement;
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
        private Camera cachedCamera;
        private static int raycastMask = -1;
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
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ClearTarget();
        }

        private void Update()
        {
            if (BoomboxHUD.Instance != null && BoomboxHUD.Instance.ShouldHideHUD())
            {
                ClearTarget();
                return;
            }

            HandleGlobalShortcuts();
            FindTargetBoombox();
            HandleInput();
            UpdateHUD();
        }

        private void HandleGlobalShortcuts()
        {
            // Позволяем открывать меню плейлистов [P] даже если бумбокс находится в кармане (companion mode)
            if (BoomboxPlaylistUI.Instance != null && BoomboxPlaylistUI.Instance.IsOpen)
            {
                return;
            }

            KeyCode playlistKey = ModConfig.PlaylistKey != null ? ModConfig.PlaylistKey.Value : KeyCode.P;
            if (Input.GetKeyDown(playlistKey))
            {
                CustomBoomboxItem target = currentTargetBoombox ?? GetPocketBoombox();
                if (target != null && BoomboxPlaylistUI.Instance != null)
                {
                    BoomboxPlaylistUI.Instance.ToggleMenu(target);
                }
            }
        }

        private void FindTargetBoombox()
        {
            // Если игрок сейчас зажимает клавишу перемотки [F] или громкости [V] — не сбрасываем текущую цель!
            KeyCode nextKey = ModConfig.NextTrackKey != null ? ModConfig.NextTrackKey.Value : KeyCode.F;
            KeyCode volumeKey = ModConfig.VolumeKey != null ? ModConfig.VolumeKey.Value : KeyCode.V;
            if (currentTargetBoombox != null && (Input.GetKey(nextKey) || Input.GetKey(volumeKey)))
            {
                return;
            }

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
            if (cachedCamera == null || !cachedCamera.gameObject.activeInHierarchy)
            {
                cachedCamera = Camera.main;
            }

            if (cachedCamera != null)
            {
                if (raycastMask == -1)
                {
                    raycastMask = ~LayerMask.GetMask("Ignore Raycast");
                }

                Ray ray = new Ray(cachedCamera.transform.position, cachedCamera.transform.forward);
                if (Physics.Raycast(ray, out RaycastHit hit, MaxInteractionDistance, raycastMask, QueryTriggerInteraction.Ignore))
                {
                    CustomBoomboxItem boombox = hit.collider.GetComponentInParent<CustomBoomboxItem>();
                    if (boombox != null)
                    {
                        SetTarget(boombox);
                        return;
                    }
                }
            }

            ClearTarget();
        }

        private void SetTarget(CustomBoomboxItem boombox)
        {
            if (currentTargetBoombox != boombox)
            {
                currentTargetBoombox = boombox;
                UpdateHUD();
            }
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
                        status = $"{BoomboxLocalization.StatusPlaying}: Loading...";
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

        private CustomBoomboxItem GetPocketBoombox()
        {
            foreach (CustomBoomboxItem b in CustomBoomboxItem.ActiveBoomboxes)
            {
                if (b != null && b.IsBoomboxInPocket())
                {
                    return b;
                }
            }
            return null;
        }

        private void HandleInput()
        {
            if (BoomboxPlaylistUI.Instance != null && BoomboxPlaylistUI.Instance.IsOpen)
            {
                return;
            }

            CustomBoomboxItem activeBoombox = currentTargetBoombox ?? GetPocketBoombox();
            if (activeBoombox == null)
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
            KeyCode playlistKey = ModConfig.PlaylistKey.Value;
            KeyCode restartKey = ModConfig.RestartTrackKey != null ? ModConfig.RestartTrackKey.Value : KeyCode.Z;

            // 0. Меню плейлистов [P]
            if (Input.GetKeyDown(playlistKey))
            {
                if (BoomboxPlaylistUI.Instance != null)
                {
                    BoomboxPlaylistUI.Instance.ToggleMenu(activeBoombox);
                    return;
                }
            }

            // 0.1 Перезапуск трека с 00:00 [Z]
            if (Input.GetKeyDown(restartKey))
            {
                if (activeBoombox.IsPlaying)
                {
                    activeBoombox.RestartTrack();
                    if (currentTargetBoombox != null) UpdateHUD();
                }
            }

            // 1. Включение / Выключение [E]
            if (Input.GetKeyDown(toggleKey))
            {
                activeBoombox.TogglePlay();
                if (currentTargetBoombox != null) UpdateHUD();
            }

            // 2. Смена режима зацикливания [R] (Выкл -> Цикл -> Единый цикл)
            if (Input.GetKeyDown(loopKey))
            {
                activeBoombox.CycleLoopMode();
                if (currentTargetBoombox != null) UpdateHUD();
            }

            // 3. Переключение перемешивания [T] (Вкл / Выкл)
            if (Input.GetKeyDown(shuffleKey))
            {
                activeBoombox.ToggleShuffle();
                if (currentTargetBoombox != null) UpdateHUD();
            }

            // 4. Отслеживание зажатия [F]
            if (Input.GetKeyDown(nextKey))
            {
                isHoldingNextTrackKey = true;
                didScrubDuringHold = false;
            }

            // 5. Перемотка: зажат [F] + колесико мыши
            if (isHoldingNextTrackKey || Input.GetKey(nextKey))
            {
                isHoldingNextTrackKey = true;
                float scrollDelta = Input.mouseScrollDelta.y;
                if (Mathf.Abs(scrollDelta) > 0.01f)
                {
                    didScrubDuringHold = true;
                    float scrubSeconds = scrollDelta * ModConfig.ScrubStepSeconds.Value;
                    activeBoombox.ScrubTime(scrubSeconds);
                }
            }

            // 6. Одиночное нажатие [F] (если не было скролла)
            if (Input.GetKeyUp(nextKey))
            {
                if (isHoldingNextTrackKey && !didScrubDuringHold)
                {
                    activeBoombox.NextTrack();
                    if (currentTargetBoombox != null) UpdateHUD();
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
                    activeBoombox.AdjustVolume(volumeDelta);
                    if (currentTargetBoombox != null) UpdateHUD();
                }
            }

            // 9. Одиночное нажатие [V]: Mute / Unmute
            if (Input.GetKeyUp(volumeKey))
            {
                if (isHoldingVolumeKey && !didScrollVolumeDuringHold)
                {
                    activeBoombox.ToggleMute();
                    if (currentTargetBoombox != null) UpdateHUD();
                }

                isHoldingVolumeKey = false;
                didScrollVolumeDuringHold = false;
            }
        }
    }
}
