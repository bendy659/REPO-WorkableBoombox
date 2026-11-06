using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using Newtonsoft.Json;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace WorkableBoombox.Playlists
{
    public class PlaylistTrackEntry
    {
        public string Title { get; set; }
        public string Url { get; set; }

        public PlaylistTrackEntry() { }

        public PlaylistTrackEntry(string title, string url)
        {
            Title = title;
            Url = url;
        }
    }

    public class PlaylistManager : MonoBehaviourPunCallbacks
    {
        public static PlaylistManager Instance { get; private set; }

        public const string PhotonPropertyKey = "wb_pl";

        public static string MasterPlaylistsFile => Path.Combine(Paths.ConfigPath, "WorkableBoombox", "playlists.json");

        public Dictionary<string, Dictionary<string, string>> LocalPlaylists { get; private set; } =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            EnsureDirectoriesAndSampleFiles();
            LoadLocalPlaylists();
        }

        public override void OnJoinedRoom()
        {
            SyncLocalPlaylistsToRoom();
        }

        public void ReloadAndSync()
        {
            LoadLocalPlaylists();
            SyncLocalPlaylistsToRoom();
            Plugin.Log.LogInfo($"[PlaylistManager] Playlists reloaded. Local playlists count: {LocalPlaylists.Count}");
        }

        public void SyncLocalPlaylistsToRoom()
        {
            if (!PhotonNetwork.InRoom)
            {
                return;
            }

            try
            {
                string json = JsonConvert.SerializeObject(LocalPlaylists);
                var props = new ExitGames.Client.Photon.Hashtable
                {
                    { PhotonPropertyKey, json }
                };
                PhotonNetwork.LocalPlayer.SetCustomProperties(props);
                Plugin.Log.LogInfo("[PlaylistManager] Playlists synced to Photon room.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PlaylistManager] Error syncing playlists: {ex.Message}");
            }
        }

        public void EnsureDirectoriesAndSampleFiles()
        {
            try
            {
                string configBoombox = Path.Combine(Paths.ConfigPath, "WorkableBoombox");
                if (!Directory.Exists(configBoombox))
                {
                    Directory.CreateDirectory(configBoombox);
                }

                // Удаляем устаревшую вспомогательную папку Playlists если она осталась от прошлых версий
                string legacyPlaylistsDir = Path.Combine(configBoombox, "Playlists");
                if (Directory.Exists(legacyPlaylistsDir))
                {
                    try
                    {
                        Directory.Delete(legacyPlaylistsDir, true);
                        Plugin.Log.LogInfo("[PlaylistManager] Cleaned up legacy Playlists directory.");
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"[PlaylistManager] Could not delete legacy Playlists dir: {ex.Message}");
                    }
                }

                if (!File.Exists(MasterPlaylistsFile))
                {
                    var sample = new Dictionary<string, Dictionary<string, string>>
                    {
                        {
                            "Hits & Classics",
                            new Dictionary<string, string>
                            {
                                { "Rick Astley - Never Gonna Give You Up", "https://www.youtube.com/watch?v=dQw4w9WgXcQ" },
                                { "Queen - Bohemian Rhapsody", "https://www.youtube.com/watch?v=fJ9rUzIMcZQ" },
                                { "KruElla - Tamponi Rap (Local)", "local:KruElla-Tamponi-Rap.mp3" }
                            }
                        },
                        {
                            "Party Mix",
                            new Dictionary<string, string>
                            {
                                { "Smash Mouth - All Star", "https://www.youtube.com/watch?v=L_jWHffIx5E" },
                                { "Niko B - Whys this dealer (Local)", "local:Niko B - Whys this dealer.mp3" }
                            }
                        }
                    };

                    string json = JsonConvert.SerializeObject(sample, Formatting.Indented);
                    File.WriteAllText(MasterPlaylistsFile, json);
                    Plugin.Log.LogInfo("[PlaylistManager] Created default playlists file: " + MasterPlaylistsFile);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PlaylistManager] Error preparing playlist files: {ex.Message}");
            }
        }

        public void LoadLocalPlaylists()
        {
            var loaded = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            // Читаем единственный мастер-файл playlists.json
            if (File.Exists(MasterPlaylistsFile))
            {
                try
                {
                    string json = File.ReadAllText(MasterPlaylistsFile);
                    var dict = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(json);
                    if (dict != null)
                    {
                        foreach (var kvp in dict)
                        {
                            if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Value != null)
                            {
                                loaded[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[PlaylistManager] Error reading {MasterPlaylistsFile}: {ex.Message}");
                }
            }

            LocalPlaylists = loaded;
            Plugin.Log.LogInfo($"[PlaylistManager] Loaded local playlists: {LocalPlaylists.Count}");
        }

        public Dictionary<string, Dictionary<string, Dictionary<string, string>>> GetAllRoomPlaylists()
        {
            var result = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);

            // 1. Локальный игрок всегда первым
            string localName = !string.IsNullOrEmpty(PhotonNetwork.NickName) ? PhotonNetwork.NickName : "Me";
            result[localName] = LocalPlaylists;

            // 2. Другие игроки в комнате
            if (PhotonNetwork.InRoom)
            {
                foreach (Player p in PhotonNetwork.PlayerListOthers)
                {
                    string pName = !string.IsNullOrEmpty(p.NickName) ? p.NickName : ("Player " + p.ActorNumber);
                    if (p.CustomProperties != null && p.CustomProperties.TryGetValue(PhotonPropertyKey, out object val) && val is string jsonStr)
                    {
                        try
                        {
                            var pls = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(jsonStr);
                            if (pls != null && pls.Count > 0)
                            {
                                result[pName] = pls;
                                continue;
                            }
                        }
                        catch { }
                    }

                    if (!result.ContainsKey(pName))
                    {
                        result[pName] = new Dictionary<string, Dictionary<string, string>>();
                    }
                }
            }

            return result;
        }

        public List<PlaylistTrackEntry> GetTrackList(string ownerNick, string playlistName)
        {
            var list = new List<PlaylistTrackEntry>();
            var all = GetAllRoomPlaylists();

            if (all.TryGetValue(ownerNick, out var playerPlaylists))
            {
                if (playerPlaylists.TryGetValue(playlistName, out var tracks))
                {
                    foreach (var kvp in tracks)
                    {
                        list.Add(new PlaylistTrackEntry(kvp.Key, kvp.Value));
                    }
                }
            }

            return list;
        }

        public void SaveLocalPlaylists()
        {
            try
            {
                string json = JsonConvert.SerializeObject(LocalPlaylists, Formatting.Indented);
                File.WriteAllText(MasterPlaylistsFile, json);
                Plugin.Log.LogInfo($"[PlaylistManager] Локальные плейлисты успешно сохранены в {MasterPlaylistsFile}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PlaylistManager] Ошибка при сохранении плейлистов: {ex.Message}");
            }

            SyncLocalPlaylistsToRoom();
        }

        public bool AddPlaylist(string playlistName)
        {
            if (string.IsNullOrWhiteSpace(playlistName))
            {
                return false;
            }

            playlistName = playlistName.Trim();
            if (!LocalPlaylists.ContainsKey(playlistName))
            {
                LocalPlaylists[playlistName] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                SaveLocalPlaylists();
                Plugin.Log.LogInfo($"[PlaylistManager] Создан новый локальный плейлист: '{playlistName}'");
                return true;
            }

            return false;
        }

        public bool AddTrackToPlaylist(string playlistName, string trackTitle, string trackUrl)
        {
            if (string.IsNullOrWhiteSpace(playlistName) || string.IsNullOrWhiteSpace(trackTitle) || string.IsNullOrWhiteSpace(trackUrl))
            {
                return false;
            }

            playlistName = playlistName.Trim();
            trackTitle = trackTitle.Trim();
            trackUrl = trackUrl.Trim();

            if (!LocalPlaylists.TryGetValue(playlistName, out var tracks))
            {
                tracks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                LocalPlaylists[playlistName] = tracks;
            }

            tracks[trackTitle] = trackUrl;
            SaveLocalPlaylists();
            Plugin.Log.LogInfo($"[PlaylistManager] В плейлист '{playlistName}' добавлен трек: '{trackTitle}' -> {trackUrl}");
            return true;
        }

        public bool RemovePlaylist(string playlistName)
        {
            if (string.IsNullOrWhiteSpace(playlistName))
            {
                return false;
            }

            if (LocalPlaylists.Remove(playlistName.Trim()))
            {
                SaveLocalPlaylists();
                return true;
            }

            return false;
        }

        public bool RemoveTrackFromPlaylist(string playlistName, string trackTitle)
        {
            if (string.IsNullOrWhiteSpace(playlistName) || string.IsNullOrWhiteSpace(trackTitle))
            {
                return false;
            }

            if (LocalPlaylists.TryGetValue(playlistName.Trim(), out var tracks))
            {
                if (tracks.Remove(trackTitle.Trim()))
                {
                    SaveLocalPlaylists();
                    return true;
                }
            }

            return false;
        }

        public bool ImportPlaylistFromPlayer(string ownerNick, string playlistName, out string finalPlaylistName)
        {
            finalPlaylistName = null;
            if (string.IsNullOrWhiteSpace(ownerNick) || string.IsNullOrWhiteSpace(playlistName))
            {
                return false;
            }

            var all = GetAllRoomPlaylists();
            if (!all.TryGetValue(ownerNick, out var playerPlaylists) || !playerPlaylists.TryGetValue(playlistName, out var sourceTracks))
            {
                return false;
            }

            // Выбираем имя, чтобы не перезаписать существующий локальный плейлист
            string targetName = playlistName.Trim();
            if (LocalPlaylists.ContainsKey(targetName))
            {
                targetName = $"{playlistName.Trim()} ({ownerNick})";
            }

            int suffix = 1;
            while (LocalPlaylists.ContainsKey(targetName))
            {
                targetName = $"{playlistName.Trim()} ({ownerNick} {suffix++})";
            }

            var newTracks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in sourceTracks)
            {
                newTracks[kvp.Key] = kvp.Value;
            }

            LocalPlaylists[targetName] = newTracks;
            SaveLocalPlaylists();
            finalPlaylistName = targetName;
            Plugin.Log.LogInfo($"[PlaylistManager] Imported playlist '{playlistName}' from {ownerNick} as '{targetName}' ({newTracks.Count} tracks).");
            return true;
        }

        public bool ImportTrackFromPlayer(string ownerNick, string sourcePlaylistName, string trackTitle, string targetLocalPlaylistName)
        {
            if (string.IsNullOrWhiteSpace(ownerNick) || string.IsNullOrWhiteSpace(sourcePlaylistName) || string.IsNullOrWhiteSpace(trackTitle))
            {
                return false;
            }

            var all = GetAllRoomPlaylists();
            if (!all.TryGetValue(ownerNick, out var playerPlaylists) || !playerPlaylists.TryGetValue(sourcePlaylistName, out var sourceTracks))
            {
                return false;
            }

            if (!sourceTracks.TryGetValue(trackTitle, out string trackUrl))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(targetLocalPlaylistName))
            {
                targetLocalPlaylistName = "Imported Tracks";
            }

            targetLocalPlaylistName = targetLocalPlaylistName.Trim();
            if (!LocalPlaylists.TryGetValue(targetLocalPlaylistName, out var targetTracks))
            {
                targetTracks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                LocalPlaylists[targetLocalPlaylistName] = targetTracks;
            }

            targetTracks[trackTitle.Trim()] = trackUrl.Trim();
            SaveLocalPlaylists();
            Plugin.Log.LogInfo($"[PlaylistManager] Imported track '{trackTitle}' from {ownerNick} into '{targetLocalPlaylistName}'.");
            return true;
        }

        public bool ImportPlaylistFromText(string rawText, string targetPlaylistName, out string finalPlaylistName, out int importedCount)
        {
            finalPlaylistName = targetPlaylistName;
            importedCount = 0;

            if (string.IsNullOrWhiteSpace(rawText))
            {
                return false;
            }

            rawText = rawText.Trim();

            // 1. Попытка распарсить как словарь плейлистов JSON
            try
            {
                var multiPlaylists = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(rawText);
                if (multiPlaylists != null && multiPlaylists.Count > 0)
                {
                    foreach (var pl in multiPlaylists)
                    {
                        if (!string.IsNullOrWhiteSpace(pl.Key) && pl.Value != null)
                        {
                            string plName = pl.Key.Trim();
                            if (!LocalPlaylists.TryGetValue(plName, out var tracks))
                            {
                                tracks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                LocalPlaylists[plName] = tracks;
                            }
                            foreach (var tr in pl.Value)
                            {
                                tracks[tr.Key] = tr.Value;
                                importedCount++;
                            }
                            finalPlaylistName = plName;
                        }
                    }
                    if (importedCount > 0)
                    {
                        SaveLocalPlaylists();
                        return true;
                    }
                }
            }
            catch { }

            // 2. Попытка распарсить как одиночный JSON плейлист (Title -> URL)
            try
            {
                var singlePlaylist = JsonConvert.DeserializeObject<Dictionary<string, string>>(rawText);
                if (singlePlaylist != null && singlePlaylist.Count > 0)
                {
                    string plName = !string.IsNullOrWhiteSpace(targetPlaylistName) ? targetPlaylistName.Trim() : "Imported Playlist";
                    if (!LocalPlaylists.TryGetValue(plName, out var tracks))
                    {
                        tracks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        LocalPlaylists[plName] = tracks;
                    }
                    foreach (var tr in singlePlaylist)
                    {
                        tracks[tr.Key] = tr.Value;
                        importedCount++;
                    }
                    finalPlaylistName = plName;
                    SaveLocalPlaylists();
                    return true;
                }
            }
            catch { }

            // 3. Построчный парсинг простого текста: "Title - URL" или "Title = URL" или чистый URL
            string destPlaylist = !string.IsNullOrWhiteSpace(targetPlaylistName) ? targetPlaylistName.Trim() : "Imported Playlist";
            if (!LocalPlaylists.TryGetValue(destPlaylist, out var destTracks))
            {
                destTracks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                LocalPlaylists[destPlaylist] = destTracks;
            }

            string[] lines = rawText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#") || trimmed.StartsWith("//"))
                {
                    continue;
                }

                string title = null;
                string url = null;

                if (trimmed.Contains(" - "))
                {
                    int lastSep = trimmed.LastIndexOf(" - ", StringComparison.Ordinal);
                    title = trimmed.Substring(0, lastSep).Trim();
                    url = trimmed.Substring(lastSep + 3).Trim();
                }
                else if (trimmed.Contains(" = "))
                {
                    int sep = trimmed.IndexOf(" = ", StringComparison.Ordinal);
                    title = trimmed.Substring(0, sep).Trim();
                    url = trimmed.Substring(sep + 3).Trim();
                }
                else if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                         trimmed.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
                {
                    url = trimmed;
                    title = Path.GetFileNameWithoutExtension(url);
                    if (string.IsNullOrEmpty(title) || title.Length < 3)
                    {
                        title = $"Track {destTracks.Count + 1}";
                    }
                }

                if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(url))
                {
                    destTracks[title] = url;
                    importedCount++;
                }
            }

            if (importedCount > 0)
            {
                finalPlaylistName = destPlaylist;
                SaveLocalPlaylists();
                return true;
            }

            return false;
        }

        public string ExportPlaylistJson(string playlistName)
        {
            if (string.IsNullOrWhiteSpace(playlistName)) return null;

            if (LocalPlaylists.TryGetValue(playlistName.Trim(), out var tracks))
            {
                var dict = new Dictionary<string, Dictionary<string, string>>
                {
                    { playlistName.Trim(), tracks }
                };
                return JsonConvert.SerializeObject(dict, Formatting.Indented);
            }

            return null;
        }
    }
}
