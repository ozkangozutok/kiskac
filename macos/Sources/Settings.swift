import Foundation
import ServiceManagement

enum Settings {
    private static let d = UserDefaults.standard

    static var saveDir: URL {
        get {
            if let p = d.string(forKey: "SaveDir") {
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
        set { d.set(newValue.path, forKey: "SaveDir") }
    }

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
