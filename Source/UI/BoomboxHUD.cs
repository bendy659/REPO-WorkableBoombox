using System;
using UnityEngine;
using WorkableBoombox.Config;
using WorkableBoombox.Localization;

namespace WorkableBoombox.UI
{
    public class BoomboxHUD : MonoBehaviour
    {
        public static BoomboxHUD Instance { get; private set; }

        private bool showHoverPrompt;
        private string hoverBoomboxStatus = "";
        private string hoverLoopStatus = "";
        private string hoverShuffleStatus = "";
        private int hoverVolumePercent = 100;
        private bool hoverIsMuted;

        // Кэшированные строки подсказки для исключения аллокаций и фризов на OnGUI
        private string cachedTitle = "BOOMBOX";
        private float cachedBoxWidth = 460f;
        private string cachedPromptRow1 = "";
        private string cachedPromptRow2 = "";
        private string cachedPromptRow3 = "";
        private string cachedPromptRow4 = "";

        private string lastStatus = null;
        private string lastLoopStatus = null;
        private string lastShuffleStatus = null;
        private int lastVolumePercent = -1;
        private bool lastIsMuted;

        private bool showVolumeNotification;
        private float volumeNotificationTimer;
        private string volumeNotificationText = "";

        private bool showScrubNotification;
        private float scrubNotificationTimer;
        private string scrubNotificationText = "";

        private bool showTrackNotification;
        private float trackNotificationTimer;
        private string trackNotificationText = "";

        private bool isDownloading;
        private string downloadStatusText = "";

        private GUIStyle promptBoxStyle;
        private GUIStyle titleStyle;
        private GUIStyle hotkeyStyle;
        private GUIStyle subHotkeyStyle;
        private GUIStyle volumeFeedbackStyle;
        private GUIStyle scrubStyle;
        private GUIStyle trackNotificationStyle;
        private Texture2D backgroundTexture;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            backgroundTexture = new Texture2D(1, 1);
            backgroundTexture.SetPixel(0, 0, new Color(0.05f, 0.05f, 0.08f, 0.85f));
            backgroundTexture.Apply();
        }

        private void Update()
        {
            if (showVolumeNotification)
            {
                volumeNotificationTimer -= Time.deltaTime;
                if (volumeNotificationTimer <= 0f)
                {
                    showVolumeNotification = false;
                }
            }

            if (showScrubNotification)
            {
                scrubNotificationTimer -= Time.deltaTime;
                if (scrubNotificationTimer <= 0f)
                {
                    showScrubNotification = false;
                }
            }

            if (showTrackNotification)
            {
                trackNotificationTimer -= Time.deltaTime;
                if (trackNotificationTimer <= 0f)
                {
                    showTrackNotification = false;
                }
            }
        }

        public void SetHoverPrompt(bool visible, string status = "", string loopStatus = "", string shuffleStatus = "", int volumePercent = 100, bool isMuted = false)
        {
            showHoverPrompt = visible;
            if (!visible)
            {
                return;
            }

            // Обновляем форматирование строк только при изменении состояния (исключаем аллокации в OnGUI)
            if (status != lastStatus || loopStatus != lastLoopStatus || shuffleStatus != lastShuffleStatus || volumePercent != lastVolumePercent || isMuted != lastIsMuted)
            {
                lastStatus = status;
                lastLoopStatus = loopStatus;
                lastShuffleStatus = shuffleStatus;
                lastVolumePercent = volumePercent;
                lastIsMuted = isMuted;

                hoverBoomboxStatus = status;
                hoverLoopStatus = loopStatus;
                hoverShuffleStatus = shuffleStatus;
                hoverVolumePercent = volumePercent;
                hoverIsMuted = isMuted;

                cachedTitle = string.IsNullOrEmpty(status)
                    ? BoomboxLocalization.BoomboxTitle
                    : $"{BoomboxLocalization.BoomboxTitle} ({status})";

                string eKey = ModConfig.ToggleKey != null ? ModConfig.ToggleKey.Value.ToString() : "E";
                string fKey = ModConfig.NextTrackKey != null ? ModConfig.NextTrackKey.Value.ToString() : "F";
                string rKey = ModConfig.LoopModeKey != null ? ModConfig.LoopModeKey.Value.ToString() : "R";
                string tKey = ModConfig.ShuffleKey != null ? ModConfig.ShuffleKey.Value.ToString() : "T";
                string vKey = ModConfig.VolumeKey != null ? ModConfig.VolumeKey.Value.ToString() : "V";
                string pKey = ModConfig.PlaylistKey != null ? ModConfig.PlaylistKey.Value.ToString() : "P";
                string zKey = ModConfig.RestartTrackKey != null ? ModConfig.RestartTrackKey.Value.ToString() : "Z";

                cachedPromptRow1 = BoomboxLocalization.PromptRow1(eKey, fKey, pKey);
                cachedPromptRow2 = BoomboxLocalization.PromptRow2(rKey, loopStatus, tKey, shuffleStatus);
                cachedPromptRow3 = BoomboxLocalization.PromptRow3(vKey, volumePercent, isMuted);
                cachedPromptRow4 = BoomboxLocalization.PromptRow4(fKey, zKey);

                if (titleStyle != null)
                {
                    float titleWidth = titleStyle.CalcSize(new GUIContent(cachedTitle)).x;
                    cachedBoxWidth = Mathf.Max(460f, titleWidth + 30f);
                }
                else
                {
                    cachedBoxWidth = 460f;
                }
            }
        }

        public void ShowVolumeFeedback(int percent, bool muted)
        {
            showVolumeNotification = true;
            volumeNotificationTimer = 2.0f;
            volumeNotificationText = BoomboxLocalization.NotificationVolume(percent, muted);
        }

