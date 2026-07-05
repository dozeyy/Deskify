# Deskify for macOS

The macOS-native counterpart of Deskify. Same product, same workflow, same
project format — implemented with native macOS APIs (SwiftUI, Accessibility,
NSWorkspace) rather than a port of the Windows code.

## Build

There are two supported paths — both build the same universal (Apple Silicon +
Intel) app from the same sources. Requires macOS 13+ and Xcode (or the Command
Line Tools).

### Command line / CI (SwiftPM)

```
cd macos
./scripts/package-app.sh      # → build/Deskify.app  (universal, signed ad-hoc)
```

Individual steps are available too: `build-release.sh` (compile only),
`make-icons.sh` (regenerate icons), `make-dmg.sh` (drag-to-install `.dmg`),
`sign-notarize.sh` (Developer ID notarization). All read `scripts/config.sh`,
where the signing/versioning placeholders live. For a quick non-bundled dev
run: `swift run`.

### Xcode

The project is defined as a text spec (`project.yml`) generated with
[XcodeGen](https://github.com/yonyz/XcodeGen):

```
brew install xcodegen
cd macos
./scripts/make-icons.sh        # populate the app-icon PNGs (once)
xcodegen generate
open Deskify.xcodeproj
```

You get real **Debug** and **Release** configurations, a `DeskifyCore` static
library (the shared, platform-agnostic module) linked into the `Deskify` app
target, and Run/Archive that produce a proper `Deskify.app`. `project.yml` is
the source of truth — regenerate the `.xcodeproj` any time instead of editing
it by hand.

> Deskify is intentionally **not** sandboxed: controlling other apps' windows
> and launching arbitrary apps is incompatible with the App Sandbox (the same
> reason macOS window managers ship outside the Mac App Store). It is
> Hardened-Runtime-ready for Developer ID notarization — see
> `Resources/Deskify.entitlements` and `scripts/sign-notarize.sh`.

### Distribution pipeline

```
./scripts/build-release.sh     # 1. compile universal Release binary
./scripts/package-app.sh       # 2. assemble + sign Deskify.app
./scripts/make-dmg.sh          # 3. produce Deskify-<version>.dmg
./scripts/sign-notarize.sh     # 4. notarize + staple (needs Developer ID)
```

Steps 1–3 work with zero Apple credentials (ad-hoc signing, local use). To ship
publicly, set `SIGN_IDENTITY`, `TEAM_ID`, and notary credentials in
`scripts/config.sh`; step 4 then notarizes and staples with no other changes.

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
