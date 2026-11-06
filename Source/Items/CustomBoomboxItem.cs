using System;
using System.Collections.Generic;
using System.Reflection;
using Photon.Pun;
using UnityEngine;
using WorkableBoombox.Audio;
using WorkableBoombox.Config;
using WorkableBoombox.Localization;
using WorkableBoombox.Playlists;
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

        // Active Playlist Mode
        public bool IsPlaylistMode { get; private set; }
        public string ActivePlaylistOwner { get; private set; }
        public string ActivePlaylistName { get; private set; }
        public readonly List<PlaylistTrackEntry> ActivePlaylistTracks = new List<PlaylistTrackEntry>();

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
            if (BoomboxItemBuilder.RegisteredPrefab != null && gameObject == BoomboxItemBuilder.RegisteredPrefab)
            {
                if (AudioSource != null) AudioSource.enabled = false;
                Rigidbody masterRb = GetComponent<Rigidbody>();
                if (masterRb != null) { masterRb.isKinematic = true; masterRb.detectCollisions = false; }
                Collider[] masterCols = GetComponentsInChildren<Collider>(true);
                foreach (var c in masterCols) c.enabled = false;
                if (PhysGrabObject != null) PhysGrabObject.enabled = false;
                return;
            }

            // На реальных экземплярах бумбокса гарантируем активность компонента, коллизии и физику
            enabled = true;

            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.detectCollisions = true;
            }
            Collider[] cols = GetComponentsInChildren<Collider>(true);
            foreach (var c in cols)
            {
                c.enabled = true;
            }

            if (PhysGrabObject != null)
            {
                PhysGrabObject.enabled = true;
            }
            PhysGrabObjectImpactDetector impact = GetComponent<PhysGrabObjectImpactDetector>();
            if (impact != null)
            {
                impact.destroyDisable = true;
            }

            // Находим и сохраняем ссылки на динамики и свет, если они отсутствуют
            if (Light1 == null || Light2 == null)
            {
                Light[] allLights = GetComponentsInChildren<Light>(true);
                if (allLights.Length > 0) Light1 = allLights[0];
                if (allLights.Length > 1) Light2 = allLights[1];
            }

            if (Speaker1 == null || Speaker2 == null)
            {
                Transform[] transforms = GetComponentsInChildren<Transform>(true);
                foreach (Transform t in transforms)
                {
                    if (t.name.IndexOf("speaker1", StringComparison.OrdinalIgnoreCase) >= 0) Speaker1 = t;
                    else if (t.name.IndexOf("speaker2", StringComparison.OrdinalIgnoreCase) >= 0) Speaker2 = t;
                }
            }

            if (Speaker1 != null && SpeakerRenderer1 == null) SpeakerRenderer1 = Speaker1.GetComponent<Renderer>();
            if (Speaker2 != null && SpeakerRenderer2 == null) SpeakerRenderer2 = Speaker2.GetComponent<Renderer>();

            // Изначально выключаем свет и свечение на реальном экземпляре, чтобы он не горел желтым при спавне
            if (Light1 != null) { Light1.enabled = false; Light1.intensity = 0f; }
            if (Light2 != null) { Light2.enabled = false; Light2.intensity = 0f; }
            if (SpeakerRenderer1 != null && SpeakerRenderer1.material != null) SpeakerRenderer1.material.SetColor("_EmissionColor", Color.black);
            if (SpeakerRenderer2 != null && SpeakerRenderer2.material != null) SpeakerRenderer2.material.SetColor("_EmissionColor", Color.black);

            initialBodyScale = transform.localScale;
            if (initialBodyScale.sqrMagnitude < 0.001f)
            {
                initialBodyScale = Vector3.one;
            }

            CurrentLoopMode = (LoopMode)Mathf.Clamp(ModConfig.DefaultLoopMode.Value, 0, 2);
            ShuffleEnabled = ModConfig.DefaultShuffle.Value;

            if (AudioSource == null)
            {
                Transform audioCenterTransform = transform.Find("Boombox_AudioCenter");
                GameObject audioCenterObj;
                if (audioCenterTransform == null)
                {
                    audioCenterObj = new GameObject("Boombox_AudioCenter");
                    audioCenterObj.transform.SetParent(transform, false);
                }
                else
                {
                    audioCenterObj = audioCenterTransform.gameObject;
                }

                AudioSource = audioCenterObj.GetComponent<AudioSource>();
                if (AudioSource == null)
                {
                    AudioSource = audioCenterObj.AddComponent<AudioSource>();
                }
                AudioSource.spatialBlend = 1.0f; // 3D sound
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
            else if (SemiFunc.IsMultiplayer() && !PhotonNetwork.IsMasterClient && !photonView.IsMine)
            {
                photonView.RPC(nameof(RequestStateSyncRPC), RpcTarget.MasterClient);
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

        public void RestoreSavedState(
            int trackIdx,
            float time,
            float vol,
            bool muted,
            LoopMode loop,
            bool shuffle,
            bool playing,
            bool isPlaylistMode = false,
            string plOwner = null,
            string plName = null)
        {
            CurrentVolume = Mathf.Clamp01(vol);
            IsMuted = muted;
            ApplyLocalVolume(CurrentVolume, IsMuted);
            CurrentLoopMode = loop;
            ShuffleEnabled = shuffle;

            if (isPlaylistMode && !string.IsNullOrEmpty(plName) && PlaylistManager.Instance != null)
            {
                var tracks = PlaylistManager.Instance.GetTrackList(plOwner, plName);
                if (tracks != null && tracks.Count > 0)
                {
                    IsPlaylistMode = true;
                    ActivePlaylistOwner = plOwner;
                    ActivePlaylistName = plName;
                    ActivePlaylistTracks.Clear();
                    ActivePlaylistTracks.AddRange(tracks);
                    BuildPlaylistQueue(trackIdx, shuffle);

                    if (trackIdx >= 0 && trackIdx < tracks.Count)
                    {
                        CurrentTrackIndex = trackIdx;
                        StartOffsetSeconds = time;
                        if (playing)
                        {
                            photonView.RPC(nameof(PlayTrackRPC), RpcTarget.All, trackIdx, PhotonNetwork.Time, time);
                        }
                    }
                    return;
                }
            }

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
            if (BoomboxItemBuilder.RegisteredPrefab != null && gameObject == BoomboxItemBuilder.RegisteredPrefab)
            {
                return;
            }

            if (!ActiveBoomboxes.Contains(this))
            {
                ActiveBoomboxes.Add(this);
            }
        }

        private void OnDisable()
        {
            ActiveBoomboxes.Remove(this);
        }

        public Vector3 GetAcousticCenterWorld()
        {
            if (Speaker1 != null && Speaker2 != null)
            {
                return (Speaker1.position + Speaker2.position) * 0.5f;
            }
            if (Speaker1 != null) return Speaker1.position;
            if (Speaker2 != null) return Speaker2.position;
            return transform.position;
        }

        private void Update()
        {
            if (AudioSource != null)
            {
                AudioSource.transform.position = GetAcousticCenterWorld();
            }

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
                AnalyzeAudio();
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
                int trackCount = IsPlaylistMode
                    ? ActivePlaylistTracks.Count
                    : (BoomboxAudioManager.Instance != null ? BoomboxAudioManager.Instance.LoadedTracks.Count : 0);

                if (trackCount == 0)
                {
                    // Локальных треков нет: если мультиплеер, запрашиваем воспроизведение у хоста
                    if (SemiFunc.IsMultiplayer())
                    {
                        photonView.RPC(nameof(RequestPlayRPC), RpcTarget.MasterClient);
                    }
                    else
                    {
                        if (BoomboxHUD.Instance != null)
                        {
                            BoomboxHUD.Instance.ShowTrackNotification(BoomboxLocalization.IsRussian ? "⚠️ Нет треков для воспроизведения" : "⚠️ No tracks to play");
                        }
                    }
                    return;
                }

                if (playlistQueue.Count == 0 || currentQueueIndex < 0)
                {
                    RebuildQueue(true);
                }

                int trackToPlay = GetCurrentOrNextQueueTrack();
                if (trackToPlay >= 0)
                {
                    photonView.RPC(nameof(PlayTrackRPC), RpcTarget.All, trackToPlay, PhotonNetwork.Time, 0f);
                }
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
            int trackCount = IsPlaylistMode
                ? ActivePlaylistTracks.Count
                : (BoomboxAudioManager.Instance != null ? BoomboxAudioManager.Instance.LoadedTracks.Count : 0);

            if (trackCount == 0)
            {
                if (SemiFunc.IsMultiplayer())
                {
                    photonView.RPC(nameof(RequestNextTrackRPC), RpcTarget.MasterClient);
                }
                return;
            }

            AdvanceQueueAndPlay(true);
        }

        public void ScrubTime(float deltaSeconds)
        {
            AudioClip clip = (AudioSource != null && AudioSource.clip != null) ? AudioSource.clip :
                             (pocketAudioSource != null ? pocketAudioSource.clip : null);
            if (!IsPlaying || clip == null)
            {
                return;
            }

            float currentTarget = pendingSeekTime >= 0f ? pendingSeekTime : GetCurrentPlaybackTime();
            float newTarget = Mathf.Clamp(currentTarget + deltaSeconds, 0f, clip.length - 1f);

            pendingSeekTime = newTarget;
            seekDebounceTimer = 0.2f;

            if (BoomboxHUD.Instance != null)
            {
                BoomboxHUD.Instance.ShowScrubFeedback(newTarget, clip.length, deltaSeconds);
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
            if (!IsPlaying || CurrentTrackIndex < 0)
            {
                return null;
            }

            if (IsPlaylistMode)
            {
                if (CurrentTrackIndex >= 0 && CurrentTrackIndex < ActivePlaylistTracks.Count)
                {
                    return ActivePlaylistTracks[CurrentTrackIndex].Title;
                }
                return null;
            }

            if (BoomboxAudioManager.Instance == null)
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

        public void PlayPlaylist(string ownerNick, string playlistName, int startingTrackIndex)
        {
            photonView.RPC(nameof(PlayPlaylistRPC), RpcTarget.All, ownerNick, playlistName, startingTrackIndex, PhotonNetwork.Time);
        }

        [PunRPC]
        public void PlayPlaylistRPC(string ownerNick, string playlistName, int startingIndex, double startTime, PhotonMessageInfo info = default)
        {
            if (PlaylistManager.Instance == null)
            {
                return;
            }

            var tracks = PlaylistManager.Instance.GetTrackList(ownerNick, playlistName);
            if (tracks == null || tracks.Count == 0)
            {
                Plugin.Log.LogWarning($"[CustomBoombox] Плейлист '{playlistName}' игрока '{ownerNick}' пуст или не найден.");
                return;
            }

            IsPlaylistMode = true;
            ActivePlaylistOwner = ownerNick;
            ActivePlaylistName = playlistName;
            ActivePlaylistTracks.Clear();
            ActivePlaylistTracks.AddRange(tracks);

            BuildPlaylistQueue(startingIndex, ShuffleEnabled);

            int trackToPlay = (playlistQueue.Count > 0 && currentQueueIndex >= 0 && currentQueueIndex < playlistQueue.Count)
                ? playlistQueue[currentQueueIndex]
                : 0;

            PlayTrackInternal(trackToPlay, startTime, 0f);
        }

        private void BuildPlaylistQueue(int startTrackIdx, bool shuffle)
        {
            playlistQueue.Clear();
            int count = ActivePlaylistTracks.Count;
            if (count == 0) return;

            for (int i = 0; i < count; i++)
            {
                playlistQueue.Add(i);
            }

            if (shuffle && count > 1)
            {
                // Fisher-Yates bag shuffle без повторов
                for (int i = playlistQueue.Count - 1; i > 0; i--)
                {
                    int j = UnityEngine.Random.Range(0, i + 1);
                    int temp = playlistQueue[i];
                    playlistQueue[i] = playlistQueue[j];
                    playlistQueue[j] = temp;
                }

                if (startTrackIdx >= 0 && startTrackIdx < count)
                {
                    int foundIdx = playlistQueue.IndexOf(startTrackIdx);
                    if (foundIdx > 0)
                    {
                        playlistQueue[foundIdx] = playlistQueue[0];
                        playlistQueue[0] = startTrackIdx;
                    }
                }
            }
            else if (startTrackIdx >= 0 && startTrackIdx < count)
            {
                currentQueueIndex = startTrackIdx;
                return;
            }

            currentQueueIndex = 0;
        }

        [PunRPC]
        public void PlayTrackRPC(int trackIndex, double startTime, float startOffset, PhotonMessageInfo info = default)
        {
            PlayTrackInternal(trackIndex, startTime, startOffset);
        }

        private void PlayTrackInternal(int trackIndex, double startTime, float startOffset)
        {
            CurrentTrackIndex = trackIndex;
            PlayStartTime = startTime;
            StartOffsetSeconds = startOffset;
            IsPlaying = true;

            // Немедленно останавливаем воспроизведение предыдущего трека, пока загружается новый
            if (AudioSource != null && AudioSource.isPlaying)
            {
                AudioSource.Stop();
            }
            if (pocketAudioSource != null && pocketAudioSource.isPlaying)
            {
                pocketAudioSource.Stop();
            }
            TurnOffVisuals();

            if (IsPlaylistMode)
            {
                if (trackIndex < 0 || trackIndex >= ActivePlaylistTracks.Count)
                {
                    return;
                }

                PlaylistTrackEntry entry = ActivePlaylistTracks[trackIndex];
                if (BoomboxHUD.Instance != null)
                {
                    string repeatPrefix = CurrentLoopMode == LoopMode.Single ? "🔂 " : (ShuffleEnabled ? "🔀 " : "▶ ");
                    BoomboxHUD.Instance.ShowTrackNotification($"⏳ {repeatPrefix}{entry.Title}...");
                }

                if (YouTubeAudioProvider.Instance != null)
                {
                    YouTubeAudioProvider.Instance.GetAudioClipAsync(entry.Url, entry.Title, clip =>
                    {
                        if (clip != null && IsPlaying && CurrentTrackIndex == trackIndex)
                        {
                            ApplyClipAndPlay(clip, startTime, startOffset, entry.Title);
                        }
                    }, err =>
                    {
                        Plugin.Log.LogError($"[CustomBoombox] Error loading track from playlist: {err}");
                        if (BoomboxHUD.Instance != null)
                        {
                            BoomboxHUD.Instance.ShowTrackNotification($"❌ Error: {entry.Title}");
                        }
                        AdvanceQueueAndPlay(true);
                    });
                }
            }
            else
            {
                if (BoomboxAudioManager.Instance == null)
                {
                    return;
                }

                BoomboxAudioManager.TrackData track = BoomboxAudioManager.Instance.GetTrack(trackIndex);
                if (track != null && track.Clip != null)
                {
                    ApplyClipAndPlay(track.Clip, startTime, startOffset, track.Title);
                }
                else
                {
                    Plugin.Log.LogWarning($"[CustomBoombox] Track at index {trackIndex} not found locally.");
                }
            }
        }

        private void ApplyClipAndPlay(AudioClip clip, double startTime, float startOffset, string title)
        {
            if (clip == null) return;

            // Воспроизводим скачанный трек с самого начала
            float targetOffset = startOffset;
            if (startTime > 0 && targetOffset <= 0f)
            {
                PlayStartTime = PhotonNetwork.Time;
                targetOffset = 0f;
            }
            else if (startTime > 0)
            {
                double timePassed = PhotonNetwork.Time - startTime;
                if (timePassed > 0)
                {
                    targetOffset = (float)(timePassed + startOffset);
                }
            }

            if (AudioSource != null)
            {
                AudioSource.clip = clip;
                ApplyLocalVolume(CurrentVolume, IsMuted);

                if (targetOffset < clip.length)
                {
                    AudioSource.time = targetOffset;
                    AudioSource.Play();
                }
            }

            if (pocketAudioSource != null && IsBoomboxInPocket())
            {
                pocketAudioSource.clip = clip;
                ApplyLocalVolume(CurrentVolume, IsMuted);

                if (targetOffset < clip.length)
                {
                    pocketAudioSource.time = targetOffset;
                    pocketAudioSource.Play();
                }
            }

            if (BoomboxHUD.Instance != null)
            {
                string repeatPrefix = CurrentLoopMode == LoopMode.Single ? "🔂 " : (ShuffleEnabled ? "🔀 " : "▶ ");
                BoomboxHUD.Instance.ShowTrackNotification($"{repeatPrefix}{title}");
            }

            BoomboxStateMemory.Save(this);
        }

        [PunRPC]
        public void RequestPlayRPC(PhotonMessageInfo info = default)
        {
            if (!PhotonNetwork.IsMasterClient)
            {
                return;
            }

            int count = IsPlaylistMode
                ? ActivePlaylistTracks.Count
                : (BoomboxAudioManager.Instance != null ? BoomboxAudioManager.Instance.LoadedTracks.Count : 0);

            if (count > 0)
            {
                if (playlistQueue.Count == 0 || currentQueueIndex < 0)
                {
                    RebuildQueue(true);
                }

                int trackToPlay = GetCurrentOrNextQueueTrack();
                if (trackToPlay >= 0)
                {
                    photonView.RPC(nameof(PlayTrackRPC), RpcTarget.All, trackToPlay, PhotonNetwork.Time, 0f);
                }
            }
        }

        [PunRPC]
        public void RequestNextTrackRPC(PhotonMessageInfo info = default)
        {
            if (!PhotonNetwork.IsMasterClient)
            {
                return;
            }

            int count = IsPlaylistMode
                ? ActivePlaylistTracks.Count
                : (BoomboxAudioManager.Instance != null ? BoomboxAudioManager.Instance.LoadedTracks.Count : 0);

            if (count > 0)
            {
                AdvanceQueueAndPlay(true);
            }
        }

        [PunRPC]
        public void RequestStateSyncRPC(PhotonMessageInfo info = default)
        {
            if (!PhotonNetwork.IsMasterClient && !photonView.IsMine)
            {
                return;
            }

            float currentOffset = GetCurrentPlaybackTime();
            photonView.RPC(
                nameof(SyncFullStateRPC),
                info.Sender,
                IsPlaying,
                CurrentTrackIndex,
                currentOffset,
                CurrentVolume,
                IsMuted,
                (int)CurrentLoopMode,
                ShuffleEnabled,
                IsPlaylistMode,
                ActivePlaylistOwner ?? "",
                ActivePlaylistName ?? ""
            );
        }

        [PunRPC]
        public void SyncFullStateRPC(
            bool playing,
            int trackIndex,
            float currentOffset,
            float volume,
            bool muted,
            int loopMode,
            bool shuffle,
            bool isPlaylist,
            string plOwner,
            string plName,
            PhotonMessageInfo info = default)
        {
            CurrentVolume = Mathf.Clamp01(volume);
            IsMuted = muted;
            ApplyLocalVolume(CurrentVolume, IsMuted);
            CurrentLoopMode = (LoopMode)loopMode;
            ShuffleEnabled = shuffle;

            if (!playing || trackIndex < 0)
            {
                IsPlaying = false;
                if (AudioSource != null && AudioSource.isPlaying)
                {
                    AudioSource.Stop();
                }
                return;
            }

            if (isPlaylist && !string.IsNullOrEmpty(plName) && PlaylistManager.Instance != null)
            {
                var tracks = PlaylistManager.Instance.GetTrackList(plOwner, plName);
                if (tracks != null && tracks.Count > 0)
                {
                    IsPlaylistMode = true;
                    ActivePlaylistOwner = plOwner;
                    ActivePlaylistName = plName;
                    ActivePlaylistTracks.Clear();
                    ActivePlaylistTracks.AddRange(tracks);
                    BuildPlaylistQueue(trackIndex, shuffle);
                }
            }

            PlayTrackInternal(trackIndex, PhotonNetwork.Time, currentOffset);
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

            int count = IsPlaylistMode
                ? ActivePlaylistTracks.Count
                : (BoomboxAudioManager.Instance != null ? BoomboxAudioManager.Instance.LoadedTracks.Count : 0);

            if (count == 0)
            {
                return;
            }

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

            if (playlistQueue.Count == 0)
            {
                return;
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

            if (playlistQueue.Count == 0)
            {
                return -1;
            }

            if (currentQueueIndex < 0 || currentQueueIndex >= playlistQueue.Count)
            {
                currentQueueIndex = 0;
            }

            return playlistQueue[currentQueueIndex];
        }

        private void RebuildQueue(bool shuffle)
        {
            if (IsPlaylistMode)
            {
                BuildPlaylistQueue(CurrentTrackIndex, shuffle);
                return;
            }

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
            AudioSource activeSrc = (AudioSource != null && AudioSource.isPlaying) ? AudioSource :
                                    (pocketAudioSource != null && pocketAudioSource.isPlaying) ? pocketAudioSource : null;

            if (activeSrc == null)
            {
                currentRMS = 0f;
                smoothedRMS = Mathf.Lerp(smoothedRMS, 0f, Time.deltaTime * 15f);
                return;
            }

            activeSrc.GetOutputData(audioSamples, 0);

            float sum = 0f;
            for (int i = 0; i < audioSamples.Length; i++)
            {
                sum += audioSamples[i] * audioSamples[i];
            }

            currentRMS = Mathf.Sqrt(sum / audioSamples.Length);
            smoothedRMS = Mathf.Lerp(smoothedRMS, currentRMS, Time.deltaTime * 25f);
        }

        private void EnsureVisualReferences()
        {
            if (Light1 == null || Light2 == null)
            {
                Light[] allLights = GetComponentsInChildren<Light>(true);
                if (allLights.Length > 0 && Light1 == null) Light1 = allLights[0];
                if (allLights.Length > 1 && Light2 == null) Light2 = allLights[1];
            }

            if (Speaker1 == null || Speaker2 == null)
            {
                Transform[] transforms = GetComponentsInChildren<Transform>(true);
                foreach (Transform t in transforms)
                {
                    if (t.name.IndexOf("speaker1", StringComparison.OrdinalIgnoreCase) >= 0 && Speaker1 == null) Speaker1 = t;
                    else if (t.name.IndexOf("speaker2", StringComparison.OrdinalIgnoreCase) >= 0 && Speaker2 == null) Speaker2 = t;
                }
            }

            if (Speaker1 != null && SpeakerRenderer1 == null) SpeakerRenderer1 = Speaker1.GetComponent<Renderer>();
            if (Speaker2 != null && SpeakerRenderer2 == null) SpeakerRenderer2 = Speaker2.GetComponent<Renderer>();
        }

        private void AnimateVisuals()
        {
            if (IsBoomboxInPocket())
            {
                return;
            }

            EnsureVisualReferences();

            bool pulseEnabled = ModConfig.EnableVisualPulsing != null && ModConfig.EnableVisualPulsing.Value;
            if (pulseEnabled && !IsEquipTransitioning())
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
            else if (!IsEquipTransitioning())
            {
                if (Speaker1 != null && Speaker1.localScale != Vector3.one) Speaker1.localScale = Vector3.one;
                if (Speaker2 != null && Speaker2.localScale != Vector3.one) Speaker2.localScale = Vector3.one;
                if (transform.localScale != initialBodyScale) transform.localScale = initialBodyScale;
            }

            // Светомузыка
            bool enableLights = ModConfig.EnableLightMusic == null || ModConfig.EnableLightMusic.Value;
            if (!enableLights)
            {
                if (Light1 != null && Light1.enabled) { Light1.intensity = 0f; Light1.enabled = false; }
                if (Light2 != null && Light2.enabled) { Light2.intensity = 0f; Light2.enabled = false; }
                if (SpeakerRenderer1 != null && SpeakerRenderer1.material != null) SpeakerRenderer1.material.SetColor("_EmissionColor", Color.black);
                if (SpeakerRenderer2 != null && SpeakerRenderer2.material != null) SpeakerRenderer2.material.SetColor("_EmissionColor", Color.black);
                return;
            }

            float brightness = ModConfig.LightMusicBrightness != null ? Mathf.Clamp(ModConfig.LightMusicBrightness.Value, 0f, 2f) : 0.4f;
            float hue = Mathf.PingPong(Time.time * 0.5f, 1f);
            Color glowColor = Color.HSVToRGB(hue, 1f, 1f);

            float emissionMult = (0.5f + smoothedRMS * 1.5f) * brightness;
            if (SpeakerRenderer1 != null && SpeakerRenderer1.material != null)
            {
                SpeakerRenderer1.material.SetColor("_EmissionColor", glowColor * emissionMult);
            }

            if (SpeakerRenderer2 != null && SpeakerRenderer2.material != null)
            {
                SpeakerRenderer2.material.SetColor("_EmissionColor", glowColor * emissionMult);
            }

            float lightIntensity = (1.5f + smoothedRMS * 6.0f) * brightness;

            if (Light1 != null)
            {
                Light1.type = LightType.Point;
                Light1.range = 6.0f;
                Light1.shadows = LightShadows.None;
                Light1.renderMode = LightRenderMode.ForcePixel;
                Light1.enabled = true;
                Light1.color = glowColor;
                Light1.intensity = lightIntensity;
                if (Speaker1 != null)
                {
                    Light1.transform.position = Speaker1.position + Speaker1.forward * 0.12f;
                }
            }

            if (Light2 != null)
            {
                Light2.type = LightType.Point;
                Light2.range = 6.0f;
                Light2.shadows = LightShadows.None;
                Light2.renderMode = LightRenderMode.ForcePixel;
                Light2.enabled = true;
                Light2.color = glowColor;
                Light2.intensity = lightIntensity;
                if (Speaker2 != null)
                {
                    Light2.transform.position = Speaker2.position + Speaker2.forward * 0.12f;
                }
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
            if (IsBoomboxInPocket())
            {
                return;
            }

            if (!IsEquipTransitioning())
            {
                transform.localScale = Vector3.Lerp(transform.localScale, initialBodyScale, Time.deltaTime * 10f);
            }

            if (Speaker1 != null) Speaker1.localScale = Vector3.one;
            if (Speaker2 != null) Speaker2.localScale = Vector3.one;

            if (Light1 != null)
            {
                Light1.intensity = 0f;
                Light1.enabled = false;
            }

            if (Light2 != null)
            {
                Light2.intensity = 0f;
                Light2.enabled = false;
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

                // Удаляем скрипты, физику и коллайдеры с косметической копии
                foreach (Component comp in pocketVisual.GetComponentsInChildren<Component>(true))
                {
                    if (comp is PhysGrabObject || comp is Rigidbody || comp is Collider ||
                        comp is PhotonView || comp is ItemAttributes || comp is ItemEquippable ||
                        comp is CustomBoomboxItem || comp is RoomVolumeCheck || comp is ItemToggle ||
                        comp is PhysGrabObjectImpactDetector)
                    {
                        UnityEngine.Object.DestroyImmediate(comp);
                    }
                }

                // Ищем динамики и свет
                Transform[] allTransforms = pocketVisual.GetComponentsInChildren<Transform>(true);
                foreach (Transform t in allTransforms)
                {
                    if (t.name.IndexOf("speaker1", StringComparison.OrdinalIgnoreCase) >= 0) pocketSpeaker1 = t;
                    if (t.name.IndexOf("speaker2", StringComparison.OrdinalIgnoreCase) >= 0) pocketSpeaker2 = t;
                }

                Light[] lights = pocketVisual.GetComponentsInChildren<Light>(true);
                if (lights.Length > 0) pocketLight1 = lights[0];
                if (lights.Length > 1) pocketLight2 = lights[1];

                if (pocketLight1 != null) { pocketLight1.enabled = false; pocketLight1.intensity = 0f; }
                if (pocketLight2 != null) { pocketLight2.enabled = false; pocketLight2.intensity = 0f; }

                if (pocketSpeaker1 != null) pocketSpeakerRenderer1 = pocketSpeaker1.GetComponent<Renderer>();
                if (pocketSpeaker2 != null) pocketSpeakerRenderer2 = pocketSpeaker2.GetComponent<Renderer>();

                if (pocketSpeakerRenderer1 != null && pocketSpeakerRenderer1.material != null) pocketSpeakerRenderer1.material.SetColor("_EmissionColor", Color.black);
                if (pocketSpeakerRenderer2 != null && pocketSpeakerRenderer2.material != null) pocketSpeakerRenderer2.material.SetColor("_EmissionColor", Color.black);

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

            PlayerAvatar owner = null;
            if (ItemEquippable != null)
            {
                owner = ItemEquippable.GetOwnerPlayerAvatar();
            }
            if (owner == null)
            {
                owner = SemiFunc.PlayerAvatarLocal();
            }
            if (owner == null && PlayerAvatar.instance != null)
            {
                owner = PlayerAvatar.instance;
            }

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
            Quaternion targetRot = anchor.rotation;

            pocketVisual.transform.position = Vector3.Lerp(pocketVisual.transform.position, targetPos, Time.deltaTime * 20f);
            pocketVisual.transform.rotation = Quaternion.Slerp(pocketVisual.transform.rotation, targetRot, Time.deltaTime * 20f);

            // Синхронизация 3D звука при нахождении в кармане
            if (IsPlaying && AudioSource != null && AudioSource.clip != null)
            {
                // Глушим основной AudioSource, который телепортирован далеко под карту
                AudioSource.volume = 0f;

                // Выключаем свет на основном телепортированном бумбоксе, пока он в кармане
                if (Light1 != null && Light1.enabled) { Light1.enabled = false; Light1.intensity = 0f; }
                if (Light2 != null && Light2.enabled) { Light2.enabled = false; Light2.intensity = 0f; }
                if (SpeakerRenderer1 != null && SpeakerRenderer1.material != null) SpeakerRenderer1.material.SetColor("_EmissionColor", Color.black);
                if (SpeakerRenderer2 != null && SpeakerRenderer2.material != null) SpeakerRenderer2.material.SetColor("_EmissionColor", Color.black);

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
            EnsureVisualReferences();
        }

        private void AnimatePocketVisuals()
        {
            float spScale = 1f + smoothedRMS * 0.45f;
            Vector3 targetScale = new Vector3(spScale, spScale, spScale);
            if (pocketSpeaker1 != null) pocketSpeaker1.localScale = targetScale;
            if (pocketSpeaker2 != null) pocketSpeaker2.localScale = targetScale;

            bool enableLights = ModConfig.EnableLightMusic == null || ModConfig.EnableLightMusic.Value;
            if (!enableLights)
            {
                if (pocketLight1 != null && pocketLight1.enabled) pocketLight1.enabled = false;
                if (pocketLight2 != null && pocketLight2.enabled) pocketLight2.enabled = false;
                if (pocketSpeakerRenderer1 != null && pocketSpeakerRenderer1.material != null) pocketSpeakerRenderer1.material.SetColor("_EmissionColor", Color.black);
                if (pocketSpeakerRenderer2 != null && pocketSpeakerRenderer2.material != null) pocketSpeakerRenderer2.material.SetColor("_EmissionColor", Color.black);
                return;
            }

            float hue = Mathf.PingPong(Time.time * 0.5f, 1f);
            Color glowColor = Color.HSVToRGB(hue, 1f, 1f);
            float brightness = ModConfig.LightMusicBrightness != null ? Mathf.Clamp(ModConfig.LightMusicBrightness.Value, 0f, 2f) : 0.4f;
            float pocketIntensity = (1.2f + smoothedRMS * 4.0f) * brightness;

            if (pocketLight1 != null)
            {
                pocketLight1.type = LightType.Point;
                pocketLight1.range = 5.0f;
                pocketLight1.shadows = LightShadows.None;
                pocketLight1.renderMode = LightRenderMode.ForcePixel;
                pocketLight1.enabled = true;
                pocketLight1.color = glowColor;
                pocketLight1.intensity = pocketIntensity;
            }

            if (pocketLight2 != null)
            {
                pocketLight2.type = LightType.Point;
                pocketLight2.range = 5.0f;
                pocketLight2.shadows = LightShadows.None;
                pocketLight2.renderMode = LightRenderMode.ForcePixel;
                pocketLight2.enabled = true;
                pocketLight2.color = glowColor;
                pocketLight2.intensity = pocketIntensity;
            }

            float pocketEmission = (0.5f + smoothedRMS * 1.5f) * brightness;
            if (pocketSpeakerRenderer1 != null && pocketSpeakerRenderer1.material != null)
            {
                pocketSpeakerRenderer1.material.SetColor("_EmissionColor", glowColor * pocketEmission);
            }

            if (pocketSpeakerRenderer2 != null && pocketSpeakerRenderer2.material != null)
            {
                pocketSpeakerRenderer2.material.SetColor("_EmissionColor", glowColor * pocketEmission);
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
