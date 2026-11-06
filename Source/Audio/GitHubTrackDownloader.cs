using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using WorkableBoombox.Config;

namespace WorkableBoombox.Audio
{
    public class GitHubTrackDownloader : MonoBehaviour
    {
        public class GitHubContentItem
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("path")]
            public string Path { get; set; }

            [JsonProperty("sha")]
            public string Sha { get; set; }

            [JsonProperty("size")]
            public long Size { get; set; }

            [JsonProperty("type")]
            public string Type { get; set; }

            [JsonProperty("download_url")]
            public string DownloadUrl { get; set; }
        }

        public class SyncMetadata
        {
            public Dictionary<string, string> FileShas { get; set; } = new Dictionary<string, string>();
        }

        public static GitHubTrackDownloader Instance { get; private set; }

        public bool IsSyncing { get; private set; }

        private string MetadataFilePath => System.IO.Path.Combine(BoomboxAudioManager.TracksDirectory, ".sync_metadata.json");

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

        public void StartSync()
        {
            bool hasValidSource = ModConfig.TryParseGitHubSource(ModConfig.GitHubSource.Value, out _, out _, out _);
            bool canSync = ModConfig.GitHubSyncEnabled.Value && hasValidSource;
            if (!canSync)
            {
                Plugin.Log.LogInfo("[GitHubSync] Синхронизация отключена или GitHubSource не настроен (формат: owner/repo/token).");
                return;
            }

            if (IsSyncing)
            {
                Plugin.Log.LogWarning("[GitHubSync] Синхронизация уже выполняется.");
                return;
            }

            StartCoroutine(SyncTracksCoroutine());
        }

        public IEnumerator SyncTracksCoroutine()
        {
            IsSyncing = true;

            bool parsed = ModConfig.TryParseGitHubSource(ModConfig.GitHubSource.Value, out string owner, out string repo, out string token);
            if (!parsed)
            {
                Plugin.Log.LogWarning("[GitHubSync] Не удалось распарсить GitHubSource. Ожидается: owner/repo/token");
                IsSyncing = false;
                yield break;
            }

            Plugin.Log.LogInfo($"[GitHubSync] Запуск проверки обновлений в репозитории '{owner}/{repo}' (корень)...");

            // Запрашиваем корень репозитория
            string apiUrl = $"https://api.github.com/repos/{owner}/{repo}/contents";

            SyncMetadata metadata = LoadMetadata();

            using (UnityWebRequest req = UnityWebRequest.Get(apiUrl))
            {
                req.SetRequestHeader("User-Agent", "WorkableBoombox-Mod");
                req.SetRequestHeader("Accept", "application/vnd.github+json");

                bool hasToken = !string.IsNullOrWhiteSpace(token);
                if (hasToken)
                {
                    req.SetRequestHeader("Authorization", $"Bearer {token}");
                }

                yield return req.SendWebRequest();

                bool isError = req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError;
                if (isError)
                {
                    Plugin.Log.LogError($"[GitHubSync] Не удалось получить список файлов с GitHub: {req.responseCode} - {req.error}");
                    IsSyncing = false;
                    yield break;
                }

                string jsonText = req.downloadHandler.text;
                List<GitHubContentItem> items = null;

                try
                {
                    items = JsonConvert.DeserializeObject<List<GitHubContentItem>>(jsonText);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[GitHubSync] Ошибка парсинга ответа GitHub: {ex.Message}");
                    IsSyncing = false;
                    yield break;
                }

                if (items == null || items.Count == 0)
                {
                    Plugin.Log.LogInfo("[GitHubSync] Репозиторий пуст или не содержит файлов.");
                    IsSyncing = false;
                    yield break;
                }

                int downloadedCount = 0;
                HashSet<string> remoteFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (GitHubContentItem item in items)
                {
                    bool isFile = item.Type == "file";
                    if (!isFile)
                    {
                        continue;
                    }

                    remoteFiles.Add(item.Name);

                    string ext = System.IO.Path.GetExtension(item.Name).ToLowerInvariant();
                    bool isAudioExt = ext == ".mp3" || ext == ".ogg" || ext == ".wav" || ext == ".aiff";
                    if (!isAudioExt)
                    {
                        continue;
                    }

                    string targetPath = System.IO.Path.Combine(BoomboxAudioManager.TracksDirectory, item.Name);
                    bool fileExistsLocally = File.Exists(targetPath);
                    bool shaMatches = metadata.FileShas.TryGetValue(item.Name, out string cachedSha) && cachedSha == item.Sha;

                    if (fileExistsLocally && shaMatches)
                    {
                        // Файл уже актуален
                        continue;
                    }

                    Plugin.Log.LogInfo($"[GitHubSync] Скачиваем '{item.Name}' ({item.Size / 1024} КБ)...");

                    string downloadUrl = item.DownloadUrl;
                    if (string.IsNullOrWhiteSpace(downloadUrl))
                    {
                        downloadUrl = $"https://api.github.com/repos/{owner}/{repo}/contents/{item.Name}";
                    }

                    using (UnityWebRequest downloadReq = UnityWebRequest.Get(downloadUrl))
                    {
                        downloadReq.SetRequestHeader("User-Agent", "WorkableBoombox-Mod");
                        if (hasToken)
                        {
                            downloadReq.SetRequestHeader("Authorization", $"Bearer {token}");
                        }

                        // Если качаем через API, нужен заголовок raw
                        if (downloadUrl.Contains("api.github.com"))
                        {
                            downloadReq.SetRequestHeader("Accept", "application/vnd.github.v3.raw");
                        }

                        yield return downloadReq.SendWebRequest();

                        bool downloadFailed = downloadReq.result != UnityWebRequest.Result.Success;
                        if (downloadFailed)
                        {
                            Plugin.Log.LogError($"[GitHubSync] Ошибка скачивания '{item.Name}': {downloadReq.error}");
                            continue;
                        }

                        byte[] fileBytes = downloadReq.downloadHandler.data;
                        File.WriteAllBytes(targetPath, fileBytes);
                        metadata.FileShas[item.Name] = item.Sha;
                        downloadedCount++;
                    }
                }

                // Удаляем локальные файлы, которые были удалены или переименованы в GitHub-репозитории
                List<string> deletedLocalKeys = new List<string>();
                foreach (string cachedName in metadata.FileShas.Keys)
                {
                    if (!remoteFiles.Contains(cachedName))
                    {
                        string localPath = System.IO.Path.Combine(BoomboxAudioManager.TracksDirectory, cachedName);
                        if (File.Exists(localPath))
                        {
                            try
                            {
                                File.Delete(localPath);
                                Plugin.Log.LogInfo($"[GitHubSync] Удален файл '{cachedName}', так как он больше не существует в репозитории.");
                            }
                            catch (Exception ex)
                            {
                                Plugin.Log.LogWarning($"[GitHubSync] Ошибка удаления устаревшего файла '{cachedName}': {ex.Message}");
                            }
                        }
                        deletedLocalKeys.Add(cachedName);
                    }
                }

                foreach (string del in deletedLocalKeys)
                {
                    metadata.FileShas.Remove(del);
                }

                SaveMetadata(metadata);

                Plugin.Log.LogInfo($"[GitHubSync] Синхронизация завершена. Скачано новых/обновленных треков: {downloadedCount}");

                if (downloadedCount > 0 && BoomboxAudioManager.Instance != null)
                {
                    BoomboxAudioManager.Instance.ReloadTracks();
                }
            }

            IsSyncing = false;
        }

        private SyncMetadata LoadMetadata()
        {
            if (File.Exists(MetadataFilePath))
            {
                try
                {
                    string json = File.ReadAllText(MetadataFilePath);
                    return JsonConvert.DeserializeObject<SyncMetadata>(json) ?? new SyncMetadata();
                }
                catch
                {
                    return new SyncMetadata();
                }
            }

            return new SyncMetadata();
        }

        private void SaveMetadata(SyncMetadata meta)
        {
            try
            {
                string json = JsonConvert.SerializeObject(meta, Formatting.Indented);
                File.WriteAllText(MetadataFilePath, json);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[GitHubSync] Ошибка сохранения метаданных: {ex.Message}");
            }
        }
    }
}
