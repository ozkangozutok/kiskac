import Carbon.HIToolbox
import Foundation
import ServiceManagement

// Tüm ayarlar UserDefaults'ta tutulur. Anahtarlar SettingsView'daki @AppStorage ile aynıdır.
enum Settings {
    private static let d = UserDefaults.standard

    enum Key {
        static let hotKeyCode = "HotKeyCode"
        static let hotKeyMods = "HotKeyModifiers"
        static let useF13 = "UseF13"
        static let playSound = "PlaySound"
        static let dimOpacity = "DimOpacity"
        static let showHint = "ShowHint"
        static let rememberStyle = "RememberStyle"
        static let lastColor = "LastColor"
        static let lastWidth = "LastWidth"
        static let saveDir = "SaveDir"
        static let format = "Format"
        static let jpegQuality = "JpegQuality"
        static let quickSave = "QuickSave"
        static let copyAfterSave = "CopyAfterSave"
        static let downscale = "Downscale"
        static let autoUpdate = "AutoUpdate"
        static let videoDir = "VideoDir"
        static let recordAudio = "RecordAudio"
        static let systemVolume = "SystemVolume"
        static let micVolume = "MicVolume"
        static let videoFps = "VideoFps"
        static let gifFps = "GifFps"
        static let recordCursor = "RecordCursor"
        static let recordCountdown = "RecordCountdown"
    }

    static let videoRates = [15, 30, 60]
    static let gifRates = [5, 8, 10, 15, 20]

    static func registerDefaults() {
        d.register(defaults: [
            Key.hotKeyCode: kVK_ANSI_2,
            Key.hotKeyMods: cmdKey | shiftKey,
            Key.useF13: true,
            Key.playSound: false,
            Key.dimOpacity: 0.45,
            Key.showHint: true,
            Key.rememberStyle: true,
            Key.lastColor: 0,
            Key.lastWidth: 3.0,
            Key.format: "png",
            Key.jpegQuality: 0.92,
            Key.quickSave: false,
            Key.copyAfterSave: false,
            Key.downscale: false,
            Key.autoUpdate: true,
            Key.recordAudio: AudioMode.system.rawValue,
            Key.systemVolume: 100,
            Key.micVolume: 100,
            Key.videoFps: 30,
            Key.gifFps: 10,
            Key.recordCursor: true,
            Key.recordCountdown: true,
        ])
    }

    static var hotKey: HotKey {
        get { HotKey(keyCode: d.integer(forKey: Key.hotKeyCode), modifiers: d.integer(forKey: Key.hotKeyMods)) }
        set {
            d.set(newValue.keyCode, forKey: Key.hotKeyCode)
            d.set(newValue.modifiers, forKey: Key.hotKeyMods)
        }
    }

    static var useF13: Bool { d.bool(forKey: Key.useF13) }
    static var playSound: Bool { d.bool(forKey: Key.playSound) }
    static var dimOpacity: CGFloat { CGFloat(d.double(forKey: Key.dimOpacity)) }
    static var showHint: Bool { d.bool(forKey: Key.showHint) }
    static var rememberStyle: Bool { d.bool(forKey: Key.rememberStyle) }
    static var jpeg: Bool { d.string(forKey: Key.format) == "jpeg" }
    static var jpegQuality: CGFloat { CGFloat(d.double(forKey: Key.jpegQuality)) }
    static var quickSave: Bool { d.bool(forKey: Key.quickSave) }
    static var copyAfterSave: Bool { d.bool(forKey: Key.copyAfterSave) }
    static var downscale: Bool { d.bool(forKey: Key.downscale) }

    static var autoUpdate: Bool { d.bool(forKey: Key.autoUpdate) }

    static var lastUpdateCheck: Date? {
        get { d.object(forKey: "LastUpdateCheck") as? Date }
        set { d.set(newValue, forKey: "LastUpdateCheck") }
    }

    static var skippedVersion: String? {
        get { d.string(forKey: "SkippedVersion") }
        set { d.set(newValue, forKey: "SkippedVersion") }
    }

