import AppKit
import Carbon.HIToolbox

// Tuş kodu + Carbon değiştiricileri (cmdKey, shiftKey, optionKey, controlKey).
struct HotKey: Equatable {
    var keyCode: Int
    var modifiers: Int

    static let functionKeys: [Int: Int] = [
        kVK_F1: 1, kVK_F2: 2, kVK_F3: 3, kVK_F4: 4, kVK_F5: 5, kVK_F6: 6, kVK_F7: 7, kVK_F8: 8, kVK_F9: 9, kVK_F10: 10,
        kVK_F11: 11, kVK_F12: 12, kVK_F13: 13, kVK_F14: 14, kVK_F15: 15, kVK_F16: 16, kVK_F17: 17, kVK_F18: 18,
        kVK_F19: 19, kVK_F20: 20,
    ]

    private static let specialNames: [Int: String] = [
        kVK_Space: "Boşluk", kVK_Return: "↩", kVK_Tab: "⇥", kVK_Delete: "⌫", kVK_ForwardDelete: "⌦",
        kVK_LeftArrow: "←", kVK_RightArrow: "→", kVK_UpArrow: "↑", kVK_DownArrow: "↓",
        kVK_Home: "↖", kVK_End: "↘", kVK_PageUp: "⇞", kVK_PageDown: "⇟",
    ]

    // Uygulamalarda standart olan ⌘ kısayolları (kopyala, yapıştır, çık…); genel kısayol olursa her yerde bozulurlar.
    private static let standardCommandKeys: Set<Int> = [
        kVK_ANSI_Q, kVK_ANSI_W, kVK_ANSI_C, kVK_ANSI_V, kVK_ANSI_X, kVK_ANSI_Z, kVK_ANSI_A, kVK_ANSI_S, kVK_ANSI_F,
        kVK_ANSI_N, kVK_ANSI_T, kVK_ANSI_P, kVK_ANSI_H, kVK_ANSI_M, kVK_ANSI_O, kVK_ANSI_Comma, kVK_Tab, kVK_Space,
    ]

    private static let modifierMask = cmdKey | shiftKey | optionKey | controlKey

    // Sistem Ayarları › Klavye › Klavye Kısayolları'nda etkin olanlar (ekran görüntüsü, Spotlight, Mission Control…).
    static func systemShortcuts() -> [HotKey] {
        var arr: Unmanaged<CFArray>?
        guard CopySymbolicHotKeys(&arr) == noErr, let list = arr?.takeRetainedValue() as? [[String: Any]] else { return [] }
        return list.compactMap { d in
            guard (d[kHISymbolicHotKeyEnabled] as? Bool) ?? false,
                  let code = (d[kHISymbolicHotKeyCode] as? NSNumber)?.intValue,
                  let mods = (d[kHISymbolicHotKeyModifiers] as? NSNumber)?.intValue
            else { return nil }
            return HotKey(keyCode: code, modifiers: mods & modifierMask)
        }
    }

    // Kısayol kullanılamıyorsa nedenini döner.
    var problem: String? {
        if !isFunctionKey && modifiers & (cmdKey | controlKey) == 0 {
            return "⌘ veya ⌃ ile birlikte bir tuşa bas. F tuşları tek başına da olur."
        }
        let m = modifiers & Self.modifierMask
        if Self.standardCommandKeys.contains(keyCode) && (m == cmdKey || m == cmdKey | shiftKey) {
            return "\(display) uygulamalarda standart bir kısayol, başka bir tane seç."
        }
        if Self.systemShortcuts().contains(HotKey(keyCode: keyCode, modifiers: m)) {
            return "\(display) bir macOS sistem kısayolu, başka bir tane seç."
        }
        return nil
    }

    init(keyCode: Int, modifiers: Int) {
        self.keyCode = keyCode
        self.modifiers = modifiers
    }

    init(event e: NSEvent) {
        keyCode = Int(e.keyCode)
        let f = e.modifierFlags.intersection(.deviceIndependentFlagsMask)
        var m = 0
        if f.contains(.command) { m |= cmdKey }
        if f.contains(.shift) { m |= shiftKey }
        if f.contains(.option) { m |= optionKey }
        if f.contains(.control) { m |= controlKey }
        modifiers = m
    }

    var isFunctionKey: Bool { Self.functionKeys[keyCode] != nil }

    var cocoaModifiers: NSEvent.ModifierFlags {
        var f: NSEvent.ModifierFlags = []
        if modifiers & controlKey != 0 { f.insert(.control) }
        if modifiers & optionKey != 0 { f.insert(.option) }
        if modifiers & shiftKey != 0 { f.insert(.shift) }
        if modifiers & cmdKey != 0 { f.insert(.command) }
        return f
    }

    var keyName: String {
        if let n = Self.functionKeys[keyCode] { return "F\(n)" }
        if let s = Self.specialNames[keyCode] { return s }
        return Self.layoutCharacter(keyCode)?.uppercased() ?? "#\(keyCode)"
    }

    var display: String {
        var s = ""
        if modifiers & controlKey != 0 { s += "⌃" }
        if modifiers & optionKey != 0 { s += "⌥" }
        if modifiers & shiftKey != 0 { s += "⇧" }
        if modifiers & cmdKey != 0 { s += "⌘" }
        return s + keyName
    }

    // Menüde gösterilecek tuş eşdeğeri
    var menuKeyEquivalent: String {
        if let n = Self.functionKeys[keyCode], let u = UnicodeScalar(NSF1FunctionKey + n - 1) {
            return String(Character(u))
        }
        if Self.specialNames[keyCode] != nil { return "" }
        return Self.layoutCharacter(keyCode)?.lowercased() ?? ""
    }

    // Tuş kodunun etkin klavye düzenindeki karşılığı (Türkçe Q/F düzenlerinde doğru harf).
    private static func layoutCharacter(_ code: Int) -> String? {
        guard let src = TISCopyCurrentKeyboardLayoutInputSource()?.takeRetainedValue()
                ?? TISCopyCurrentASCIICapableKeyboardLayoutInputSource()?.takeRetainedValue(),
              let ptr = TISGetInputSourceProperty(src, kTISPropertyUnicodeKeyLayoutData)
        else { return nil }
        let data = Unmanaged<CFData>.fromOpaque(ptr).takeUnretainedValue() as Data
        var dead: UInt32 = 0
        var chars = [UniChar](repeating: 0, count: 4)
        var len = 0
        let st = data.withUnsafeBytes { raw -> OSStatus in
            guard let layout = raw.bindMemory(to: UCKeyboardLayout.self).baseAddress else { return -1 }
            return UCKeyTranslate(layout, UInt16(code), UInt16(kUCKeyActionDisplay), 0, UInt32(LMGetKbdType()),
                                  OptionBits(kUCKeyTranslateNoDeadKeysBit), &dead, chars.count, &len, &chars)
        }
        guard st == noErr, len > 0 else { return nil }
        let s = String(utf16CodeUnits: chars, count: len)
        return s.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? nil : s
    }
}

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
    func register(_ key: HotKey) -> Bool {
        installHandler()
        var ref: EventHotKeyRef?
        let id = EventHotKeyID(signature: OSType(0x4B534B43), id: UInt32(refs.count + 1)) // 'KSKC'
        let st = RegisterEventHotKey(UInt32(key.keyCode), UInt32(key.modifiers), id, GetApplicationEventTarget(), 0, &ref)
        guard st == noErr, let r = ref else { return false }
        refs.append(r)
        return true
    }

    func unregisterAll() {
        for r in refs { UnregisterEventHotKey(r) }
        refs.removeAll()
    }

    deinit { unregisterAll() }
}
