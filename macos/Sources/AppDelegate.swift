import AppKit
import Carbon.HIToolbox

// Uygulamanın gövdesi: menü çubuğu simgesi, kısayol ve çekimi başlatma.
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private var statusItem: NSStatusItem!
    private let hotKeys = HotKeys()
    private var session: OverlaySession?
    private var capturing = false
    private var loginItem: NSMenuItem!
    private var permissionItem: NSMenuItem!

    func applicationDidFinishLaunching(_ notification: Notification) {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem.button?.image = Logo.menuBarImage()
        statusItem.button?.toolTip = "Kıskaç — ⇧⌘2 ile ekran görüntüsü"

        let menu = NSMenu()
        menu.delegate = self
        let capture = NSMenuItem(title: "Ekran görüntüsü al", action: #selector(captureFromMenu), keyEquivalent: "2")
        capture.keyEquivalentModifierMask = [.command, .shift]
        menu.addItem(capture)
        menu.addItem(NSMenuItem(title: "Kayıt klasörünü aç", action: #selector(openSaveDir), keyEquivalent: ""))
        permissionItem = NSMenuItem(title: "Ekran kaydı izni ver…", action: #selector(openPermissionSettings), keyEquivalent: "")
        menu.addItem(permissionItem)
        menu.addItem(.separator())
        loginItem = NSMenuItem(title: "Oturum açılınca başlat", action: #selector(toggleLogin), keyEquivalent: "")
        menu.addItem(loginItem)
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Kıskaç'tan çık", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
        for item in menu.items where item.action != #selector(NSApplication.terminate(_:)) { item.target = self }
        statusItem.menu = menu

        // ⇧⌘2: macOS'un ⇧⌘3/4/5 kısayollarıyla çakışmaz.
        // F13: PC klavyelerindeki PrtSc tuşu macOS'ta F13 olarak gelir.
        HotKeys.handler = { [weak self] in self?.startCapture() }
        let a = hotKeys.register(keyCode: kVK_ANSI_2, modifiers: cmdKey | shiftKey)
        let b = hotKeys.register(keyCode: kVK_F13, modifiers: 0)
        if !a && !b {
            alert("Kısayol kaydedilemedi",
                  "⇧⌘2 başka bir uygulama tarafından kullanılıyor. Menü çubuğundaki simgeden çekim yapabilirsin.")
        }

        if !Settings.initialized {
            Settings.initialized = true
            LoginItem.set(true)
            alert("Kıskaç çalışıyor",
                  "⇧⌘2 tuşlarına bas ve alanı seç. ⌘C kopyalar, ⌘S kaydeder.\n\nKıskaç menü çubuğunda çalışır; ilk çekimde macOS ekran kaydı izni isteyecek.",
                  style: .informational)
            requestPermission()
        }
    }

    // Uygulama zaten çalışırken yeniden açılırsa (Finder, Spotlight, Dock): ekran görüntüsü al.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        startCapture()
        return false
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        loginItem.state = LoginItem.isEnabled ? .on : .off
        permissionItem.isHidden = ScreenCapture.hasPermission
    }

    // Menü kapanma animasyonu görüntüye girmesin.
    @objc private func captureFromMenu() {
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) { [weak self] in self?.startCapture() }
    }

    @objc private func openSaveDir() {
        NSWorkspace.shared.open(Settings.saveDir)
    }

    @objc private func toggleLogin() {
        LoginItem.set(!LoginItem.isEnabled)
    }

    @objc private func openPermissionSettings() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture") {
            NSWorkspace.shared.open(url)
        }
    }

    // İlk seferde sistemin kendi izin penceresi, sonrakilerde açıklama ve Ayarlar'a yönlendirme.
    @discardableResult
    private func requestPermission() -> Bool {
        if ScreenCapture.hasPermission { return true }
        if !Settings.askedScreenCapture {
            Settings.askedScreenCapture = true
            return CGRequestScreenCaptureAccess()
        }
        showPermissionAlert()
        return false
    }

    private func showPermissionAlert() {
        let a = NSAlert()
        a.messageText = "Ekran kaydı izni gerekli"
        a.informativeText = "Sistem Ayarları › Gizlilik ve Güvenlik › Ekran ve Sistem Sesi Kaydı bölümünden Kıskaç'ı aç. İzinden sonra Kıskaç'ı yeniden başlatman gerekebilir."
        a.addButton(withTitle: "Sistem Ayarları'nı aç")
        a.addButton(withTitle: "Vazgeç")
        NSApp.activate(ignoringOtherApps: true)
        if a.runModal() == .alertFirstButtonReturn { openPermissionSettings() }
    }

    func startCapture() {
        if let s = session {
            s.bringToFront()
            return
        }
        if capturing { return }
        if !requestPermission() { return }

        capturing = true
        Task { @MainActor in
            defer { capturing = false }
            do {
                let shots = try await ScreenCapture.captureAll()
                guard !shots.isEmpty else { return }
                let s = OverlaySession(shots: shots)
                s.onClose = { [weak self] in self?.session = nil }
                session = s
                s.show()
            } catch {
                NSLog("Kıskaç: çekim hatası: \(error)")
                if !ScreenCapture.hasPermission {
                    showPermissionAlert()
                } else {
                    alert("Ekran yakalanamadı", error.localizedDescription)
                }
            }
        }
    }

    private func alert(_ title: String, _ text: String, style: NSAlert.Style = .warning) {
        let a = NSAlert()
        a.messageText = title
        a.informativeText = text
        a.alertStyle = style
        NSApp.activate(ignoringOtherApps: true)
        a.runModal()
    }
}
