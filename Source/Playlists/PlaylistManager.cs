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

        public static string PlaylistsDirectory => Path.Combine(Paths.ConfigPath, "WorkableBoombox", "Playlists");
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
            Plugin.Log.LogInfo($"[PlaylistManager] Плейлисты перезагружены. Локальных плейлистов: {LocalPlaylists.Count}");
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
                Plugin.Log.LogInfo("[PlaylistManager] Плейлисты успешно синхронизированы в комнату Photon.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PlaylistManager] Ошибка синхронизации плейлистов в сеть: {ex.Message}");
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

                if (!Directory.Exists(PlaylistsDirectory))
                {
                    Directory.CreateDirectory(PlaylistsDirectory);
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
                    Plugin.Log.LogInfo("[PlaylistManager] Создан файл примеров плейлистов: " + MasterPlaylistsFile);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PlaylistManager] Ошибка создания директорий плейлистов: {ex.Message}");
            }
        }

        public void LoadLocalPlaylists()
        {
            var loaded = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            // 1. Читаем мастер-файл playlists.json
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
                    Plugin.Log.LogError($"[PlaylistManager] Ошибка чтения {MasterPlaylistsFile}: {ex.Message}");
                }
            }

            // 2. Читаем все индивидуальные .json файлы в папке Playlists/
            if (Directory.Exists(PlaylistsDirectory))
            {
                string[] files = Directory.GetFiles(PlaylistsDirectory, "*.json");
                foreach (string file in files)
                {
                    try
                    {
                        string playlistName = Path.GetFileNameWithoutExtension(file);
                        string json = File.ReadAllText(file);

                        // Может быть как словарь "ИмяТрека": "URL", так и полный объект
                        var tracks = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                        if (tracks != null && tracks.Count > 0)
                        {
                            loaded[playlistName] = tracks;
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogError($"[PlaylistManager] Ошибка чтения плейлиста '{Path.GetFileName(file)}': {ex.Message}");
                    }
                }
            }

            LocalPlaylists = loaded;
            Plugin.Log.LogInfo($"[PlaylistManager] Загружено локальных плейлистов: {LocalPlaylists.Count}");
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
    }
}