        public void ShowScrubFeedback(float currentTime, float totalTime, float delta)
        {
            showScrubNotification = true;
            scrubNotificationTimer = 2.0f;

            string sign = delta >= 0 ? "+" : "";
            string currentFormatted = FormatTime(currentTime);
            string totalFormatted = FormatTime(totalTime);
            scrubNotificationText = $"[ {sign}{delta:F0}s ]  {currentFormatted} / {totalFormatted}";
        }

        public void ShowTrackNotification(string title)
        {
            showTrackNotification = true;
            trackNotificationTimer = 3.0f;
            trackNotificationText = title;
        }

        public void SetDownloadProgress(bool downloading, string statusText = "")
        {
            isDownloading = downloading;
            downloadStatusText = statusText;
        }

        private void OnGUI()
        {
            InitStyles();

            // 1. Подсказка взаимодействия (рисуется чистыми GUI.Label без тяжелого GUILayout-паса)
            if (showHoverPrompt)
            {
                float boxWidth = cachedBoxWidth;
                float boxHeight = 138f;

                float alignX = ModConfig.OverlayPositionX != null ? Mathf.Clamp01(ModConfig.OverlayPositionX.Value) : 0.5f;
                float alignY = ModConfig.OverlayPositionY != null ? Mathf.Clamp01(ModConfig.OverlayPositionY.Value) : 0.6f;

                float margin = 20f;
                float minX = margin;
                float maxX = Mathf.Max(minX, Screen.width - boxWidth - margin);
                float boxX = Mathf.Lerp(minX, maxX, alignX);

                float minY = margin;
                float maxY = Mathf.Max(minY, Screen.height - boxHeight - margin);
                float boxY = Mathf.Lerp(minY, maxY, alignY);

                Rect boxRect = new Rect(boxX, boxY, boxWidth, boxHeight);
                GUI.Box(boxRect, GUIContent.none, promptBoxStyle);

                float innerX = boxX + 10f;
                float innerW = boxWidth - 20f;

                GUI.Label(new Rect(innerX, boxY + 8f, innerW, 22f), cachedTitle, titleStyle);
                GUI.Label(new Rect(innerX, boxY + 34f, innerW, 20f), cachedPromptRow1, hotkeyStyle);
                GUI.Label(new Rect(innerX, boxY + 58f, innerW, 20f), cachedPromptRow2, subHotkeyStyle);
                GUI.Label(new Rect(innerX, boxY + 82f, innerW, 20f), cachedPromptRow3, subHotkeyStyle);
                GUI.Label(new Rect(innerX, boxY + 106f, innerW, 20f), cachedPromptRow4, hotkeyStyle);
            }

            // 2. Уведомление о перемотке (по центру экрана, над подсказкой взаимодействия)
            if (showScrubNotification)
            {
                float scrubWidth = 340f;
                float scrubHeight = 52f;
                float scrubX = (Screen.width - scrubWidth) * 0.5f;
                float scrubY = Screen.height * 0.48f;

                Rect scrubRect = new Rect(scrubX, scrubY, scrubWidth, scrubHeight);
                GUI.Box(scrubRect, GUIContent.none, promptBoxStyle);
                GUI.Label(scrubRect, scrubNotificationText, scrubStyle);
            }

            // 3. Уведомление о громкости (по центру снизу)
            if (showVolumeNotification)
            {
                float volWidth = 280f;
                float volHeight = 45f;
                float volX = (Screen.width - volWidth) * 0.5f;
                float volY = Screen.height * 0.76f;

                Rect volRect = new Rect(volX, volY, volWidth, volHeight);
                GUI.Box(volRect, GUIContent.none, promptBoxStyle);
                GUI.Label(volRect, volumeNotificationText, volumeFeedbackStyle);
            }

            // 4. Уведомление о загрузке трека или переключении трека/режима (сверху экрана)
            if (isDownloading || showTrackNotification)
            {
                string displayText = isDownloading ? downloadStatusText : trackNotificationText;
                float trackWidth = 560f;
                float trackHeight = 45f;
                float trackX = (Screen.width - trackWidth) * 0.5f;
                float trackY = 60f;

                Rect trackRect = new Rect(trackX, trackY, trackWidth, trackHeight);
                GUI.Box(trackRect, GUIContent.none, promptBoxStyle);
                GUI.Label(trackRect, displayText, trackNotificationStyle);
            }
        }

        private void InitStyles()
        {
            if (promptBoxStyle != null)
            {
                return;
            }

            promptBoxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = backgroundTexture },
                border = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0)
            };

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.2f, 0.9f, 1.0f) }
            };

            hotkeyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }
            };

            subHotkeyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.95f, 0.8f, 0.3f) }
            };

            volumeFeedbackStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.4f, 0.9f, 1.0f) }
            };

            scrubStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1.0f, 0.85f, 0.2f) }
            };

            trackNotificationStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.3f, 1.0f, 0.6f) }
            };

            // Прогрев растрирования шрифтов offscreen для устранения фриза при первом наведении
            GUI.Label(new Rect(-2000f, -2000f, 10f, 10f), "BOOMBOX Playing Off (Loading...) 1234567890%", titleStyle);
            GUI.Label(new Rect(-2000f, -2000f, 10f, 10f), "[E] Play/Pause [F] Next Track [P] Playlists [Z] Restart", hotkeyStyle);
            GUI.Label(new Rect(-2000f, -2000f, 10f, 10f), "[R] Loop: Playlist [T] Shuffle: On [V] Mute Scroll Volume (-/+ 5s)", subHotkeyStyle);
        }

        private static string FormatTime(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int mins = (int)(seconds / 60);
            int secs = (int)(seconds % 60);
            return $"{mins:D2}:{secs:D2}";
        }
    }
}
