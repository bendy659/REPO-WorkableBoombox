# Workable Boombox for R.E.P.O.

[![GitHub](https://img.shields.io/badge/GitHub-REPO--WorkableBoombox-181717?logo=github)](https://github.com/bendy659/REPO-WorkableBoombox)
[![Thunderstore](https://img.shields.io/badge/Thunderstore-WorkableBoombox-blue)](https://thunderstore.io/c/repo/)
[![Game](https://img.shields.io/badge/Game-R.E.P.O.-orange)](https://store.steampowered.com/app/3241660/REPO/)

A fully featured, networked portable boombox mod for **R.E.P.O.** with custom music support, track management, volume controls, audio scrubbing, RGB light show, customizable HUD overlay, and automatic online sync via private/public GitHub repositories.

---

## English Guide

### 📻 Features & Controls
* **Shop Item:** Buyable in the shop as **Workable Boombox** (costs $45 by default). Can be stored in inventory, held in hands, or transported in carts.
* **Facility Boomboxes:** Naturally spawned valuable boomboxes in the complex are also upgraded.
* **Multiplayer Synced:** Playback state, track skips, volume, scrubbing, and playlists are fully synced across all players via Photon PUN RPCs with network timestamps.
* **Audio Reactivity:** Speakers pulse with real bass and RMS audio volume, accompanied by RGB reactive lights and optional close-range camera shake.
* **Pocket Companion & Memory:** Continues playing when placed in inventory, hovering over your shoulder with responsive lights, and remembers exact track timestamp, volume, and playback modes.

### 🎮 Keybindings (Hover over boombox or hold in hands)
* **`[E]`** — **Play / Pause** toggle.
* **`[P]`** — **Open Playlist Menu** (3-column UI: Players | Playlists | Tracks with YouTube audio & network sync).
* **`[F]`** — **Next track** in queue.
* **`[R]`** — **Cycle Loop Mode**:
  * `Off` — Plays playlist once and turns off.
  * `Playlist` — Loops entire playlist continuously.
  * `Single` — Loops the currently playing track.
* **`[T]`** — **Shuffle Mode** toggle (`On` / `Off` — true deck shuffle without repeats until all tracks are exhausted).
* **`[V]`** — **Mute / Unmute** toggle.
* **`[Hold V + Mouse Scroll]`** — Smooth **Volume Adjustment** (with HUD indicator `[ 🔊 Volume: 80% ]`).
* **`[Hold F + Mouse Scroll]`** — **Scrub / Rewind / Fast-forward** track time by 5s steps (with time indicator `[ +15s ] 01:25 / 03:40`).

### 🎵 Playlists & YouTube Audio
Playlists are stored in `BepInEx/config/WorkableBoombox/playlists.json` or in individual `.json` files under `BepInEx/config/WorkableBoombox/Playlists/`.
Format:
```json
{
  "My Favorite Mix": {
    "Never Gonna Give You Up": "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
    "Local Track": "local:MyTrack.mp3"
  }
}
```
* **Network Synced:** Each player's playlists are automatically shared across the lobby. You can pick any player's playlist or track from the `[P]` menu!
* **YouTube Downloads:** YouTube tracks are automatically streamed and cached via built-in `yt-dlp` support.

### 🎵 Adding Custom Music
Supported formats: **`.mp3`**, **`.ogg`**, **`.wav`**, **`.aiff`**.

#### Method 1: Local Files
Place your music files into:
`BepInEx/plugins/WorkableBoombox/Tracks/`

#### Method 2: Automatic GitHub Sync
Synchronize tracks with your group via a private or public GitHub repo. In `BepInEx/config/com.bendy659.workableboombox.cfg`:
```ini
[GitHub]
GitHubSource = username/repo-name/github_pat_11AB...
```
The mod checks Git tree commit SHAs at game launch, downloads new tracks directly from the root of the repository, and removes deleted ones automatically.

---

## Русское руководство

### 📻 Основные возможности
* **Покупка в магазине:** Продается в терминале как **Workable Boombox** (по умолчанию $45). Можно брать в инвентарь, носить в руках и возить в тележке.
* **Улучшение найденных бумбоксов:** Найденные в шахте бумбоксы также автоматически становятся рабочими.
* **Сетевая синхронизация:** Состояние воспроизведения, треки, громкость и перемотка синхронизируются у всех игроков в лобби через Photon PUN.
* **Светомузыка и бас:** Динамики и корпус пульсируют в такт басам (RMS), светодиоды светятся мягким RGB, а при громкой музыке рядом ощущается легкая вибрация камеры.
* **Карманный компаньон и память:** При помещении в инвентарь продолжает играть и парит стильным компаньоном за плечом игрока со светомузыкой. Запоминает выбранный трек, точную секунду, громкость и режимы при уборке в карман и сменах уровней.

### 🎮 Управление (при взгляде на бумбокс или в руках)
* **`[E]`** — **Вкл / Выкл** музыки.
* **`[P]`** — **Открыть меню плейлистов** (3-колоночная таблица: Игроки | Плейлисты | Треки, поддержка YouTube и сетевая синхронизация).
* **`[F]`** — **Следующий трек**.
* **`[R]`** — **Режим зацикливания**:
  * `Выкл` — проигрывает список один раз и выключается.
  * `Цикл` — бесконечный повтор плейлиста.
  * `Единый цикл` — зацикливание одного выбранного трека.
* **`[T]`** — **Перемешивание (Shuffle)** (`Вкл` / `Выкл` — колодный рандом без повторов треков до исчерпания очереди).
* **`[V]`** — **Mute / Unmute** (быстрое глушение звука).
* **`[Зажать V + Колёсико мыши]`** — **Регулировка громкости** (с шагом 5% и экранным индикатором `[ 🔊 Громкость: 80% ]`).
* **`[Зажать F + Колёсико мыши]`** — **Перемотка трека** вперед / назад по 5 секунд.

### 🎵 Плейлисты и YouTube
Плейлисты хранятся в `BepInEx/config/WorkableBoombox/playlists.json` или в отдельных `.json` файлах в папке `Playlists/`:
```json
{
  "Мой любимый микс": {
    "Rick Astley": "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
    "Локальный трек": "local:MySong.mp3"
  }
}
```
* **Общие плейлисты в лобби:** Плейлисты всех игроков автоматически видны всем в меню `[P]`. Можно выбрать плейлист любого друга или запустить конкретный трек!
* **YouTube интеграция:** Музыка с YouTube скачивается в фоновом режиме и кэшируется в `Cache/`. При повторном включении играет мгновенно из диска!

### ⚙️ Конфигурация (`com.bendy659.workableboombox.cfg`)
* `Language` — `Auto` (автоматически из игры), `Russian` или `English`.
* `OverlayPositionX` — горизонтальное положение плашки HUD (0.0 = слева ... 0.5 = центр ... 1.0 = справа).
* `OverlayPositionY` — вертикальное положение плашки HUD (0.0 = сверху ... 0.6 = под прицелом ... 1.0 = снизу).
* `EnableCameraShake` — включение/выключение тряски камеры рядом с громкой музыкой (`true` / `false`).
* `EnableVisualPulsing` — включение/выключение пульсации колонок в такт басу (`true` / `false`).
* `ShopPrice` — цена на ценнике в магазине ($45).
* `GitHubSource` — inline-ссылка на репозиторий треков.
