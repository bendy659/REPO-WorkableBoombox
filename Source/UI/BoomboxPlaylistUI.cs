using System;
using System.Collections.Generic;
using UnityEngine;
using WorkableBoombox.Config;
using WorkableBoombox.Items;
using WorkableBoombox.Localization;
using WorkableBoombox.Playlists;

namespace WorkableBoombox.UI
{
    public class BoomboxPlaylistUI : MonoBehaviour
    {
        public static BoomboxPlaylistUI Instance { get; private set; }

        public bool IsOpen { get; private set; }
        public CustomBoomboxItem TargetBoombox { get; private set; }

        private string selectedPlayerNick;
        private string selectedPlaylistName;
        private int selectedTrackIndex = 0;

        private bool isAddingPlaylist = false;
        private string newPlaylistInput = "";

        private bool isImportingPlaylist = false;
        private string importPlaylistNameInput = "Imported Playlist";
        private string importTextInput = "";

        private bool isAddingTrack = false;
        private string newTrackTitleInput = "";
        private string newTrackUrlInput = "";

        private Vector2 scrollPlayers;
        private Vector2 scrollPlaylists;
        private Vector2 scrollTracks;

        private Texture2D windowBgTexture;
        private Texture2D selectedBgTexture;
        private Texture2D buttonBgTexture;
        private Texture2D headerBgTexture;
        private Texture2D deleteBgTexture;

        private GUIStyle windowStyle;
        private GUIStyle headerTitleStyle;
        private GUIStyle colHeaderStyle;
        private GUIStyle itemLabelStyle;
        private GUIStyle itemSelectedStyle;
        private GUIStyle playButtonStyle;
        private GUIStyle secondaryButtonStyle;
        private GUIStyle inputFieldStyle;
        private GUIStyle actionButtonStyle;
        private GUIStyle cancelButtonStyle;
        private GUIStyle deleteButtonStyle;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            InitTextures();
        }

        private void InitTextures()
        {
            windowBgTexture = MakeColorTexture(new Color(0.06f, 0.07f, 0.10f, 0.95f));
            selectedBgTexture = MakeColorTexture(new Color(0.12f, 0.35f, 0.50f, 0.85f));
            buttonBgTexture = MakeColorTexture(new Color(0.15f, 0.45f, 0.30f, 0.90f));
            headerBgTexture = MakeColorTexture(new Color(0.10f, 0.12f, 0.18f, 0.95f));
            deleteBgTexture = MakeColorTexture(new Color(0.55f, 0.15f, 0.15f, 0.90f));
        }

