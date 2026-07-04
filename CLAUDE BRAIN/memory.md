# AI Brain — Memory Log (Root)

## 2026-07-03 (third session)
- Fixed stacked/tabbed folder saving: Windows 11 Explorer stacks folders as TABS sharing one window handle, but matching only read the first tab's path — stacked folders failed to save/restore layouts. ExplorerWindows now maps each window to ALL of its tab paths (one cached COM enumeration instead of one per window), path comparison ignores trailing backslashes, and two folder entries may share one tabbed window instead of the second reporting "not found". Same normalization applied to auto-linked entry sync and wizard folder dedupe.
- Logo unified to the outlined style (the in-app badge Will prefers): regenerated deskify.ico with stroked "blank" boxes instead of filled — exe/taskbar/shortcuts now match the badge; Light mode keeps the inverted badge by design.
- Edit Layout got its own icon (window-split IcoLayout) so it no longer shares the pencil with Edit project; the floating layout toolbar uses it too.
- Native title bar now follows the theme (DWM immersive dark mode): dark in Dark theme, light in Light, applied to main window + all dialogs and retinted live on theme switch.

## 2026-07-03 (later session)
- Autosave on exit added — nothing typed is lost when the app closes:
  - Wizard mid-edit → saved to %APPDATA%\Deskify\draft.json (project data + step + original file path); restored into the wizard at the same step on next startup, then cleared. Save/Cancel also clear it; an untouched empty new-project wizard is not drafted. Verified end-to-end (plant draft → restored+consumed; graceful close mid-wizard → draft re-written correctly).
  - Settings → valid changed values auto-apply when leaving the page or closing the app (explicit Save button kept for validation feedback). Re-clicking the Settings nav no longer wipes unsaved box edits.
  - Notes → saved on close if changed (LostFocus doesn't fire when closing with the box focused).
- New logo: monochrome rounded square with a tiled window layout (tall pane + two stacked panes), matching the app palette (#E0E0E0 bg, #121212/#8A8A8A tiles). Multi-size deskify.ico (16–256px, PNG frames) at src\Deskify.App\Assets\deskify.ico, wired via <ApplicationIcon> — exe, taskbar, window chrome, and shortcuts all use it. In-app brand badge + empty state use the matching IcoDeskify geometry in Styles.xaml. Icon generator source kept in session scratchpad (WPF render → ICO writer) — trivially recreatable.

## 2026-07-03
- Full review + stabilization pass on Deskify (0 warnings, builds clean, smoke-tested).
- Bugs fixed:
  - Wizard Cancel button was hidden under the Next button (both in the same grid column) — Cancel is now clickable.
  - Blocked/invalid URL feedback in the wizard was written to a hidden label — now shows inline next to the URL box, with basic URL validation and duplicate rejection.
  - Quick switch (Ctrl+Space): an accidental tap instantly launched the top project via release-polling; typing to filter required clicking the box while holding both keys. Reworked to a standard launcher popup: opens focused on search, type to filter, Up/Down + Enter or click to launch, Esc/click-away to close. Removed the 40 ms key-poll timer.
  - "Add Selected" with nothing selected closed the app picker as if cancelled — Add buttons in the picker and capture dialogs now stay disabled until something is selected (capture dialog also shows a live selected count).
  - Project delete could throw an unhandled exception on a locked file — now guarded, failure reported in status without dropping the project from the list.
  - Duplicate folders/URLs could be added to a project (double launch on restore) — deduped on add.
  - Edit Layout had no reentrancy guard — double-click double-launched every app; now guarded alongside Launch.
  - A matched window that Windows refused to reposition (e.g. elevated apps) was dropped silently — now reported as an error.
- Consistency/polish: scrollbar thumb color was hardcoded (broken in Light theme) — now theme-driven; "workspace"/"project" terminology unified to "project"; list rows and settings cards left-aligned to match the dialogs; checkbox template reworked so long labels wrap instead of clipping; placeholder text added to search boxes and the URL box; app(s)/window(s) strings properly pluralized; quick-switch settings copy updated; wizard progress segments rounded.

## 2026-07-01
- Initialized AI Brain structure with root instructions system and project folder organization.
- Defined core domains: applications (general), music production, and game development.
- Replaced widget-specific scope with generalized application development focus.
- Established performance-first priorities:
  - Fast startup and responsiveness
  - Low-latency UI interactions
  - Persistent session state
  - Efficient resource usage
- Set strict preference for direct, implementation-focused communication style.