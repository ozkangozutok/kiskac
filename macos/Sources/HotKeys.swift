import Carbon.HIToolbox
import Foundation

// Sistem genelinde kısayol. Carbon RegisterEventHotKey Erişilebilirlik veya
// Girdi İzleme izni istemez; olay dinleme (event tap) yöntemleri ister.
final class HotKeys {
    nonisolated(unsafe) static var handler: (() -> Void)?
    private var refs: [EventHotKeyRef] = []
    private var installed = false

    private func installHandler() {
        if installed { return }
        installed = true
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, _, _ in
            DispatchQueue.main.async { HotKeys.handler?() }
            return noErr
        }, 1, &spec, nil, nil)
    }

    @discardableResult
    func register(keyCode: Int, modifiers: Int) -> Bool {
        installHandler()
        var ref: EventHotKeyRef?
        let id = EventHotKeyID(signature: OSType(0x4B534B43), id: UInt32(refs.count + 1)) // 'KSKC'
        let st = RegisterEventHotKey(UInt32(keyCode), UInt32(modifiers), id, GetApplicationEventTarget(), 0, &ref)
        guard st == noErr, let r = ref else { return false }
        refs.append(r)
        return true
    }

    deinit {
        for r in refs { UnregisterEventHotKey(r) }
    }
}
