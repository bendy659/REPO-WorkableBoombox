using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using BepInEx;
using UnityEngine;
using UnityEngine.Networking;
using WorkableBoombox.Config;

namespace WorkableBoombox.Audio
{
    public class YouTubeAudioProvider : MonoBehaviour
    {
        public static YouTubeAudioProvider Instance { get; private set; }

        public static string CacheDirectory => Path.Combine(Paths.ConfigPath, "WorkableBoombox", "Cache");

        private static readonly Regex YouTubeVideoIdRegex = new Regex(
            @"(?:youtube\.com\/(?:[^\/]+\/.+\/|(?:v|e(?:mbed)?)\/|.*[?&]v=)|youtu\.be\/|youtube\.com\/shorts\/)([^""&?\/\s]{11})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly Dictionary<string, AudioClip> memoryClipCache = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> activeDownloads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private class ForceAcceptAllCertificate : CertificateHandler
        {
            protected override bool ValidateCertificate(byte[] certificateData) => true;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (!Directory.Exists(CacheDirectory))
            {
                Directory.CreateDirectory(CacheDirectory);
            }
        }

        public static bool IsYouTubeUrl(string url, out string videoId)
        {
            videoId = null;
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            Match match = YouTubeVideoIdRegex.Match(url);
            if (match.Success && match.Groups.Count > 1)
            {
                videoId = match.Groups[1].Value;
                return true;
            }

            return false;
        }

        public static string GetYtDlpPath()
        {
            string[] candidatePaths = new[]
            {
                Path.Combine(Paths.ConfigPath, "WorkableBoombox", "yt-dlp.exe"),
                Path.Combine(Paths.PluginPath, "WorkableBoombox", "yt-dlp.exe"),
                Path.Combine(Paths.PluginPath, "bendy659-WorkableBoombox", "yt-dlp.exe"),
                Path.Combine(Path.GetDirectoryName(typeof(YouTubeAudioProvider).Assembly.Location) ?? "", "yt-dlp.exe")
            };

            foreach (string path in candidatePaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return Path.Combine(Paths.ConfigPath, "WorkableBoombox", "yt-dlp.exe");
        }

        public bool IsAudioCached(string source, out string cachedFilePath)
        {
            cachedFilePath = null;

            if (IsYouTubeUrl(source, out string videoId))
            {
                string[] exts = new[] { ".m4a", ".mp3", ".ogg", ".wav" };
                foreach (string ext in exts)
                {
                    string candidate = Path.Combine(CacheDirectory, videoId + ext);
                    if (File.Exists(candidate) && new FileInfo(candidate).Length > 1024)
                    {
                        cachedFilePath = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        public void GetAudioClipAsync(string source, string trackTitle, Action<AudioClip> onComplete, Action<string> onError)
        {
            StartCoroutine(GetAudioClipCoroutine(source, trackTitle, onComplete, onError));
        }

        private IEnumerator GetAudioClipCoroutine(string source, string trackTitle, Action<AudioClip> onComplete, Action<string> onError)
        {
            // 1. Проверяем in-memory кэш
            if (memoryClipCache.TryGetValue(source, out AudioClip cachedClip) && cachedClip != null)
            {
                onComplete?.Invoke(cachedClip);
                yield break;
            }

            // 2. Обработка локального трека "local:ИмяФайла"
            if (source.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
            {
                string localName = source.Substring(6).Trim();
                AudioClip localClip = FindLocalAudioClip(localName);
                if (localClip != null)
                {
                    memoryClipCache[source] = localClip;
                    onComplete?.Invoke(localClip);
                }
                else
                {
                    onError?.Invoke($"Локальный трек '{localName}' не найден в папках Tracks.");
                }
                yield break;
            }

            // 3. Обработка YouTube URL
            if (IsYouTubeUrl(source, out string videoId))
            {
                // Проверяем дисковый кэш
                if (!IsAudioCached(source, out string cachedFile))
                {
                    // Проверяем наличие yt-dlp.exe
                    string ytDlpPath = GetYtDlpPath();
                    if (!File.Exists(ytDlpPath))
                    {
                        Plugin.Log.LogInfo("[YouTubeAudio] yt-dlp.exe не найден. Начинаем автоматическую загрузку yt-dlp...");
                        yield return DownloadYtDlpCoroutine(ytDlpPath);
                    }

                    if (!File.Exists(ytDlpPath))
                    {
                        onError?.Invoke("Не удалось найти или скачать yt-dlp.exe для загрузки YouTube.");
                        yield break;
                    }

                    // Скачиваем аудио через yt-dlp
                    Plugin.Log.LogInfo($"[YouTubeAudio] Запуск скачивания YouTube: {trackTitle} ({videoId})...");
                    yield return DownloadYouTubeAudioCoroutine(ytDlpPath, source, videoId);

                    if (!IsAudioCached(source, out cachedFile))
                    {
                        onError?.Invoke($"Ошибка скачивания YouTube аудио для {videoId}.");
                        yield break;
                    }
                }

                // Загружаем аудиофайл в AudioClip
                yield return LoadAudioClipFromFileCoroutine(cachedFile, clip =>
                {
                    if (clip != null)
                    {
                        clip.name = string.IsNullOrEmpty(trackTitle) ? videoId : trackTitle;
                        memoryClipCache[source] = clip;
                        onComplete?.Invoke(clip);
                    }
                    else
                    {
                        onError?.Invoke($"Не удалось декодировать аудиофайл {cachedFile}.");
                    }
                });
                yield break;
            }

            // 4. Прямая ссылка на веб-аудио (HTTP/HTTPS)
            if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                AudioType aType = AudioType.UNKNOWN;
                string lower = source.ToLowerInvariant();
                if (lower.Contains(".ogg")) aType = AudioType.OGGVORBIS;
                else if (lower.Contains(".mp3")) aType = AudioType.MPEG;
                else if (lower.Contains(".wav")) aType = AudioType.WAV;
                else if (lower.Contains(".m4a") || lower.Contains(".aac")) aType = AudioType.ACC;

                using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(source, aType))
                {
                    www.certificateHandler = new ForceAcceptAllCertificate();
                    yield return www.SendWebRequest();

                    if (www.result == UnityWebRequest.Result.Success)
                    {
                        AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                        if (clip != null)
                        {
                            clip.name = string.IsNullOrEmpty(trackTitle) ? "WebTrack" : trackTitle;
                            memoryClipCache[source] = clip;
                            onComplete?.Invoke(clip);
                            yield break;
                        }
                    }

                    onError?.Invoke($"Ошибка загрузки прямого URL: {www.error}");
                }
                yield break;
            }

            onError?.Invoke($"Неизвестный формат источника: {source}");
        }

        private AudioClip FindLocalAudioClip(string localName)
        {
            if (BoomboxAudioManager.Instance != null)
            {
                foreach (var track in BoomboxAudioManager.Instance.LoadedTracks)
                {
                    if (track != null && track.Clip != null)
                    {
                        if (string.Equals(track.FileName, localName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(track.Title, localName, StringComparison.OrdinalIgnoreCase))
                        {
                            return track.Clip;
                        }
                    }
                }
            }

            return null;
        }

        private IEnumerator DownloadYtDlpCoroutine(string destinationPath)
        {
            string url = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.certificateHandler = new ForceAcceptAllCertificate();
                req.downloadHandler = new DownloadHandlerFile(destinationPath);
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    Plugin.Log.LogInfo("[YouTubeAudio] yt-dlp.exe успешно загружен!");
                }
                else
                {
                    Plugin.Log.LogError($"[YouTubeAudio] Ошибка загрузки yt-dlp.exe: {req.error}");
                }
            }
        }

        private IEnumerator DownloadYouTubeAudioCoroutine(string ytDlpPath, string url, string videoId)
        {
            if (activeDownloads.Contains(videoId))
            {
                while (activeDownloads.Contains(videoId))
                {
                    yield return new WaitForSeconds(0.2f);
                }
                yield break;
            }

            activeDownloads.Add(videoId);
            bool isProcessDone = false;

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string outputTemplate = Path.Combine(CacheDirectory, "%(id)s.%(ext)s");
                    // Скачиваем поток m4a (aac) без перекодирования - это быстрее всего
                    string args = $"-f \"ba[ext=m4a]/ba/b\" --no-playlist --no-check-certificates -o \"{outputTemplate}\" \"{url}\"";

                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = ytDlpPath,
                        Arguments = args,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using (Process proc = Process.Start(psi))
                    {
                        proc.WaitForExit(30000); // Тайм-аут 30 секунд
                        if (proc.ExitCode == 0)
                        {
                            Plugin.Log.LogInfo($"[YouTubeAudio] Завершена загрузка {videoId}");
                        }
                        else
                        {
                            string err = proc.StandardError.ReadToEnd();
                            Plugin.Log.LogWarning($"[YouTubeAudio] yt-dlp exit code {proc.ExitCode}: {err}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[YouTubeAudio] Исключение при выполнении yt-dlp: {ex.Message}");
                }
                finally
                {
                    isProcessDone = true;
                }
            });

            while (!isProcessDone)
            {
                yield return new WaitForSeconds(0.1f);
            }

            activeDownloads.Remove(videoId);
        }

        private IEnumerator LoadAudioClipFromFileCoroutine(string filePath, Action<AudioClip> callback)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            AudioType aType = AudioType.UNKNOWN;

            if (ext == ".m4a" || ext == ".aac")
            {
                aType = AudioType.ACC;
            }
            else if (ext == ".mp3")
            {
                aType = AudioType.MPEG;
            }
            else if (ext == ".ogg")
            {
                aType = AudioType.OGGVORBIS;
            }
            else if (ext == ".wav")
            {
                aType = AudioType.WAV;
            }

            string uri = new Uri(Path.GetFullPath(filePath)).AbsoluteUri;
            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, aType))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                    callback?.Invoke(clip);
                }
                else
                {
                    Plugin.Log.LogError($"[YouTubeAudio] Ошибка загрузки клипа из файла {filePath}: {www.error}");
                    callback?.Invoke(null);
                }
            }
        }
    }
}
