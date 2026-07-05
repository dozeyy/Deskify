# Deskify

Workspace automation for Windows 10/11 and macOS. Create projects that restore your entire
desktop environment — apps, websites, folders, and exact window layout — in one click.

## Build (Windows)

```
dotnet build Deskify.sln -c Release
```

Output: `src\Deskify.App\bin\Release\net8.0-windows\Deskify.exe`
Requires the .NET 8 SDK. No admin privileges needed to build or run.

## Build (macOS)

```
cd macos && ./scripts/package-app.sh
```

Output: `macos/build/Deskify.app` (universal: Apple Silicon + Intel).
Or open it in Xcode via `xcodegen generate && open Deskify.xcodeproj`.
Same product, same workflow, same project JSON format — see `macos/README.md`.

## Data

- Projects: `%APPDATA%\Deskify\projects\*.json` (one file per project, human-editable)
- Settings: `%APPDATA%\Deskify\settings.json`
- Log: `%APPDATA%\Deskify\deskify.log`

## Project JSON format

```json
{
  "name": "Coding Workspace",
  "apps": [
    {
      "name": "VS Code",
      "path": "C:\\Program Files\\Microsoft VS Code\\Code.exe",
      "args": null,
      "window": { "monitor": 0, "x": 0, "y": 0, "width": 960, "height": 1080, "state": "normal" }
    }
  ],
  "folders": ["D:\\Projects\\App"],
  "urls": ["https://github.com"],
  "launchOrder": ["apps", "urls", "folders"],
  "strictLayout": false
}
```

Notes:
- `window.x/y` are physical pixels relative to the target monitor's top-left corner,
  so layouts survive monitor rearrangement. `monitor` is 0-based (left-to-right order).
- `state` is `"normal"` or `"maximized"`.
- `launchOrder` is optional; default is apps → urls → folders. Editable in the JSON.
- `args` supports launcher-style apps, e.g. Discord:
  `"path": "...\\Discord\\Update.exe", "args": "--processStart Discord.exe"` —
  window matching follows the `--processStart` target.

## How launch works

1. Apps start first (per `launchOrder`), then URLs (default browser) and folders (Explorer).
2. Deskify polls for each app's window (default every 500 ms, 15 s timeout — both in Settings).
   New windows are preferred over pre-existing ones, so single-instance apps
   (VS Code, browsers) still match the right window.
3. Each window is restored with `SetWindowPos`: monitor, position, size, normal/maximized.
4. **Soft mode** (default): best effort, silent. **Strict mode** (per project): after launch,
   positions are verified for ~3 s and re-applied if the app moved/resized itself.

## Creating projects

Projects are the only structure in Deskify — each holds apps, folders, websites, and
layout data. There are no templates.

- **New Project** — a short step-by-step flow: name → apps → folders → websites → finish.
  Add apps by browsing for an exe/lnk or picking from windows open right now.
- **Capture session** — pick from running windows; their current positions become the
  layout. Nothing is saved until you confirm in the create flow.

## Edit Layout

Open a project → **Edit Layout**. Deskify launches any project apps that aren't running
and opens the project's websites (as tabs in your browser — a new window if it's closed,
or added to your current window with existing tabs kept), then shows a floating toolbar. Drag/resize windows where you want them, then **Save Layout**
captures position, size, monitor, and window state per app. The toolbar's **Snap** toggle
rounds saved positions to the grid (size configurable in Settings); **Reset** cancels
without saving.
