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

        public static List<string> GetAllTrackDirectories()
        {
            List<string> dirs = new List<string>();
            try
            {
                string assemblyDir = Path.GetDirectoryName(typeof(BoomboxAudioManager).Assembly.Location);
                if (!string.IsNullOrEmpty(assemblyDir))
                {
                    string d1 = Path.Combine(assemblyDir, "Tracks");
                    if (Directory.Exists(d1) && !dirs.Contains(d1)) dirs.Add(d1);
                }

                string d2 = Path.Combine(Paths.PluginPath, "WorkableBoombox", "Tracks");
                if (Directory.Exists(d2) && !dirs.Contains(d2)) dirs.Add(d2);

                string d3 = Path.Combine(Paths.PluginPath, "bendy659-WorkableBoombox", "Tracks");
                if (Directory.Exists(d3) && !dirs.Contains(d3)) dirs.Add(d3);

                if (Directory.Exists(Paths.PluginPath))
                {
                    string[] subDirs = Directory.GetDirectories(Paths.PluginPath, "*WorkableBoombox*", SearchOption.TopDirectoryOnly);
                    foreach (string sd in subDirs)
                    {
                        string candidate = Path.Combine(sd, "Tracks");
                        if (Directory.Exists(candidate) && !dirs.Contains(candidate))
                        {
                            dirs.Add(candidate);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[BoomboxAudioManager] Ошибка поиска директорий треков: {ex.Message}");
            }
            return dirs;
        }

        public static string TracksDirectory
        {
            get
            {
                try
                {
                    List<string> existing = GetAllTrackDirectories();
                    if (existing.Count > 0)
                    {
                        return existing[0];
                    }

                    string assemblyDir = Path.GetDirectoryName(typeof(BoomboxAudioManager).Assembly.Location);
                    if (!string.IsNullOrEmpty(assemblyDir))
                    {
                        string localTracks = Path.Combine(assemblyDir, "Tracks");
                        if (!Directory.Exists(localTracks))
                        {
                            Directory.CreateDirectory(localTracks);
                        }
                        return localTracks;
                    }
                }
                catch
                {
                }

                string fallback = Path.Combine(Paths.PluginPath, "WorkableBoombox", "Tracks");
                if (!Directory.Exists(fallback))
                {
                    Directory.CreateDirectory(fallback);
                }
                return fallback;
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

            string primaryDir = TracksDirectory;
            Plugin.Log.LogInfo($"[BoomboxAudioManager] Основная папка треков: {primaryDir}");
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

            List<string> searchDirs = GetAllTrackDirectories();
            if (searchDirs.Count == 0)
            {
                searchDirs.Add(TracksDirectory);
            }

            Dictionary<string, string> uniqueAudioFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string dir in searchDirs)
            {
                if (!Directory.Exists(dir)) continue;

                string[] files = Directory.GetFiles(dir);
                foreach (string filePath in files)
                {
                    string ext = Path.GetExtension(filePath).ToLowerInvariant();
                    bool isAudio = ext == ".mp3" || ext == ".ogg" || ext == ".wav" || ext == ".aiff";
                    if (!isAudio) continue;

                    string fileName = Path.GetFileName(filePath);
                    if (!uniqueAudioFiles.ContainsKey(fileName))
                    {
                        uniqueAudioFiles[fileName] = filePath;
                    }
                }
            }

            Plugin.Log.LogInfo($"[BoomboxAudioManager] Найдено {uniqueAudioFiles.Count} уникальных аудиофайлов в {searchDirs.Count} папках. Начинаем загрузку...");

            foreach (KeyValuePair<string, string> kvp in uniqueAudioFiles)
            {
                string fileName = kvp.Key;
                string filePath = kvp.Value;
                string ext = Path.GetExtension(filePath).ToLowerInvariant();

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

                string uri = new Uri(Path.GetFullPath(filePath)).AbsoluteUri;
                using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, audioType))
                {
                    yield return www.SendWebRequest();

                    bool hasError = www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError;
                    if (hasError)
                    {
                        Plugin.Log.LogError($"[BoomboxAudioManager] Ошибка загрузки {fileName}: {www.error}");
                        continue;
                    }

                    AudioClip clip = null;
                    try
                    {
                        clip = DownloadHandlerAudioClip.GetContent(www);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogError($"[BoomboxAudioManager] Ошибка декодирования {fileName}: {ex.Message}");
                    }

                    if (clip != null)
                    {
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
                        Plugin.Log.LogWarning($"[BoomboxAudioManager] Не удалось декодировать аудиоклип из '{fileName}'.");
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
