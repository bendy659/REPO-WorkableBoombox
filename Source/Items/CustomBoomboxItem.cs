using System;
using System.Collections.Generic;
using System.Reflection;
using Photon.Pun;
using UnityEngine;
using WorkableBoombox.Audio;
using WorkableBoombox.Config;
using WorkableBoombox.Localization;
using WorkableBoombox.UI;

namespace WorkableBoombox.Items
{
    public class CustomBoomboxItem : MonoBehaviourPun
    {
        public enum LoopMode
        {
            Off = 0,
            Playlist = 1,
            Single = 2
        }

        public static readonly List<CustomBoomboxItem> ActiveBoomboxes = new List<CustomBoomboxItem>();

        // Visuals & Components
        public Transform Speaker1;
        public Transform Speaker2;
        public Light Light1;
        public Light Light2;
        public Renderer SpeakerRenderer1;
        public Renderer SpeakerRenderer2;
        public PhysGrabObject PhysGrabObject;
        public AudioSource AudioSource;
        public ItemEquippable ItemEquippable { get; private set; }

        // Pocket cosmetic visual & audio
        private GameObject pocketVisual;
        private AudioSource pocketAudioSource;
        private Transform pocketSpeaker1;
        private Transform pocketSpeaker2;
        private Light pocketLight1;
        private Light pocketLight2;
        private Renderer pocketSpeakerRenderer1;
        private Renderer pocketSpeakerRenderer2;
        private float stateSaveInterval;

