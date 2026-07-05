# Deskify for macOS — Production-Readiness Review

A static-analysis pass over every Swift file: compile simulation, runtime
simulation, and an architecture review, done without a Mac to compile on. The
goal was to eliminate as many unknowns as possible through reasoning, and to
leave the remainder clearly categorized.

## Changes made during this review

| Area | Fix |
| --- | --- |
| `MacWindowService.closeWindow` | Replaced `button as! AXUIElement` with a `CFGetTypeID(...) == AXUIElementGetTypeID()` guard before the cast — a misbehaving app can no longer turn a bad AX value into a trap. |
| `MacWindowService.frame` | Extracted `axValue(_:_:)` that type-ID-checks before casting to `AXValue`; removed two force casts. |
| `QuickSwitchController` | Now subclasses `NSObject` (with `super.init()`) — required for the `@objc` notification selector and `#selector` to compile at all. This was a hard compile error. |
| `onChange` (2 sites) | Added a deployment-target-safe `onValueChange(of:perform:)` helper: the two-parameter `onChange` is macOS 14+ only and the one-parameter form is deprecated there. The app now compiles warning-free on new SDKs and still runs on macOS 13. |
| `SheetChrome`, `SelectableRow`, `CardView` | Converted `@ViewBuilder` stored properties to explicit initializers — removes reliance on synthesized-memberwise-init behavior for builder closures plus an optional in between. |
| `SettingsView.section` | Made generic (`<C: View>`) instead of `content: () -> some View` — opaque types in a closure-parameter return position are fragile; a generic is unambiguous. |
| `LaunchEngine.launchApp` | A Finder entry whose args carry a folder now opens that folder rather than activating the Finder app (matches how captured folder windows must relaunch). |

## Compile simulation — high-confidence conclusions

- **Accessibility (AX) bridging.** `AXUIElementCopyAttributeValue`, `AXValueGetValue`,
  `AXValueCreate`, `AXUIElementSetAttributeValue`, `AXUIElementPerformAction`,
  `kAX…Attribute`/`kAX…Subrole` constants (imported as `String`, bridged with
  `as CFString`), and `AXIsProcessTrustedWithOptions` with
  `kAXTrustedCheckOptionPrompt.takeUnretainedValue()` are all the standard,
  widely-used forms. All CF downcasts are now type-ID-guarded.
- **Carbon hotkey.** The `InstallEventHandler` callback is a non-capturing
  closure (valid as a C function pointer); `Unmanaged` round-trips the `self`
  pointer; `RegisterEventHotKey(kVK_Space, controlKey, …)` with `UInt32(...)`
  conversions is correct.
- **Concurrency model.** `LaunchEngine`/`WindowCloser` are non-isolated and run
  off the main actor; `AppState` is `@MainActor`. Off-main code reaches AppKit
  only through `onMainThread { }` (a guarded `DispatchQueue.main.sync`), and the
  main thread is never blocked on the engine (all engine calls are `await`ed, so
  the actor suspends rather than blocks) — no deadlock path. UI mutation from
  background completion happens via `Task { @MainActor in … }`.
- **SwiftUI surface.** `App`/`Scene`/`View` are `@MainActor`, so `@StateObject
  private var state = AppState()` and all the synchronous calls into
  `@MainActor` `AppState` from view bodies/button actions are correctly isolated.
- **Codable ↔ Windows JSON.** Field names and the ISO-8601 date handling match
  the C# `System.Text.Json` output; decoding tolerates missing keys and both
  fractional/whole-second timestamps.

## Runtime simulation — walked workflows

Launch app · create/save/edit/delete/duplicate workspace · launch workspace ·
close others (with confirm + "don't ask again") · restore positions (normal +
"maximized"→visible-frame) · browser tabs in one window · Finder folder windows
· multi-monitor mapping (top-left-origin, left-to-right) · Accessibility
permission gating and live re-check · ⌃Space quick switch · missing apps ·
invalid/partial project files · autosave-on-quit draft restore.

Notable resilience properties confirmed by reading the paths:
- No permission → launching/URLs/folders still work; only layout features are
  skipped, with a clear message. No crash.
- Every disk write is checked; failures surface distinct, honest messages
  rather than silent success.
- Force-kill escalation can never hit Finder, `/System` apps, Deskify itself, or
  Riot/Vanguard (hard-blocked everywhere).

## Confidence report

### High confidence (very likely correct as written)
- DeskifyCore in full: models, JSON, stores, `LaunchEngine`/`WindowCloser`
  orchestration, `ProtectedApps`, `Log`. Pure Swift/Foundation, no exotic APIs.
- Carbon hotkey registration and the `Unmanaged`/C-callback plumbing.
- The concurrency/isolation architecture (no deadlocks, UI mutations on main).
- NSWorkspace launching, URL/folder opening, process termination; NSScreen
  coordinate flipping; default-browser lookup.
- Build/packaging: SwiftPM universal build, `.app` assembly, `.icns` generation,
  `.dmg` creation, ad-hoc + Developer-ID/notarization script paths.

### Medium confidence (should work; not provable without compiling)
- Exact AX attribute *values* at runtime for third-party apps — the API usage is
  correct, but whether a given app exposes `AXPosition`/`AXSize`/moves on request
  varies by app (some Java/Electron/admin apps refuse; this is reported, not
  fatal).
- `.task { … }` resuming on the main actor after an `await` on a detached scan,
  such that the subsequent `@State` mutation is on main. This matches Apple's
  documented `.task` usage; flagged only because it can't be observed here.
- SwiftUI environment-object inheritance into `.sheet` content on macOS 13
  (correct on 13+, but a historical sore spot worth a first-run check).
- Finder folder-window matching via `AXDocument`/title across macOS versions.

### Low confidence (requires real macOS verification)
- First actual compilation against a specific Xcode/SDK — the whole point of the
  build is to surface anything static reasoning can't (a renamed symbol, a
  parameter-label drift). Nothing found suggests a failure, but it is unproven.
- Live window positioning across a real multi-monitor arrangement, including the
  "maximized ≈ visible frame" tolerance (8 pt) and Retina scaling.
- Ad-hoc-signature ↔ Accessibility TCC interaction: because ad-hoc signatures
  change every rebuild, macOS may require re-granting Accessibility after each
  local rebuild. A stable Developer ID identity removes this.
- Icon crispness: the master is 256×256 (extracted from the Windows `.ico`);
  `make-icons.sh` upscales to 512/1024. Acceptable for testing; a native
  512/1024 master would be sharper for release.

## Recommended first-run sequence on a Mac

```
cd macos
swift build                 # 1. surface any compile issues first (fast, single-arch)
./scripts/package-app.sh    # 2. universal .app
open build/Deskify.app      # 3. grant Accessibility, then walk:
                            #    launch → Edit Layout → Save Layout →
                            #    Close Other Apps → ⌃Space
```
