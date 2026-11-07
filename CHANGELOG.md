# Changelog

## 1.2.2
- **Track Synchronization Modes (`SyncMode`)**:
  - `0 = None`: Independent playback right after download.
  - `1 = LiveStream` (default): Synchronized playback by global network timecode; first downloader starts at 00:00 and late downloaders catch up in real-time.
  - `2 = WaitForAll`: Collective ready-check across all players in the room with 12s host timeout fallback.
- **Controls & Keybindings**:
  - Added `RestartTrackKey` (`KeyCode.Z` by default) to instantly restart the current track from 00:00 without toggling playback.
  - Added `[Z] Restart Track` prompt in the HUD overlay.
- **Visuals & Bug Fixes**:
  - Added `LightMusicBrightness` setting (0.0 to 2.0, default: 0.4).
  - Fixed light show and speaker emissions persisting when re-equipping the boombox from hotbar.
  - Fixed hardware cursor visibility in the Playlist Menu (hidden behind 3D in-game cursor).

## 1.2.1
- **Playlist & Track Import/Export**:
  - Export any local playlist to clipboard with 1 click.
  - Import other players' playlists with 1 click.
  - Import individual tracks from other players into your active playlist.
  - Direct clipboard paste button in the Add Track interface.
- **Audio & Physics Improvements**:
  - Centered 3D acoustic audio source between the two physical speakers.
  - Pauses previous track during download; new track starts from 00:00.
  - Centered boombox spawn location on the truck table.
- **Visuals & Quality of Life**:
  - Added `EnableLightMusic` toggle in config with point lights (8m range).
  - Isolated UI inputs: chat and player movement are blocked while playlist menu is open.
  - Adjusted default shop price to 15.

## 1.1.5
- Playlist synchronization improvements and scrub overlay fixes.

## 1.0.3
- Added volume controls, looping, shuffle, and hover prompt HUD.
