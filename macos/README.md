# Deskify for macOS

The macOS-native counterpart of Deskify. Same product, same workflow, same
project format — implemented with native macOS APIs (SwiftUI, Accessibility,
NSWorkspace) rather than a port of the Windows code.

## Build

```
cd macos
./scripts/build-app.sh
```

Output: `macos/build/Deskify.app` — a universal binary (Apple Silicon + Intel).
Requires macOS 13+ and Xcode (or the Command Line Tools). For a quick
non-bundled dev run: `swift run` (some bundle-dependent behavior, like the
Dock icon, only works from the .app).

## First run — Accessibility permission

Deskify uses the macOS Accessibility API to see, arrange, and close other
apps' windows — that's the heart of saving and restoring layouts. Grant it
once in **System Settings → Privacy & Security → Accessibility** (Deskify
prompts, and Settings has a Grant Access button showing live status).
Launching apps, opening websites and folders all work without it; only
window layout features need it.

## Data

- Projects: `~/Library/Application Support/Deskify/projects/*.json` (one file per project, human-editable)
- Settings: `~/Library/Application Support/Deskify/settings.json`
- Log: `~/Library/Application Support/Deskify/deskify.log`

The project JSON format is identical to the Windows version (see the root
README) — only the paths inside differ (`.app` bundles instead of `.exe`s,
POSIX folder paths). `window.x/y` are points relative to the target
monitor's top-left corner; `monitor` is 0-based, left-to-right.

## How the Windows features map to macOS

| Windows | macOS |
| --- | --- |
| Launch `.exe` / `.lnk` | `NSWorkspace.openApplication` on `.app` bundles (aliases resolved) |
| `EnumWindows` / `SetWindowPos` | Accessibility API (`AXUIElement` position/size) |
| Window matching by exe/process tree | Bundle-path matching (windows belong to their app on macOS) |
| Maximized state | Window filling the screen's visible frame (what the zoom button does) |
| File Explorer folder windows | Finder windows, matched by `AXDocument`/title |
| URLs via default browser in one launch | `NSWorkspace.open(_:withApplicationAt:)` with every URL — tabs in one window |
| `WM_CLOSE` → verify → kill process tree | `NSRunningApplication.terminate()` → verify → `forceTerminate()` |
| Explorer never killed | Finder windows closed individually; Finder never quit |
| Ctrl+Space global hotkey (RegisterHotKey) | ⌃Space via Carbon `RegisterEventHotKey` |
| Monitors left-to-right, physical px | `NSScreen` left-to-right, points, top-left-origin space |
| `%APPDATA%\Deskify` | `~/Library/Application Support/Deskify` |
| Riot/Valorant hard block | Same block (League of Legends + Vanguard exist on macOS) |

## Architecture

```
macos/Sources/
  DeskifyCore/    Platform-agnostic: models (same JSON format), stores,
                  LaunchEngine, WindowCloser, ProtectedApps — all OS access
                  behind the protocols in Platform.swift.
  Deskify/
    Platform/     macOS implementations: MacWindowService (AX),
                  MacLaunchService/MacScreenService/MacBrowserService
                  (NSWorkspace/NSScreen), InstalledAppsScanner, HotkeyManager.
    UI/           SwiftUI: the same three-column shell, projects list, detail
                  + right context panel, 4-step wizard, settings, capture /
                  app picker / close-others sheets, quick-switch panel,
                  floating layout toolbar, Dark/Light monochrome themes.
```

The Windows app (`src/Deskify.App`) and this app share behavior by design:
`LaunchEngine`, `WindowCloser`, and the stores are line-for-line counterparts
of the C# services, so fixes and features can be mirrored across platforms.

## Known platform differences

- **Accessibility permission** gates window layout (capture/apply/close-others).
  Everything else degrades gracefully without it.
- **"Maximized"** on macOS means "fills the visible screen area" (zoom), not
  the separate full-screen Space — full-screen windows aren't captured as such.
- Apps that manage their own windows unusually (some Java apps) may refuse AX
  moves; Deskify reports this instead of failing silently, same as Windows
  does for elevated processes.