    static var lastColor: Int {
        get { d.integer(forKey: Key.lastColor) }
        set { d.set(newValue, forKey: Key.lastColor) }
    }

    static var lastWidth: CGFloat {
        get { CGFloat(d.double(forKey: Key.lastWidth)) }
        set { d.set(Double(newValue), forKey: Key.lastWidth) }
    }

    static var saveDir: URL {
        get {
            if let p = d.string(forKey: Key.saveDir) {
                var isDir: ObjCBool = false
                if FileManager.default.fileExists(atPath: p, isDirectory: &isDir), isDir.boolValue {
                    return URL(fileURLWithPath: p, isDirectory: true)
                }
            }
            let pics = FileManager.default.urls(for: .picturesDirectory, in: .userDomainMask).first
                ?? URL(fileURLWithPath: NSHomeDirectory()).appendingPathComponent("Pictures")
            let dir = pics.appendingPathComponent("Kıskaç", isDirectory: true)
            try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            return dir
        }
        set { d.set(newValue.path, forKey: Key.saveDir) }
    }

    // ---------------------------------------------------------------- video kaydı

    static var videoDir: URL {
        get {
            if let p = d.string(forKey: Key.videoDir) {
                var isDir: ObjCBool = false
                if FileManager.default.fileExists(atPath: p, isDirectory: &isDir), isDir.boolValue {
                    return URL(fileURLWithPath: p, isDirectory: true)
                }
            }
            let movies = FileManager.default.urls(for: .moviesDirectory, in: .userDomainMask).first
                ?? URL(fileURLWithPath: NSHomeDirectory()).appendingPathComponent("Movies")
            let dir = movies.appendingPathComponent("Kıskaç", isDirectory: true)
            try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            return dir
        }
        set { d.set(newValue.path, forKey: Key.videoDir) }
    }

    // MP4 ses kaynakları; kayıt çubuğundaki son seçim hatırlanır.
    static var recordAudio: AudioMode {
        get { AudioMode(rawValue: min(max(d.integer(forKey: Key.recordAudio), 0), 3)) }
        set { d.set(newValue.rawValue, forKey: Key.recordAudio) }
    }

    // Ses seviyeleri, yüzde (0–200)
    static var systemVolume: Int {
        get { min(max(d.integer(forKey: Key.systemVolume), 0), 200) }
        set { d.set(min(max(newValue, 0), 200), forKey: Key.systemVolume) }
    }

    static var micVolume: Int {
        get { min(max(d.integer(forKey: Key.micVolume), 0), 200) }
        set { d.set(min(max(newValue, 0), 200), forKey: Key.micVolume) }
    }

    // MP4: 15, 30 veya 60; GIF: 5–20 kare/sn
    static var videoFps: Int {
        let v = d.integer(forKey: Key.videoFps)
        return videoRates.contains(v) ? v : 30
    }

    static var gifFps: Int { min(max(d.integer(forKey: Key.gifFps), 5), 20) }
    static var recordCursor: Bool { d.bool(forKey: Key.recordCursor) }
    static var recordCountdown: Bool { d.bool(forKey: Key.recordCountdown) }

    static var initialized: Bool {
        get { d.bool(forKey: "Initialized") }
        set { d.set(newValue, forKey: "Initialized") }
    }

    static var askedScreenCapture: Bool {
        get { d.bool(forKey: "AskedScreenCapture") }
        set { d.set(newValue, forKey: "AskedScreenCapture") }
    }
}

// Oturum açılınca başlatma (macOS 13+ SMAppService, ek yardımcı uygulama gerektirmez).
enum LoginItem {
    static var isEnabled: Bool {
        SMAppService.mainApp.status == .enabled
    }

    static func set(_ on: Bool) {
        do {
            if on { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
        } catch {
            NSLog("Kıskaç: başlangıç öğesi ayarlanamadı: \(error)")
        }
    }
}