        private static readonly FieldInfo isEquippingField =
            typeof(ItemEquippable).GetField("isEquipping", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo isUnequippingField =
            typeof(ItemEquippable).GetField("isUnequipping", BindingFlags.NonPublic | BindingFlags.Instance);

        // State
        public bool IsPlaying { get; private set; }
        public int CurrentTrackIndex { get; private set; } = -1;
        public double PlayStartTime { get; private set; }
        public float StartOffsetSeconds { get; private set; }

        // Track Manager Modes
        public LoopMode CurrentLoopMode { get; private set; } = LoopMode.Playlist;
        public bool ShuffleEnabled { get; private set; } = true;

        private readonly List<int> playlistQueue = new List<int>();
        private int currentQueueIndex = -1;

        // Volume State
        public float CurrentVolume { get; private set; } = 1.0f;
        public bool IsMuted { get; private set; }
        private float unmutedVolume = 1.0f;
        private float pendingVolume = -1f;
        private float volumeDebounceTimer;

        private float pendingSeekTime = -1f;
        private float seekDebounceTimer;
        private float trackSwitchCooldown;

        // Audio reactivity
        private readonly float[] audioSamples = new float[128];
        private float currentRMS;
        private float smoothedRMS;
        private Vector3 initialBodyScale = Vector3.one;

        private void Awake()
        {
            initialBodyScale = transform.localScale;

            CurrentLoopMode = (LoopMode)Mathf.Clamp(ModConfig.DefaultLoopMode.Value, 0, 2);
            ShuffleEnabled = ModConfig.DefaultShuffle.Value;

            if (AudioSource == null)
            {
                AudioSource = gameObject.AddComponent<AudioSource>();
                AudioSource.spatialBlend = 1.0f; // 3D звук
                AudioSource.rolloffMode = AudioRolloffMode.Linear;
                AudioSource.minDistance = 2.0f;
                AudioSource.maxDistance = ModConfig.AudioMaxDistance.Value;
                AudioSource.loop = false;
                AudioSource.playOnAwake = false;
            }

            if (PhysGrabObject == null)
            {
                PhysGrabObject = GetComponent<PhysGrabObject>();
            }

            if (ItemEquippable == null)
            {
                ItemEquippable = GetComponent<ItemEquippable>();
            }
        }

        private void Start()
        {
            StartCoroutine(RestoreStateWhenReady());
        }

        private System.Collections.IEnumerator RestoreStateWhenReady()
        {
            while (BoomboxAudioManager.Instance == null || !BoomboxAudioManager.Instance.IsLoaded)
            {
                yield return new WaitForSeconds(0.2f);
            }

            if (BoomboxStateMemory.HasSavedState)
            {
                bool isMasterOrOwner = PhotonNetwork.IsMasterClient || photonView.IsMine || !SemiFunc.IsMultiplayer();
                if (isMasterOrOwner)
                {
                    BoomboxStateMemory.Restore(this);
                }
            }
        }

        public float GetCurrentPlaybackTime()
        {
            if (AudioSource != null && AudioSource.clip != null && AudioSource.isPlaying)
            {
                return AudioSource.time;
            }

            if (pocketAudioSource != null && pocketAudioSource.isPlaying)
            {
                return pocketAudioSource.time;
            }

            return StartOffsetSeconds;
        }

        public void RestoreSavedState(int trackIdx, float time, float vol, bool muted, LoopMode loop, bool shuffle, bool playing)
        {
            CurrentVolume = Mathf.Clamp01(vol);
            IsMuted = muted;
            CurrentLoopMode = loop;
            ShuffleEnabled = shuffle;

            if (trackIdx >= 0)
            {
                CurrentTrackIndex = trackIdx;
                StartOffsetSeconds = time;
                if (playing)
                {
                    photonView.RPC(nameof(PlayTrackRPC), RpcTarget.All, trackIdx, PhotonNetwork.Time, time);
                }
            }
        }

        public bool IsBoomboxInPocket()
        {
            if (ItemEquippable != null)
            {
                if (ItemEquippable.IsEquipped())
                {
                    return true;
                }

                if (isEquippingField != null)
                {
                    try
                    {
                        if ((bool)isEquippingField.GetValue(ItemEquippable))
                        {
                            return true;
                        }
                    }
                    catch { }
                }
            }

            if (transform.localScale.magnitude < initialBodyScale.magnitude * 0.85f)
            {
                return true;
            }

            return false;
        }

        public bool IsEquipTransitioning()
        {
            if (ItemEquippable != null && isUnequippingField != null)
            {
                try
                {
                    if ((bool)isUnequippingField.GetValue(ItemEquippable))
                    {
                        return true;
                    }
                }
                catch { }
            }

            return false;
        }

        private void OnEnable()
        {
            if (!ActiveBoomboxes.Contains(this))
            {
                ActiveBoomboxes.Add(this);
            }
        }

        private void OnDisable()
        {
            ActiveBoomboxes.Remove(this);
        }

        private void Update()
        {
            if (trackSwitchCooldown > 0f)
            {
                trackSwitchCooldown -= Time.deltaTime;
            }

            HandlePendingSeekDebounce();
            HandlePendingVolumeDebounce();

            // Периодическое сохранение точного времени воспроизведения
            if (IsPlaying)
            {
                stateSaveInterval += Time.deltaTime;
                if (stateSaveInterval >= 1.0f)
                {
                    stateSaveInterval = 0f;
                    BoomboxStateMemory.SavedTime = GetCurrentPlaybackTime();
                }
            }

            // Обработка нахождения в инвентаре/кармане
            bool isEquipped = IsBoomboxInPocket();
            if (isEquipped)
            {
                UpdatePocketMode();
                CheckTrackProgression();
                return;
            }
            else
            {
                DisablePocketMode();
            }

            if (!IsPlaying)
            {
                TurnOffVisuals();
                return;
            }

            AnalyzeAudio();
            AnimateVisuals();
            ApplyCameraShake();
            CheckTrackProgression();
        }

        public void TogglePlay()
        {
            bool nextState = !IsPlaying;
            if (nextState)
            {
                if (playlistQueue.Count == 0 || currentQueueIndex < 0)
                {
                    RebuildQueue(true);
                }

                int trackToPlay = GetCurrentOrNextQueueTrack();
                photonView.RPC(nameof(PlayTrackRPC), RpcTarget.All, trackToPlay, PhotonNetwork.Time, 0f);
            }
            else
            {
                photonView.RPC(nameof(ToggleBoomboxRPC), RpcTarget.All, false);
            }
        }

        public void CycleLoopMode()
        {
            int nextModeInt = ((int)CurrentLoopMode + 1) % 3;
            photonView.RPC(nameof(SetLoopModeRPC), RpcTarget.All, nextModeInt);
        }

        public void ToggleShuffle()
        {
            bool nextShuffle = !ShuffleEnabled;
            photonView.RPC(nameof(SetShuffleRPC), RpcTarget.All, nextShuffle);
        }

        public void NextTrack()
        {
            if (BoomboxAudioManager.Instance == null || BoomboxAudioManager.Instance.LoadedTracks.Count == 0)
            {
                return;
            }

            AdvanceQueueAndPlay(true);
        }

        public void ScrubTime(float deltaSeconds)
        {
            if (!IsPlaying || AudioSource == null || AudioSource.clip == null)
            {
                return;
            }

            float currentTarget = pendingSeekTime >= 0f ? pendingSeekTime : AudioSource.time;
            float newTarget = Mathf.Clamp(currentTarget + deltaSeconds, 0f, AudioSource.clip.length - 1f);

            pendingSeekTime = newTarget;
            seekDebounceTimer = 0.2f;

            if (BoomboxHUD.Instance != null)
            {
                BoomboxHUD.Instance.ShowScrubFeedback(newTarget, AudioSource.clip.length, deltaSeconds);
            }
        }

        public void ToggleMute()
        {
            bool newMute = !IsMuted;
            float targetVol;
            if (newMute)
            {
                unmutedVolume = CurrentVolume > 0.05f ? CurrentVolume : 1.0f;
                targetVol = 0f;
            }
            else
            {
                targetVol = unmutedVolume > 0.05f ? unmutedVolume : 1.0f;
            }

            photonView.RPC(nameof(SetVolumeRPC), RpcTarget.All, targetVol, newMute);
        }

        public void AdjustVolume(float delta)
        {
            float current = pendingVolume >= 0f ? pendingVolume : CurrentVolume;
            float newVol = Mathf.Clamp01(current + delta);
            pendingVolume = newVol;
            volumeDebounceTimer = 0.15f;

            // Мгновенный локальный отклик для плавности
            ApplyLocalVolume(newVol, newVol <= 0.001f);

            if (BoomboxHUD.Instance != null)
            {
                BoomboxHUD.Instance.ShowVolumeFeedback(Mathf.RoundToInt(newVol * 100f), newVol <= 0.001f);
            }
        }

        private void HandlePendingVolumeDebounce()
        {
            if (pendingVolume < 0f)
            {
                return;
            }

            volumeDebounceTimer -= Time.deltaTime;
            if (volumeDebounceTimer <= 0f)
            {
                float finalVol = pendingVolume;
                pendingVolume = -1f;
                bool muted = finalVol <= 0.001f;
                photonView.RPC(nameof(SetVolumeRPC), RpcTarget.All, finalVol, muted);
            }
        }

        public string GetLoopModeString()
        {
            switch (CurrentLoopMode)
            {
                case LoopMode.Off: return BoomboxLocalization.LoopModeOff;
                case LoopMode.Playlist: return BoomboxLocalization.LoopModePlaylist;
                case LoopMode.Single: return BoomboxLocalization.LoopModeSingle;
                default: return BoomboxLocalization.LoopModePlaylist;
            }
        }

        public string GetShuffleString()
        {
            return ShuffleEnabled ? BoomboxLocalization.ShuffleOn : BoomboxLocalization.ShuffleOff;
        }

        public string GetCurrentTrackDisplayName()
        {
            if (!IsPlaying || CurrentTrackIndex < 0 || BoomboxAudioManager.Instance == null)
            {
                return null;
            }

            BoomboxAudioManager.TrackData track = BoomboxAudioManager.Instance.GetTrack(CurrentTrackIndex);
            if (track != null)
            {
                return !string.IsNullOrEmpty(track.FileName) ? track.FileName : track.Title;
            }

            return null;
        }

        [PunRPC]
        public void SetVolumeRPC(float volume, bool muted, PhotonMessageInfo info = default)
        {
            CurrentVolume = Mathf.Clamp01(volume);
            IsMuted = muted;
            ApplyLocalVolume(CurrentVolume, IsMuted);

            if (BoomboxHUD.Instance != null)
            {
                BoomboxHUD.Instance.ShowVolumeFeedback(Mathf.RoundToInt(CurrentVolume * 100f), IsMuted);
            }

            BoomboxStateMemory.Save(this);
        }

        public void ApplyLocalVolume(float vol, bool muted)
        {
            if (AudioSource != null)
            {
                float masterVolume = Mathf.Clamp01(ModConfig.BoomboxVolume.Value);
                AudioSource.volume = muted ? 0f : (vol * masterVolume);
            }
        }

        [PunRPC]
        public void SetLoopModeRPC(int mode, PhotonMessageInfo info = default)
        {
            CurrentLoopMode = (LoopMode)mode;
            if (BoomboxHUD.Instance != null)
            {
                BoomboxHUD.Instance.ShowTrackNotification(BoomboxLocalization.NotificationLoop(GetLoopModeString()));
            }

            BoomboxStateMemory.Save(this);
        }

        [PunRPC]
        public void SetShuffleRPC(bool shuffle, PhotonMessageInfo info = default)
        {
            ShuffleEnabled = shuffle;
            RebuildQueue(true);
            if (BoomboxHUD.Instance != null)
            {
                BoomboxHUD.Instance.ShowTrackNotification(BoomboxLocalization.NotificationShuffle(ShuffleEnabled));
            }

            BoomboxStateMemory.Save(this);
        }

        [PunRPC]
        public void ToggleBoomboxRPC(bool play, PhotonMessageInfo info = default)
        {
            IsPlaying = play;
            if (!IsPlaying)
            {
                if (AudioSource != null && AudioSource.isPlaying)
                {
                    AudioSource.Stop();
                }
            }

            BoomboxStateMemory.Save(this);
        }

        [PunRPC]
        public void PlayTrackRPC(int trackIndex, double startTime, float startOffset, PhotonMessageInfo info = default)
        {
            CurrentTrackIndex = trackIndex;
            PlayStartTime = startTime;
            StartOffsetSeconds = startOffset;
            IsPlaying = true;

            if (BoomboxAudioManager.Instance == null)
            {
                return;
            }

            BoomboxAudioManager.TrackData track = BoomboxAudioManager.Instance.GetTrack(trackIndex);
            if (track != null && track.Clip != null)
            {
                AudioSource.clip = track.Clip;
                ApplyLocalVolume(CurrentVolume, IsMuted);

                double timePassed = PhotonNetwork.Time - startTime;
                if (timePassed < 0) timePassed = 0;

                float targetOffset = (float)(timePassed + startOffset);
                if (targetOffset < track.Clip.length)
                {
                    AudioSource.time = targetOffset;
                    AudioSource.Play();
                }

                if (BoomboxHUD.Instance != null)
                {
                    string repeatPrefix = CurrentLoopMode == LoopMode.Single ? "🔂 " : (ShuffleEnabled ? "🔀 " : "▶ ");
                    BoomboxHUD.Instance.ShowTrackNotification($"{repeatPrefix}{track.Title}");
                }

                BoomboxStateMemory.Save(this);
            }
            else
            {
                Plugin.Log.LogWarning($"[CustomBoombox] Трек под индексом {trackIndex} не найден локально.");
            }
        }

        [PunRPC]
        public void SeekTrackRPC(float newOffsetSeconds, double syncTime, PhotonMessageInfo info = default)
        {
            if (AudioSource == null || AudioSource.clip == null)
            {
                return;
            }

            double timeSinceSync = PhotonNetwork.Time - syncTime;
            if (timeSinceSync < 0) timeSinceSync = 0;

            float finalTime = (float)(newOffsetSeconds + timeSinceSync);
            if (finalTime >= 0f && finalTime < AudioSource.clip.length)
            {
                AudioSource.time = finalTime;
                PlayStartTime = PhotonNetwork.Time;
                StartOffsetSeconds = finalTime;

                if (!AudioSource.isPlaying && IsPlaying)
                {
                    AudioSource.Play();
                }

                BoomboxStateMemory.Save(this);
            }
        }

        private void CheckTrackProgression()
        {
            if (trackSwitchCooldown > 0f || !IsPlaying || AudioSource == null || AudioSource.clip == null)
            {
                return;
            }

            bool isMasterOrOwner = PhotonNetwork.IsMasterClient || photonView.IsMine;
            if (!isMasterOrOwner)
            {
                return;
            }

            double elapsed = (PhotonNetwork.Time - PlayStartTime) + StartOffsetSeconds;
            bool reachedEnd = elapsed >= AudioSource.clip.length;
            bool stoppedNaturally = !AudioSource.isPlaying && elapsed > 0.5;

            if (reachedEnd || stoppedNaturally)
            {
                trackSwitchCooldown = 1.0f;

                if (CurrentLoopMode == LoopMode.Single)
                {
                    // Единый цикл: зацикливание одного и того же трека
                    photonView.RPC(nameof(PlayTrackRPC), RpcTarget.All, CurrentTrackIndex, PhotonNetwork.Time, 0f);
                }
                else
                {
                    AdvanceQueueAndPlay(false);
                }
            }
        }

        private void AdvanceQueueAndPlay(bool isManualSkip)
        {
            trackSwitchCooldown = 1.0f;

            if (BoomboxAudioManager.Instance == null || BoomboxAudioManager.Instance.LoadedTracks.Count == 0)
            {
                return;
            }

            int count = BoomboxAudioManager.Instance.LoadedTracks.Count;
            if (playlistQueue.Count != count)
            {
                RebuildQueue(false);
            }

            currentQueueIndex++;

            // Если дошли до конца очереди
            if (currentQueueIndex >= playlistQueue.Count)
            {
                if (CurrentLoopMode == LoopMode.Playlist || isManualSkip)
                {
                    // Цикл: при завершении списка перемешиваем заново если включен Shuffle и начинаем сначала
                    if (ShuffleEnabled)
                    {
                        RebuildQueue(true);
                    }
                    currentQueueIndex = 0;
                }
                else
                {
                    // Режим без зацикливания: выключаем бумбокс в конце очереди
                    photonView.RPC(nameof(ToggleBoomboxRPC), RpcTarget.All, false);
                    return;
                }
            }

            int nextTrack = playlistQueue[currentQueueIndex];
            photonView.RPC(nameof(PlayTrackRPC), RpcTarget.All, nextTrack, PhotonNetwork.Time, 0f);
        }

        private int GetCurrentOrNextQueueTrack()
        {
            if (playlistQueue.Count == 0)
            {
                RebuildQueue(true);
            }

            if (currentQueueIndex < 0 || currentQueueIndex >= playlistQueue.Count)
            {
                currentQueueIndex = 0;
            }

            return playlistQueue[currentQueueIndex];
        }

        private void RebuildQueue(bool shuffle)
        {
            playlistQueue.Clear();
            if (BoomboxAudioManager.Instance == null) return;

            int count = BoomboxAudioManager.Instance.LoadedTracks.Count;
            if (count == 0) return;

            for (int i = 0; i < count; i++)
            {
                playlistQueue.Add(i);
            }

            if (shuffle && ShuffleEnabled && count > 1)
            {
                // Fisher-Yates shuffle
                for (int i = playlistQueue.Count - 1; i > 0; i--)
                {
                    int j = UnityEngine.Random.Range(0, i + 1);
                    int temp = playlistQueue[i];
                    playlistQueue[i] = playlistQueue[j];
                    playlistQueue[j] = temp;
                }
            }

            if (CurrentTrackIndex >= 0)
            {
                int found = playlistQueue.IndexOf(CurrentTrackIndex);
                if (found >= 0)
                {
                    currentQueueIndex = found;
                }
            }
        }

        private void HandlePendingSeekDebounce()
        {
            if (pendingSeekTime < 0f)
            {
                return;
            }

            seekDebounceTimer -= Time.deltaTime;
            if (seekDebounceTimer <= 0f)
            {
                float finalSeek = pendingSeekTime;
                pendingSeekTime = -1f;
                photonView.RPC(nameof(SeekTrackRPC), RpcTarget.All, finalSeek, PhotonNetwork.Time);
            }
        }

        private void AnalyzeAudio()
        {
            if (AudioSource == null || !AudioSource.isPlaying)
            {
                currentRMS = 0f;
                smoothedRMS = Mathf.Lerp(smoothedRMS, 0f, Time.deltaTime * 15f);
                return;
            }

            AudioSource.GetOutputData(audioSamples, 0);

            float sum = 0f;
            for (int i = 0; i < audioSamples.Length; i++)
            {
                sum += audioSamples[i] * audioSamples[i];
            }

            currentRMS = Mathf.Sqrt(sum / audioSamples.Length);
            smoothedRMS = Mathf.Lerp(smoothedRMS, currentRMS, Time.deltaTime * 25f);
        }

        private void AnimateVisuals()
        {
            if (IsBoomboxInPocket() || IsEquipTransitioning())
            {
                return;
            }

            bool pulseEnabled = ModConfig.EnableVisualPulsing != null && ModConfig.EnableVisualPulsing.Value;
            if (pulseEnabled)
            {
                // Динамический рескейл динамиков от реального звука
                float speakerScale = 1f + smoothedRMS * 0.55f;
                Vector3 targetSpeakerScale = new Vector3(speakerScale, speakerScale, speakerScale);

                if (Speaker1 != null) Speaker1.localScale = targetSpeakerScale;
                if (Speaker2 != null) Speaker2.localScale = targetSpeakerScale;

                // Легкий рескейл самого корпуса бумбокса от баса
                float bodyPulse = 1f + smoothedRMS * 0.10f;
                transform.localScale = initialBodyScale * bodyPulse;
            }
            else
            {
                if (Speaker1 != null && Speaker1.localScale != Vector3.one) Speaker1.localScale = Vector3.one;
                if (Speaker2 != null && Speaker2.localScale != Vector3.one) Speaker2.localScale = Vector3.one;
                if (transform.localScale != initialBodyScale) transform.localScale = initialBodyScale;
            }

            // Светомузыка
            float hue = Mathf.PingPong(Time.time * 0.5f, 1f);
            Color glowColor = Color.HSVToRGB(hue, 1f, 1f);

            if (SpeakerRenderer1 != null && SpeakerRenderer1.material != null)
            {
                SpeakerRenderer1.material.SetColor("_EmissionColor", glowColor * (1f + smoothedRMS * 2f));
            }

            if (SpeakerRenderer2 != null && SpeakerRenderer2.material != null)
            {
                SpeakerRenderer2.material.SetColor("_EmissionColor", glowColor * (1f + smoothedRMS * 2f));
            }

            if (Light1 != null)
            {
                Light1.enabled = true;
                Light1.color = glowColor;
                Light1.intensity = Mathf.Lerp(Light1.intensity, 3f + smoothedRMS * 8f, Time.deltaTime * 10f);
            }

            if (Light2 != null)
            {
                Light2.enabled = true;
                Light2.color = glowColor;
                Light2.intensity = Mathf.Lerp(Light2.intensity, 3f + smoothedRMS * 8f, Time.deltaTime * 10f);
            }
        }

        private void ApplyCameraShake()
        {
            if (ModConfig.EnableCameraShake != null && !ModConfig.EnableCameraShake.Value)
            {
                return;
            }

            Camera cam = Camera.main;
            if (cam == null || CameraAim.Instance == null)
            {
                return;
            }

            float distance = Vector3.Distance(transform.position, cam.transform.position);
            if (distance > 4.5f)
            {
                return;
            }

            float distFactor = 1f - (distance / 4.5f);
            if (smoothedRMS > 0.05f)
            {
                // Тряска камеры при громком звуке вблизи
                float shakePower = (smoothedRMS - 0.05f) * distFactor * 2.2f;
                float shakeY = Mathf.Sin(Time.time * 45f) * shakePower;
                CameraAim.Instance.AdditiveAimY(shakeY);
            }
        }

        private void TurnOffVisuals()
        {
            if (IsBoomboxInPocket() || IsEquipTransitioning())
            {
                return;
            }

            transform.localScale = Vector3.Lerp(transform.localScale, initialBodyScale, Time.deltaTime * 10f);

            if (Speaker1 != null) Speaker1.localScale = Vector3.one;
            if (Speaker2 != null) Speaker2.localScale = Vector3.one;

            if (Light1 != null && Light1.enabled)
            {
                Light1.intensity = Mathf.Lerp(Light1.intensity, 0f, Time.deltaTime * 6f);
                if (Light1.intensity < 0.05f) Light1.enabled = false;
            }

            if (Light2 != null && Light2.enabled)
            {
                Light2.intensity = Mathf.Lerp(Light2.intensity, 0f, Time.deltaTime * 6f);
                if (Light2.intensity < 0.05f) Light2.enabled = false;
            }

            if (SpeakerRenderer1 != null && SpeakerRenderer1.material != null)
            {
                SpeakerRenderer1.material.SetColor("_EmissionColor", Color.black);
            }

            if (SpeakerRenderer2 != null && SpeakerRenderer2.material != null)
            {
                SpeakerRenderer2.material.SetColor("_EmissionColor", Color.black);
            }
        }

        private void InitPocketVisual()
        {
            if (pocketVisual != null) return;
            if (BoomboxItemBuilder.RegisteredPrefab == null) return;

            try
            {
                pocketVisual = UnityEngine.Object.Instantiate(BoomboxItemBuilder.RegisteredPrefab);
                pocketVisual.name = "PocketBoomboxVisual_" + gameObject.name;
                pocketVisual.transform.localScale = Vector3.one * 0.45f;

                // Удаляем всю физику, коллайдеры и сетевые компоненты с косметической копии
                foreach (Component comp in pocketVisual.GetComponentsInChildren<Component>(true))
                {
                    if (comp is PhysGrabObject || comp is Rigidbody || comp is Collider ||
                        comp is PhotonView || comp is ItemAttributes || comp is ItemEquippable ||
                        comp is CustomBoomboxItem || comp is RoomVolumeCheck || comp is ItemToggle ||
                        comp is PhysGrabObjectImpactDetector)
                    {
                        UnityEngine.Object.Destroy(comp);
                    }
                }

                // Ищем динамики и свет
                Transform[] allTransforms = pocketVisual.GetComponentsInChildren<Transform>(true);
                foreach (Transform t in allTransforms)
                {
                    if (t.name == "speaker1" || (Speaker1 != null && t.name == Speaker1.name)) pocketSpeaker1 = t;
                    if (t.name == "speaker2" || (Speaker2 != null && t.name == Speaker2.name)) pocketSpeaker2 = t;
                }

                Light[] lights = pocketVisual.GetComponentsInChildren<Light>(true);
                if (lights.Length > 0) pocketLight1 = lights[0];
                if (lights.Length > 1) pocketLight2 = lights[1];

                if (pocketSpeaker1 != null) pocketSpeakerRenderer1 = pocketSpeaker1.GetComponent<Renderer>();
                if (pocketSpeaker2 != null) pocketSpeakerRenderer2 = pocketSpeaker2.GetComponent<Renderer>();

                pocketAudioSource = pocketVisual.GetComponent<AudioSource>();
                if (pocketAudioSource == null)
                {
                    pocketAudioSource = pocketVisual.AddComponent<AudioSource>();
                }
                pocketAudioSource.spatialBlend = 1.0f;
                pocketAudioSource.rolloffMode = AudioRolloffMode.Linear;
                pocketAudioSource.minDistance = 2.0f;
                pocketAudioSource.maxDistance = ModConfig.AudioMaxDistance.Value;
                pocketAudioSource.loop = false;
                pocketAudioSource.playOnAwake = false;

                pocketVisual.SetActive(false);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[CustomBoombox] Ошибка создания pocketVisual: {ex}");
            }
        }

        private void UpdatePocketMode()
        {
            if (ItemEquippable == null) return;
            InitPocketVisual();
            if (pocketVisual == null) return;

            PlayerAvatar owner = ItemEquippable.GetOwnerPlayerAvatar();
            if (owner == null)
            {
                if (pocketVisual.activeSelf) pocketVisual.SetActive(false);
                return;
            }

            if (!pocketVisual.activeSelf)
            {
                pocketVisual.SetActive(true);
            }

            // Точка крепления: за спиной / у правого плеча и головы игрока
            Transform anchor = null;
            if (owner.playerAvatarVisuals != null)
            {
                anchor = owner.playerAvatarVisuals.bodyTopUpTransform != null
                    ? owner.playerAvatarVisuals.bodyTopUpTransform
                    : owner.playerAvatarVisuals.transform;
            }
            if (anchor == null)
            {
                anchor = owner.playerTransform != null ? owner.playerTransform : owner.transform;
            }

            // Небольшой левитирующий боб-эффект
            float bob = Mathf.Sin(Time.time * 2.5f) * 0.03f;
            Vector3 targetPos = anchor.position - anchor.forward * 0.32f + anchor.up * (0.16f + bob) + anchor.right * 0.22f;
            Quaternion targetRot = anchor.rotation * Quaternion.Euler(0f, 180f, 0f);

            pocketVisual.transform.position = Vector3.Lerp(pocketVisual.transform.position, targetPos, Time.deltaTime * 20f);
            pocketVisual.transform.rotation = Quaternion.Slerp(pocketVisual.transform.rotation, targetRot, Time.deltaTime * 20f);

            // Синхронизация 3D звука при нахождении в кармане
            if (IsPlaying && AudioSource != null && AudioSource.clip != null)
            {
                // Глушим основной AudioSource, который телепортирован далеко под карту
                AudioSource.volume = 0f;

                if (pocketAudioSource != null)
                {
                    if (pocketAudioSource.clip != AudioSource.clip)
                    {
                        pocketAudioSource.clip = AudioSource.clip;
                    }

                    float expectedTime = (float)((PhotonNetwork.Time - PlayStartTime) + StartOffsetSeconds);
                    if (expectedTime >= 0f && expectedTime < AudioSource.clip.length)
                    {
                        if (Mathf.Abs(pocketAudioSource.time - expectedTime) > 0.35f)
                        {
                            pocketAudioSource.time = expectedTime;
                        }
                    }

                    float masterVol = Mathf.Clamp01(ModConfig.BoomboxVolume.Value);
                    pocketAudioSource.volume = IsMuted ? 0f : (CurrentVolume * masterVol);

                    if (!pocketAudioSource.isPlaying)
                    {
                        pocketAudioSource.Play();
                    }
                }

                AnimatePocketVisuals();
            }
            else
            {
                if (pocketAudioSource != null && pocketAudioSource.isPlaying)
                {
                    pocketAudioSource.Stop();
                }
                TurnOffPocketVisuals();
            }
        }

        private void DisablePocketMode()
        {
            if (pocketVisual != null && pocketVisual.activeSelf)
            {
                pocketVisual.SetActive(false);
                if (pocketAudioSource != null && pocketAudioSource.isPlaying)
                {
                    pocketAudioSource.Stop();
                }

                // Восстанавливаем основной AudioSource при доставании из кармана
                ApplyLocalVolume(CurrentVolume, IsMuted);
                if (IsPlaying && AudioSource != null && AudioSource.clip != null)
                {
                    float targetTime = (float)((PhotonNetwork.Time - PlayStartTime) + StartOffsetSeconds);
                    if (targetTime >= 0f && targetTime < AudioSource.clip.length)
                    {
                        AudioSource.time = targetTime;
                    }
                    if (!AudioSource.isPlaying)
                    {
                        AudioSource.Play();
                    }
                }
            }
        }

        private void AnimatePocketVisuals()
        {
            float spScale = 1f + smoothedRMS * 0.45f;
            Vector3 targetScale = new Vector3(spScale, spScale, spScale);
            if (pocketSpeaker1 != null) pocketSpeaker1.localScale = targetScale;
            if (pocketSpeaker2 != null) pocketSpeaker2.localScale = targetScale;

            float hue = Mathf.PingPong(Time.time * 0.5f, 1f);
            Color glowColor = Color.HSVToRGB(hue, 1f, 1f);

            if (pocketLight1 != null)
            {
                pocketLight1.enabled = true;
                pocketLight1.color = glowColor;
                pocketLight1.intensity = 2f + smoothedRMS * 4f;
            }

            if (pocketLight2 != null)
            {
                pocketLight2.enabled = true;
                pocketLight2.color = glowColor;
                pocketLight2.intensity = 2f + smoothedRMS * 4f;
            }

            if (pocketSpeakerRenderer1 != null && pocketSpeakerRenderer1.material != null)
            {
                pocketSpeakerRenderer1.material.SetColor("_EmissionColor", glowColor * (1f + smoothedRMS * 2f));
            }

            if (pocketSpeakerRenderer2 != null && pocketSpeakerRenderer2.material != null)
            {
                pocketSpeakerRenderer2.material.SetColor("_EmissionColor", glowColor * (1f + smoothedRMS * 2f));
            }
        }

        private void TurnOffPocketVisuals()
        {
            if (pocketSpeaker1 != null) pocketSpeaker1.localScale = Vector3.one;
            if (pocketSpeaker2 != null) pocketSpeaker2.localScale = Vector3.one;
            if (pocketLight1 != null) pocketLight1.enabled = false;
            if (pocketLight2 != null) pocketLight2.enabled = false;
            if (pocketSpeakerRenderer1 != null && pocketSpeakerRenderer1.material != null) pocketSpeakerRenderer1.material.SetColor("_EmissionColor", Color.black);
            if (pocketSpeakerRenderer2 != null && pocketSpeakerRenderer2.material != null) pocketSpeakerRenderer2.material.SetColor("_EmissionColor", Color.black);
        }

        private void OnDestroy()
        {
            if (IsPlaying || CurrentTrackIndex >= 0)
            {
                BoomboxStateMemory.Save(this);
            }

            if (pocketVisual != null)
            {
                Destroy(pocketVisual);
            }
        }
    }
}