        private Texture2D MakeColorTexture(Color col)
        {
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, col);
            tex.Apply();
            return tex;
        }

        public void OpenMenu(CustomBoomboxItem boombox)
        {
            TargetBoombox = boombox;
            IsOpen = true;

            if (InputManager.instance != null)
            {
                InputManager.instance.SwitchToUIInputs();
            }
            SemiFunc.CursorUnlock(0.5f);
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            if (MenuCursor.instance != null)
            {
                MenuCursor.instance.OverrideHide(0.5f);
            }

            isAddingPlaylist = false;
            newPlaylistInput = "";
            isImportingPlaylist = false;
            importTextInput = "";
            isAddingTrack = false;
            newTrackTitleInput = "";
            newTrackUrlInput = "";

            // Выбираем по умолчанию локального игрока
            if (PlaylistManager.Instance != null)
            {
                var roomPls = PlaylistManager.Instance.GetAllRoomPlaylists();
                foreach (string player in roomPls.Keys)
                {
                    selectedPlayerNick = player;
                    break;
                }

                if (!string.IsNullOrEmpty(selectedPlayerNick) && roomPls.TryGetValue(selectedPlayerNick, out var pls))
                {
                    foreach (string plName in pls.Keys)
                    {
                        selectedPlaylistName = plName;
                        break;
                    }
                }
            }
        }

        public void CloseMenu()
        {
            IsOpen = false;
            TargetBoombox = null;

            isAddingPlaylist = false;
            isImportingPlaylist = false;
            importTextInput = "";
            isAddingTrack = false;

            if (InputManager.instance != null)
            {
                InputManager.instance.SwitchToPlayerInputs();
            }
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
            if (MenuCursor.instance != null)
            {
                MenuCursor.instance.OverrideHideReset();
            }
        }

        private void OnDisable()
        {
            if (IsOpen)
            {
                CloseMenu();
            }
        }

        public void ToggleMenu(CustomBoomboxItem boombox)
        {
            if (IsOpen)
            {
                CloseMenu();
            }
            else
            {
                OpenMenu(boombox);
            }
        }

        private void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            // Блокируем инпуты перемещения и прицеливания во время открытого меню
            SemiFunc.InputDisableAiming();
            SemiFunc.InputDisableMovement();
            SemiFunc.CursorUnlock(0.1f);
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            if (MenuCursor.instance != null)
            {
                MenuCursor.instance.OverrideHide(0.1f);
            }

            if (ChatManager.instance != null && ChatManager.instance.StateIsActive())
            {
                ChatManager.instance.ForceConfirmChat();
            }

            // Закрытие по Escape или повторной клавише плейлиста
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(ModConfig.PlaylistKey.Value))
            {
                CloseMenu();
                return;
            }

            // F5 - быстрая перезагрузка
            if (Input.GetKeyDown(KeyCode.F5))
            {
                if (PlaylistManager.Instance != null)
                {
                    PlaylistManager.Instance.ReloadAndSync();
                }
            }

            // Если бумбокс пропал или стал недоступен
            if (TargetBoombox == null || !TargetBoombox.gameObject.activeInHierarchy)
            {
                CloseMenu();
            }
        }

        private void InitStyles()
        {
            if (windowStyle != null) return;

            windowStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = windowBgTexture }
            };

            headerTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.3f, 0.9f, 1f) }
            };

            colHeaderStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = headerBgTexture, textColor = new Color(0.95f, 0.8f, 0.2f) },
                fontStyle = FontStyle.Bold,
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter
            };

            itemLabelStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 11,
                normal = { textColor = Color.white }
            };

            itemSelectedStyle = new GUIStyle(GUI.skin.button)
            {
                normal = { background = selectedBgTexture, textColor = new Color(0.3f, 1f, 0.7f) },
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                fontSize = 11
            };

            playButtonStyle = new GUIStyle(GUI.skin.button)
            {
                normal = { background = buttonBgTexture, textColor = Color.white },
                fontStyle = FontStyle.Bold,
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter
            };

            secondaryButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            inputFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 11,
                normal = { textColor = Color.white }
            };

            actionButtonStyle = new GUIStyle(GUI.skin.button)
            {
                normal = { background = buttonBgTexture, textColor = Color.white },
                fontStyle = FontStyle.Bold,
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter
            };

            cancelButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            deleteButtonStyle = new GUIStyle(GUI.skin.button)
            {
                normal = { background = deleteBgTexture, textColor = new Color(1f, 0.45f, 0.45f) },
                hover = { textColor = Color.white },
                fontStyle = FontStyle.Bold,
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter
            };
        }

        private void OnGUI()
        {
            if (!IsOpen || PlaylistManager.Instance == null)
            {
                return;
            }

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            InitStyles();

            float totalWidth = 840f;
            float totalHeight = 500f;
            float posX = (Screen.width - totalWidth) * 0.5f;
            float posY = (Screen.height - totalHeight) * 0.5f;

            Rect windowRect = new Rect(posX, posY, totalWidth, totalHeight);
            GUI.Box(windowRect, GUIContent.none, windowStyle);

            // 1. Верхняя панель заголовка
            Rect headerRect = new Rect(posX + 10f, posY + 10f, totalWidth - 20f, 32f);
            string titleText = "🎵 BOOMBOX PLAYLISTS";
            GUI.Label(headerRect, titleText, headerTitleStyle);

            // Кнопка закрытия [X]
            if (GUI.Button(new Rect(posX + totalWidth - 36f, posY + 10f, 26f, 26f), "✕", secondaryButtonStyle))
            {
                CloseMenu();
                return;
            }

            // 2. Три колонки таблицы
            float contentY = posY + 48f;
            float contentHeight = totalHeight - 110f;

            float col1Width = 190f; // Игроки
            float col2Width = 240f; // Плейлисты
            float col3Width = totalWidth - col1Width - col2Width - 40f; // Треки (~370px)

            var roomPlaylists = PlaylistManager.Instance.GetAllRoomPlaylists();

            // Автовыбор если выбранный игрок отсутствует
            if (string.IsNullOrEmpty(selectedPlayerNick) || !roomPlaylists.ContainsKey(selectedPlayerNick))
            {
                foreach (string first in roomPlaylists.Keys)
                {
                    selectedPlayerNick = first;
                    break;
                }
            }

            Dictionary<string, Dictionary<string, string>> currentPlayerPlaylists = null;
            if (!string.IsNullOrEmpty(selectedPlayerNick))
            {
                roomPlaylists.TryGetValue(selectedPlayerNick, out currentPlayerPlaylists);
            }

            // Автовыбор если выбранный плейлист отсутствует
            if (currentPlayerPlaylists != null && (string.IsNullOrEmpty(selectedPlaylistName) || !currentPlayerPlaylists.ContainsKey(selectedPlaylistName)))
            {
                foreach (string firstPl in currentPlayerPlaylists.Keys)
                {
                    selectedPlaylistName = firstPl;
                    break;
                }
            }

            Dictionary<string, string> currentTracks = null;
            if (currentPlayerPlaylists != null && !string.IsNullOrEmpty(selectedPlaylistName))
            {
                currentPlayerPlaylists.TryGetValue(selectedPlaylistName, out currentTracks);
            }

            // ================= КОЛОНКА 1: ИГРОКИ =================
            float x1 = posX + 15f;
            GUI.Box(new Rect(x1, contentY, col1Width, 26f), "PLAYERS", colHeaderStyle);
            Rect view1Rect = new Rect(x1, contentY + 28f, col1Width, contentHeight - 28f);
            float inner1Height = Mathf.Max(view1Rect.height, roomPlaylists.Count * 32f);
            Rect scroll1Rect = new Rect(0, 0, col1Width - 18f, inner1Height);

            scrollPlayers = GUI.BeginScrollView(view1Rect, scrollPlayers, scroll1Rect);
            float itemY = 0f;
            foreach (var kvp in roomPlaylists)
            {
                string pNick = kvp.Key;
                int plCount = kvp.Value != null ? kvp.Value.Count : 0;
                bool isSelected = string.Equals(selectedPlayerNick, pNick, StringComparison.OrdinalIgnoreCase);

                string label = $"{pNick} ({plCount})";
                if (GUI.Button(new Rect(0, itemY, col1Width - 20f, 28f), label, isSelected ? itemSelectedStyle : itemLabelStyle))
                {
                    selectedPlayerNick = pNick;
                    selectedPlaylistName = null;
                    selectedTrackIndex = 0;
                }
                itemY += 30f;
            }
            GUI.EndScrollView();

            // Определение, выбран ли локальный игрок (только локальный игрок может редактировать свои плейлисты)
            string localPlayerNick = !string.IsNullOrEmpty(Photon.Pun.PhotonNetwork.NickName) ? Photon.Pun.PhotonNetwork.NickName : "Me";
            bool isLocalPlayerSelected = string.IsNullOrEmpty(selectedPlayerNick) ||
                                         string.Equals(selectedPlayerNick, localPlayerNick, StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(selectedPlayerNick, "Me", StringComparison.OrdinalIgnoreCase);

            // ================= КОЛОНКА 2: ПЛЕЙЛИСТЫ =================
            float x2 = x1 + col1Width + 10f;
            GUI.Box(new Rect(x2, contentY, col2Width, 26f), "PLAYLISTS", colHeaderStyle);
            Rect view2Rect = new Rect(x2, contentY + 28f, col2Width, contentHeight - 28f);

            int plTotal = currentPlayerPlaylists != null ? currentPlayerPlaylists.Count : 0;
            float inner2Height = Mathf.Max(view2Rect.height, (plTotal + 1) * 32f + (isAddingPlaylist ? 70f : (isImportingPlaylist ? 174f : 34f)));
            Rect scroll2Rect = new Rect(0, 0, col2Width - 18f, inner2Height);

            scrollPlaylists = GUI.BeginScrollView(view2Rect, scrollPlaylists, scroll2Rect);
            itemY = 0f;
            if (currentPlayerPlaylists != null)
            {
                var plSnapshot = new List<KeyValuePair<string, Dictionary<string, string>>>(currentPlayerPlaylists);
                foreach (var kvp in plSnapshot)
                {
                    string plName = kvp.Key;
                    int trackCount = kvp.Value != null ? kvp.Value.Count : 0;
                    bool isSelected = string.Equals(selectedPlaylistName, plName, StringComparison.OrdinalIgnoreCase);

                    string label = $"{plName} [{trackCount}]";

                    if (isLocalPlayerSelected)
                    {
                        float labelW = col2Width - 78f;
                        if (GUI.Button(new Rect(0, itemY, labelW, 28f), label, isSelected ? itemSelectedStyle : itemLabelStyle))
                        {
                            selectedPlaylistName = plName;
                            selectedTrackIndex = 0;
                        }

                        // Кнопка экспорта плейлиста в буфер обмена
                        if (GUI.Button(new Rect(col2Width - 74f, itemY, 24f, 28f), "📋", secondaryButtonStyle))
                        {
                            string json = PlaylistManager.Instance.ExportPlaylistJson(plName);
                            if (!string.IsNullOrEmpty(json))
                            {
                                GUIUtility.systemCopyBuffer = json;
                                BoomboxHUD.Instance?.ShowTrackNotification($"📋 Copied '{plName}' JSON to clipboard!");
                            }
                        }

                        // Кнопка удаления
                        if (GUI.Button(new Rect(col2Width - 48f, itemY, 26f, 28f), "✕", deleteButtonStyle))
                        {
                            PlaylistManager.Instance.RemovePlaylist(plName);
                            if (string.Equals(selectedPlaylistName, plName, StringComparison.OrdinalIgnoreCase))
                            {
                                selectedPlaylistName = null;
                            }
                            break;
                        }
                    }
                    else
                    {
                        float labelW = col2Width - 52f;
                        if (GUI.Button(new Rect(0, itemY, labelW, 28f), label, isSelected ? itemSelectedStyle : itemLabelStyle))
                        {
                            selectedPlaylistName = plName;
                            selectedTrackIndex = 0;
                        }

                        // Кнопка импорта чужого плейлиста в локальные
                        if (GUI.Button(new Rect(col2Width - 48f, itemY, 26f, 28f), "📥", secondaryButtonStyle))
                        {
                            if (PlaylistManager.Instance.ImportPlaylistFromPlayer(selectedPlayerNick, plName, out string finalName))
                            {
                                BoomboxHUD.Instance?.ShowTrackNotification($"📥 Imported '{finalName}' from {selectedPlayerNick}!");
                            }
                        }
                    }

                    itemY += 30f;
                }
            }

            // Кнопки добавления и импорта плейлиста
            if (isLocalPlayerSelected)
            {
                if (!isAddingPlaylist && !isImportingPlaylist)
                {
                    float halfBtnW = (col2Width - 24f) * 0.5f;
                    if (GUI.Button(new Rect(0, itemY, halfBtnW, 28f), "+ New", secondaryButtonStyle))
                    {
                        isAddingPlaylist = true;
                        isImportingPlaylist = false;
                        newPlaylistInput = "";
                    }
                    if (GUI.Button(new Rect(halfBtnW + 4f, itemY, halfBtnW, 28f), "📥 Import", secondaryButtonStyle))
                    {
                        isImportingPlaylist = true;
                        isAddingPlaylist = false;
                        importPlaylistNameInput = "Imported Playlist";
                        importTextInput = "";
                    }
                    itemY += 32f;
                }
                else if (isAddingPlaylist)
                {
                    GUI.Box(new Rect(0, itemY, col2Width - 20f, 64f), GUIContent.none, windowStyle);
                    GUI.Label(new Rect(4f, itemY + 2f, col2Width - 28f, 16f), "Playlist Name:", itemLabelStyle);
                    newPlaylistInput = GUI.TextField(new Rect(4f, itemY + 18f, col2Width - 28f, 20f), newPlaylistInput, inputFieldStyle);

                    float halfW = (col2Width - 32f) * 0.5f;
                    if (GUI.Button(new Rect(4f, itemY + 40f, halfW, 20f), "✓ Create", actionButtonStyle))
                    {
                        if (!string.IsNullOrWhiteSpace(newPlaylistInput))
                        {
                            PlaylistManager.Instance.AddPlaylist(newPlaylistInput.Trim());
                            selectedPlaylistName = newPlaylistInput.Trim();
                            isAddingPlaylist = false;
                            newPlaylistInput = "";
                        }
                    }
                    if (GUI.Button(new Rect(4f + halfW + 4f, itemY + 40f, halfW, 20f), "✕ Cancel", cancelButtonStyle))
                    {
                        isAddingPlaylist = false;
                        newPlaylistInput = "";
                    }
                    itemY += 68f;
                }
                else if (isImportingPlaylist)
                {
                    GUI.Box(new Rect(0, itemY, col2Width - 20f, 164f), GUIContent.none, windowStyle);
                    GUI.Label(new Rect(4f, itemY + 2f, col2Width - 28f, 16f), "Target Playlist:", itemLabelStyle);
                    importPlaylistNameInput = GUI.TextField(new Rect(4f, itemY + 18f, col2Width - 28f, 20f), importPlaylistNameInput, inputFieldStyle);

                    GUI.Label(new Rect(4f, itemY + 40f, col2Width - 28f, 16f), "Paste JSON or Text / URLs:", itemLabelStyle);
                    importTextInput = GUI.TextArea(new Rect(4f, itemY + 56f, col2Width - 28f, 48f), importTextInput, inputFieldStyle);

                    if (GUI.Button(new Rect(4f, itemY + 108f, col2Width - 28f, 22f), "📋 Paste from Clipboard", secondaryButtonStyle))
                    {
                        if (!string.IsNullOrEmpty(GUIUtility.systemCopyBuffer))
                        {
                            importTextInput = GUIUtility.systemCopyBuffer;
                        }
                    }

                    float halfW = (col2Width - 32f) * 0.5f;
                    if (GUI.Button(new Rect(4f, itemY + 134f, halfW, 22f), "✓ Import", actionButtonStyle))
                    {
                        if (PlaylistManager.Instance.ImportPlaylistFromText(importTextInput, importPlaylistNameInput, out string finalPl, out int count))
                        {
                            selectedPlaylistName = finalPl;
                            isImportingPlaylist = false;
                            importTextInput = "";
                            BoomboxHUD.Instance?.ShowTrackNotification($"📥 Imported {count} tracks into '{finalPl}'!");
                        }
                    }
                    if (GUI.Button(new Rect(4f + halfW + 4f, itemY + 134f, halfW, 22f), "✕ Cancel", cancelButtonStyle))
                    {
                        isImportingPlaylist = false;
                        importTextInput = "";
                    }
                    itemY += 170f;
                }
            }
            GUI.EndScrollView();

            // ================= КОЛОНКА 3: ТРЕКИ =================
            float x3 = x2 + col2Width + 10f;
            GUI.Box(new Rect(x3, contentY, col3Width, 26f), "TRACKS", colHeaderStyle);
            Rect view3Rect = new Rect(x3, contentY + 28f, col3Width, contentHeight - 28f);

            int trackTotal = currentTracks != null ? currentTracks.Count : 0;
            float inner3Height = Mathf.Max(view3Rect.height, (trackTotal + 1) * 30f + (isAddingTrack ? 116f : 0f));
            Rect scroll3Rect = new Rect(0, 0, col3Width - 18f, inner3Height);

            scrollTracks = GUI.BeginScrollView(view3Rect, scrollTracks, scroll3Rect);
            itemY = 0f;
            if (currentTracks != null)
            {
                int idx = 0;
                var trackSnapshot = new List<KeyValuePair<string, string>>(currentTracks);
                foreach (var trackKvp in trackSnapshot)
                {
                    string tName = trackKvp.Key;
                    string tUrl = trackKvp.Value;
                    bool isSelected = (idx == selectedTrackIndex);

                    string prefix = tUrl.StartsWith("local:", StringComparison.OrdinalIgnoreCase) ? "📁 " : "▶ ";
                    string label = $"{idx + 1}. {prefix}{tName}";

                    float btnWidth = col3Width - 52f;

                    if (GUI.Button(new Rect(0, itemY, btnWidth, 26f), label, isSelected ? itemSelectedStyle : itemLabelStyle))
                    {
                        selectedTrackIndex = idx;
                        // Клик по треку = сразу начать воспроизведение с выбранного трека!
                        StartPlayback(idx);
                    }

                    if (isLocalPlayerSelected)
                    {
                        if (GUI.Button(new Rect(col3Width - 48f, itemY, 26f, 26f), "✕", deleteButtonStyle))
                        {
                            PlaylistManager.Instance.RemoveTrackFromPlaylist(selectedPlaylistName, tName);
                            break;
                        }
                    }
                    else
                    {
                        // Кнопка импорта чужого трека в локальный плейлист
                        if (GUI.Button(new Rect(col3Width - 48f, itemY, 26f, 26f), "📥", secondaryButtonStyle))
                        {
                            string targetPl = !string.IsNullOrEmpty(selectedPlaylistName) ? selectedPlaylistName : "Imported Tracks";
                            if (PlaylistManager.Instance.ImportTrackFromPlayer(selectedPlayerNick, selectedPlaylistName, tName, targetPl))
                            {
                                BoomboxHUD.Instance?.ShowTrackNotification($"📥 Added '{tName}' to '{targetPl}'!");
                            }
                        }
                    }

                    itemY += 28f;
                    idx++;
                }
            }
            else
            {
                GUI.Label(new Rect(10f, 10f, col3Width - 40f, 30f), "No tracks in playlist", itemLabelStyle);
            }

            // Кнопка добавления трека [+]
            if (isLocalPlayerSelected && !string.IsNullOrEmpty(selectedPlaylistName))
            {
                if (!isAddingTrack)
                {
                    if (GUI.Button(new Rect(0, itemY, col3Width - 20f, 26f), "+", secondaryButtonStyle))
                    {
                        isAddingTrack = true;
                        newTrackTitleInput = "";
                        newTrackUrlInput = "";
                    }
                    itemY += 30f;
                }
                else
                {
                    GUI.Box(new Rect(0, itemY, col3Width - 20f, 118f), GUIContent.none, windowStyle);
                    GUI.Label(new Rect(6f, itemY + 2f, col3Width - 32f, 16f), "Track Title:", itemLabelStyle);
                    newTrackTitleInput = GUI.TextField(new Rect(6f, itemY + 18f, col3Width - 32f, 20f), newTrackTitleInput, inputFieldStyle);

                    GUI.Label(new Rect(6f, itemY + 40f, col3Width - 32f, 16f), "URL (YouTube / local:track.mp3):", itemLabelStyle);
                    newTrackUrlInput = GUI.TextField(new Rect(6f, itemY + 56f, col3Width - 106f, 20f), newTrackUrlInput, inputFieldStyle);
                    if (GUI.Button(new Rect(col3Width - 96f, itemY + 56f, 70f, 20f), "📋 Paste", secondaryButtonStyle))
                    {
                        if (!string.IsNullOrEmpty(GUIUtility.systemCopyBuffer))
                        {
                            newTrackUrlInput = GUIUtility.systemCopyBuffer.Trim();
                            if (string.IsNullOrWhiteSpace(newTrackTitleInput))
                            {
                                newTrackTitleInput = System.IO.Path.GetFileNameWithoutExtension(newTrackUrlInput);
                            }
                        }
                    }

                    float halfTrackW = (col3Width - 44f) * 0.5f;
                    if (GUI.Button(new Rect(6f, itemY + 82f, halfTrackW, 24f), "✓ Add", actionButtonStyle))
                    {
                        if (!string.IsNullOrWhiteSpace(newTrackTitleInput) && !string.IsNullOrWhiteSpace(newTrackUrlInput))
                        {
                            PlaylistManager.Instance.AddTrackToPlaylist(selectedPlaylistName, newTrackTitleInput.Trim(), newTrackUrlInput.Trim());
                            isAddingTrack = false;
                            newTrackTitleInput = "";
                            newTrackUrlInput = "";
                        }
                    }
                    if (GUI.Button(new Rect(6f + halfTrackW + 8f, itemY + 82f, halfTrackW, 24f), "✕ Cancel", cancelButtonStyle))
                    {
                        isAddingTrack = false;
                        newTrackTitleInput = "";
                        newTrackUrlInput = "";
                    }
                    itemY += 122f;
                }
            }
            GUI.EndScrollView();

            // ================= НИЖНЯЯ ПАНЕЛЬ УПРАВЛЕНИЯ =================
            float bottomY = posY + totalHeight - 50f;
            float btnHeight = 36f;

            // Кнопка: Запустить весь плейлист
            Rect startPlRect = new Rect(x1, bottomY, 280f, btnHeight);
            string startBtnText = "▶ Play Entire Playlist";
            bool hasValidSelection = !string.IsNullOrEmpty(selectedPlayerNick) && !string.IsNullOrEmpty(selectedPlaylistName) && trackTotal > 0;

            if (GUI.Button(startPlRect, startBtnText, playButtonStyle))
            {
                if (hasValidSelection)
                {
                    StartPlayback(selectedTrackIndex >= 0 ? selectedTrackIndex : 0);
                }
            }

            // Кнопка: Перезагрузить плейлисты
            Rect reloadRect = new Rect(x1 + 290f, bottomY, 220f, btnHeight);
            string reloadText = "🔄 Reload Playlists (F5)";
            if (GUI.Button(reloadRect, reloadText, secondaryButtonStyle))
            {
                PlaylistManager.Instance.ReloadAndSync();
            }

            // Подсказка снизу справа
            Rect hintRect = new Rect(posX + totalWidth - 300f, bottomY + 6f, 285f, 26f);
            string hintText = "[Click track] = play from here";
            GUI.Label(hintRect, hintText, colHeaderStyle);
        }

        private void StartPlayback(int startingIndex)
        {
            if (TargetBoombox == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(selectedPlayerNick) || string.IsNullOrEmpty(selectedPlaylistName))
            {
                return;
            }

            TargetBoombox.PlayPlaylist(selectedPlayerNick, selectedPlaylistName, startingIndex);
            CloseMenu();
        }
    }
}
