import Carbon.HIToolbox
import AppKit

/// Registers the system-wide Quick Switch hotkey (⌃Space — the same chord as
/// the Windows version) via the Carbon hotkey API, which is still the
/// supported way to get a global hotkey without Input Monitoring permission.
final class HotkeyManager {
    var onHotkey: (() -> Void)?

    private var hotKeyRef: EventHotKeyRef?
    private var eventHandler: EventHandlerRef?

    /// Returns false when another app already owns ⌃Space (e.g. the input
    /// source switcher, if the user enabled it) — quick switch simply won't
    /// respond to the chord in that case, matching the Windows behavior.
    @discardableResult
    func register() -> Bool {
        var eventType = EventTypeSpec(eventClass: OSType(kEventClassKeyboard),
                                      eventKind: UInt32(kEventHotKeyPressed))
        let selfPointer = Unmanaged.passUnretained(self).toOpaque()

        InstallEventHandler(GetApplicationEventTarget(), { _, _, userData in
            guard let userData else { return noErr }
            let manager = Unmanaged<HotkeyManager>.fromOpaque(userData).takeUnretainedValue()
            DispatchQueue.main.async { manager.onHotkey?() }
            return noErr
        }, 1, &eventType, selfPointer, &eventHandler)

        let hotKeyID = EventHotKeyID(signature: OSType(0x44534B46), id: 1) // 'DSKF'
        let status = RegisterEventHotKey(UInt32(kVK_Space), UInt32(controlKey), hotKeyID,
                                         GetApplicationEventTarget(), 0, &hotKeyRef)
        return status == noErr
    }

    func unregister() {
        if let hotKeyRef { UnregisterEventHotKey(hotKeyRef) }
        if let eventHandler { RemoveEventHandler(eventHandler) }
        hotKeyRef = nil
        eventHandler = nil
    }

    deinit { unregister() }
}
