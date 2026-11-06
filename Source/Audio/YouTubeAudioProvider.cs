using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using BepInEx;
using UnityEngine;
using UnityEngine.Networking;
using WorkableBoombox.Config;
using WorkableBoombox.Localization;
using WorkableBoombox.UI;

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

        public static string GetFFmpegPath()
        {
            string[] candidatePaths = new[]
            {
                Path.Combine(Paths.ConfigPath, "WorkableBoombox", "ffmpeg.exe"),
                Path.Combine(Paths.PluginPath, "WorkableBoombox", "ffmpeg.exe"),
                Path.Combine(Paths.PluginPath, "bendy659-WorkableBoombox", "ffmpeg.exe"),
                Path.Combine(Path.GetDirectoryName(typeof(YouTubeAudioProvider).Assembly.Location) ?? "", "ffmpeg.exe")
            };

            foreach (string path in candidatePaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return Path.Combine(Paths.ConfigPath, "WorkableBoombox", "ffmpeg.exe");
        }

        public static bool TryConvertWithFFmpeg(string ffmpegPath, string inputFile, string outputFile)
        {
            try
            {
                if (!File.Exists(ffmpegPath) || !File.Exists(inputFile)) return false;

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-i \"{inputFile}\" -codec:a libmp3lame -qscale:a 2 \"{outputFile}\" -y",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                using (Process proc = Process.Start(psi))
                {
                    proc.WaitForExit(20000);
                    return proc.ExitCode == 0 && File.Exists(outputFile);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[YouTubeAudio] Ошибка при транскодировании через ffmpeg: {ex.Message}");
                return false;
            }
        }

        public bool IsAudioCached(string source, out string cachedFilePath)
        {
            cachedFilePath = null;

            if (IsYouTubeUrl(source, out string videoId))
            {
                // Приоритетно ищем MP3, так как Unity FMOD декодирует его без ошибок
                string[] preferredExts = new[] { ".mp3", ".ogg", ".wav" };
                foreach (string ext in preferredExts)
                {
                    string candidate = Path.Combine(CacheDirectory, videoId + ext);
                    if (File.Exists(candidate) && new FileInfo(candidate).Length > 1024)
                    {
                        cachedFilePath = candidate;
                        return true;
                    }
                }

                // Если в кэше остался старый .m4a, пробуем сконвертировать его в .mp3 на лету
                string m4aCandidate = Path.Combine(CacheDirectory, videoId + ".m4a");
                if (File.Exists(m4aCandidate) && new FileInfo(m4aCandidate).Length > 1024)
                {
                    string ffmpegPath = GetFFmpegPath();
                    if (File.Exists(ffmpegPath))
                    {
                        string mp3Candidate = Path.Combine(CacheDirectory, videoId + ".mp3");
                        if (TryConvertWithFFmpeg(ffmpegPath, m4aCandidate, mp3Candidate))
                        {
                            cachedFilePath = mp3Candidate;
                            return true;
                        }
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

                    // Проверяем наличие ffmpeg.exe для надежного декодирования MP3 в FMOD
                    string ffmpegPath = GetFFmpegPath();
                    if (!File.Exists(ffmpegPath))
                    {
                        Plugin.Log.LogInfo("[YouTubeAudio] ffmpeg.exe не найден. Начинаем автоматическую загрузку ffmpeg...");
                        yield return DownloadFFmpegCoroutine(ffmpegPath);
                    }

                    // Скачиваем аудио через yt-dlp (с перекодированием в mp3 через ffmpeg)
                    Plugin.Log.LogInfo($"[YouTubeAudio] Запуск скачивания YouTube: {trackTitle} ({videoId})...");
                    yield return DownloadYouTubeAudioCoroutine(ytDlpPath, ffmpegPath, source, videoId, trackTitle);

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

        private IEnumerator DownloadFFmpegCoroutine(string destinationPath)
        {
            string url = "https://github.com/ffbinaries/ffbinaries-prebuilt/releases/download/v4.4.1/ffmpeg-4.4.1-win-64.zip";
            string tempZip = Path.Combine(Paths.ConfigPath, "WorkableBoombox", "ffmpeg_temp.zip");

            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.certificateHandler = new ForceAcceptAllCertificate();
                req.downloadHandler = new DownloadHandlerFile(tempZip);
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    Plugin.Log.LogInfo("[YouTubeAudio] ffmpeg.zip успешно скачан. Распаковка...");
                    try
                    {
                        using (ZipArchive archive = ZipFile.OpenRead(tempZip))
                        {
                            foreach (ZipArchiveEntry entry in archive.Entries)
                            {
                                if (string.Equals(entry.Name, "ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    entry.ExtractToFile(destinationPath, true);
                                    Plugin.Log.LogInfo($"[YouTubeAudio] ffmpeg.exe успешно распакован в {destinationPath}!");
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogError($"[YouTubeAudio] Ошибка при распаковке ffmpeg: {ex.Message}");
                    }
                    finally
                    {
                        if (File.Exists(tempZip))
                        {
                            try { File.Delete(tempZip); } catch { }
                        }
                    }
                }
                else
                {
                    Plugin.Log.LogError($"[YouTubeAudio] Ошибка загрузки ffmpeg.zip: {req.error}");
                }
            }
        }

        private IEnumerator DownloadYouTubeAudioCoroutine(string ytDlpPath, string ffmpegPath, string url, string videoId, string trackTitle)
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

            string cleanTitle = !string.IsNullOrEmpty(trackTitle) ? trackTitle : videoId;
            if (BoomboxHUD.Instance != null)
            {
                string label = BoomboxLocalization.IsRussian ? "Загрузка" : "Downloading";
                BoomboxHUD.Instance.SetDownloadProgress(true, $"⏳ {label}: {cleanTitle}...");
            }

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string outputTemplate = Path.Combine(CacheDirectory, "%(id)s.%(ext)s");
                    string progressTemplate = "%(progress._percent_str)s|%(progress._downloaded_bytes_str)s|%(progress._total_bytes_str,progress._total_bytes_estimate_str)s";
                    string args;
                    if (File.Exists(ffmpegPath))
                    {
                        // При наличии ffmpeg конвертируем в чистый mp3, который без проблем декодируется Unity FMOD
                        args = $"-x --audio-format mp3 --audio-quality 192K --ffmpeg-location \"{ffmpegPath}\" --no-playlist --no-check-certificates --newline --progress-template \"{progressTemplate}\" -o \"{outputTemplate}\" \"{url}\"";
                    }
                    else
                    {
                        args = $"-f \"ba[ext=m4a]/ba/b\" --no-playlist --no-check-certificates --newline --progress-template \"{progressTemplate}\" -o \"{outputTemplate}\" \"{url}\"";
                    }

                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = ytDlpPath,
                        Arguments = args,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using (Process proc = new Process())
                    {
                        proc.StartInfo = psi;
                        proc.OutputDataReceived += (s, e) =>
                        {
                            if (string.IsNullOrEmpty(e.Data)) return;
                            string line = e.Data.Trim();
                            if (line.Contains("|"))
                            {
                                string[] parts = line.Split('|');
                                if (parts.Length >= 3)
                                {
                                    string pct = parts[0].Trim();
                                    string downloaded = parts[1].Trim();
                                    string total = parts[2].Trim();
                                    if (downloaded != "NA" && total != "NA")
                                    {
                                        string label = BoomboxLocalization.IsRussian ? "Загрузка" : "Downloading";
                                        string msg = $"⏳ {label}: {cleanTitle} [ {downloaded} / {total} - {pct} ]";
                                        if (BoomboxHUD.Instance != null)
                                        {
                                            BoomboxHUD.Instance.SetDownloadProgress(true, msg);
                                        }
                                    }
                                }
                            }
                        };

                        proc.Start();
                        proc.BeginOutputReadLine();
                        proc.WaitForExit(90000); // Тайм-аут 90 секунд

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
                    if (BoomboxHUD.Instance != null)
                    {
                        BoomboxHUD.Instance.SetDownloadProgress(false);
                    }
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

            if (ext == ".mp3")
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
            else if (ext == ".m4a" || ext == ".aac")
            {
                string ffmpegPath = GetFFmpegPath();
                string mp3Target = Path.ChangeExtension(filePath, ".mp3");
                if (TryConvertWithFFmpeg(ffmpegPath, filePath, mp3Target))
                {
                    filePath = mp3Target;
                    aType = AudioType.MPEG;
                }
                else
                {
                    aType = AudioType.ACC;
                }
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
