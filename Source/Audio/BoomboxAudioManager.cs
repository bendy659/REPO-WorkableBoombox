using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.Networking;
using WorkableBoombox.Config;

namespace WorkableBoombox.Audio
{
    public class BoomboxAudioManager : MonoBehaviour
    {
        public class TrackData
        {
            public string FileName;
            public string Title;
            public string FilePath;
            public AudioClip Clip;
        }

        public static BoomboxAudioManager Instance { get; private set; }

        public static string TracksDirectory
        {
            get
            {
                try
                {
                    string fallback = Path.Combine(Paths.PluginPath, "WorkableBoombox", "Tracks");
                    if (Directory.Exists(fallback))
                    {
                        return fallback;
                    }

                    string assemblyDir = Path.GetDirectoryName(typeof(BoomboxAudioManager).Assembly.Location);
                    if (!string.IsNullOrEmpty(assemblyDir))
                    {
                        return Path.Combine(assemblyDir, "Tracks");
                    }
                }
                catch
                {
                }

                return Path.Combine(Paths.PluginPath, "WorkableBoombox", "Tracks");
            }
        }

        public List<TrackData> LoadedTracks { get; } = new List<TrackData>();
        public bool IsLoaded { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (!Directory.Exists(TracksDirectory))
            {
                Directory.CreateDirectory(TracksDirectory);
            }
        }

        private void Start()
        {
            StartCoroutine(ReloadTracksCoroutine());
        }

        public void ReloadTracks()
        {
            StartCoroutine(ReloadTracksCoroutine());
        }

        public IEnumerator ReloadTracksCoroutine()
        {
            IsLoaded = false;
            LoadedTracks.Clear();

            if (!Directory.Exists(TracksDirectory))
            {
                Plugin.Log.LogWarning($"[BoomboxAudioManager] Папка треков не найдена: {TracksDirectory}");
                IsLoaded = true;
                yield break;
            }

            string[] files = Directory.GetFiles(TracksDirectory);
            Plugin.Log.LogInfo($"[BoomboxAudioManager] Найдено {files.Length} файлов в папке треков. Начинаем загрузку...");

            foreach (string filePath in files)
            {
                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                bool isAudio = ext == ".mp3" || ext == ".ogg" || ext == ".wav" || ext == ".aiff";
                if (!isAudio)
                {
                    continue;
                }

                AudioType audioType = AudioType.UNKNOWN;
                if (ext == ".ogg")
                {
                    audioType = AudioType.OGGVORBIS;
                }
                else if (ext == ".wav")
                {
                    audioType = AudioType.WAV;
                }
                else if (ext == ".mp3")
                {
                    audioType = AudioType.MPEG;
                }
                else if (ext == ".aiff")
                {
                    audioType = AudioType.AIFF;
                }

                string uri = "file://" + Path.GetFullPath(filePath);
                using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, audioType))
                {
                    yield return www.SendWebRequest();

                    bool hasError = www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError;
                    if (hasError)
                    {
                        Plugin.Log.LogError($"[BoomboxAudioManager] Ошибка загрузки {Path.GetFileName(filePath)}: {www.error}");
                        continue;
                    }

                    AudioClip clip = null;
                    try
                    {
                        clip = DownloadHandlerAudioClip.GetContent(www);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogError($"[BoomboxAudioManager] Ошибка декодирования {Path.GetFileName(filePath)}: {ex.Message}");
                    }

                    if (clip != null)
                    {
                        string fileName = Path.GetFileName(filePath);
                        clip.name = Path.GetFileNameWithoutExtension(filePath);
                        LoadedTracks.Add(new TrackData
                        {
                            FileName = fileName,
                            Title = clip.name,
                            FilePath = filePath,
                            Clip = clip
                        });
                        Plugin.Log.LogInfo($"[BoomboxAudioManager] Загружен трек: {clip.name} ({clip.length:F1} сек)");
                    }
                    else
                    {
                        Plugin.Log.LogWarning($"[BoomboxAudioManager] Не удалось декодировать аудиоклип из '{Path.GetFileName(filePath)}'.");
                    }
                }
            }

            // Сортировка по имени для одинаковых индексов у всех игроков в сети
            LoadedTracks.Sort((a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase));

            IsLoaded = true;
            Plugin.Log.LogInfo($"[BoomboxAudioManager] Загрузка треков завершена. Всего готово: {LoadedTracks.Count}");
        }

        public TrackData GetTrack(int index)
        {
            bool isValid = index >= 0 && index < LoadedTracks.Count;
            if (isValid)
            {
                return LoadedTracks[index];
            }

            return null;
        }

        public int GetRandomTrackIndex(int currentIndex)
        {
            if (LoadedTracks.Count <= 1)
            {
                return 0;
            }

            int nextIndex;
            do
            {
                nextIndex = UnityEngine.Random.Range(0, LoadedTracks.Count);
            } while (nextIndex == currentIndex && LoadedTracks.Count > 1);

            return nextIndex;
        }
    }
}
