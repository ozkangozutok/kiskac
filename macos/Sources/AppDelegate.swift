import AppKit
import Carbon.HIToolbox

// Uygulamanın gövdesi: menü çubuğu simgesi, kısayol ve çekimi başlatma.
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private var statusItem: NSStatusItem!
    private let hotKeys = HotKeys()
    private var session: OverlaySession?
    private var capturing = false
    private var captureItem: NSMenuItem!
    private var loginItem: NSMenuItem!
    private var permissionItem: NSMenuItem!
    private var updateItem: NSMenuItem!
    private let updater = Updater()
    private lazy var settingsWindow: SettingsWindowController = {
        let c = SettingsWindowController()
        c.model.applyHotKeys = { [weak self] in self?.applyHotKeys() ?? false }
        c.model.pauseHotKeys = { [weak self] in self?.hotKeys.unregisterAll() }
        c.model.checkForUpdates = { [weak self] in self?.updater.check(userInitiated: true) }
        return c
    }()

    private static let shutterSound: NSSound? = {
        let path = "/System/Library/Components/CoreAudio.component/Contents/SharedSupport/SystemSounds/system/Screen Capture.aif"
        return NSSound(contentsOfFile: path, byReference: true) ?? NSSound(named: "Tink")
    }()

    func applicationDidFinishLaunching(_ notification: Notification) {
        Settings.registerDefaults()

        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem.button?.image = Logo.menuBarImage()

        let menu = NSMenu()
        menu.delegate = self
        captureItem = NSMenuItem(title: "Ekran görüntüsü al", action: #selector(captureFromMenu), keyEquivalent: "")
        menu.addItem(captureItem)
        menu.addItem(NSMenuItem(title: "Kayıt klasörünü aç", action: #selector(openSaveDir), keyEquivalent: ""))
        permissionItem = NSMenuItem(title: "Ekran kaydı izni ver…", action: #selector(openPermissionSettings), keyEquivalent: "")
        menu.addItem(permissionItem)
        menu.addItem(.separator())
        loginItem = NSMenuItem(title: "Oturum açılınca başlat", action: #selector(toggleLogin), keyEquivalent: "")
        menu.addItem(loginItem)
        menu.addItem(NSMenuItem(title: "Ayarlar…", action: #selector(openSettings), keyEquivalent: ","))
        updateItem = NSMenuItem(title: "Güncellemeleri denetle…", action: #selector(checkForUpdates), keyEquivalent: "")
        menu.addItem(updateItem)
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Kıskaç'tan çık", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
        for item in menu.items where item.action != #selector(NSApplication.terminate(_:)) { item.target = self }
        statusItem.menu = menu

        updater.onStateChange = { [weak self] state in
            self?.updateItem.title = state ?? "Güncellemeleri denetle…"
            self?.updateItem.isEnabled = state == nil
        }
        updater.canInterrupt = { [weak self] in self?.session == nil && self?.capturing == false }
        updater.startAutomaticChecks()

        HotKeys.handler = { [weak self] in self?.startCapture() }
        if !applyHotKeys() {
            alert("Kısayol kaydedilemedi",
                  "\(Settings.hotKey.display) başka bir uygulama tarafından kullanılıyor. Ayarlar'dan başka bir kısayol seçebilirsin.")
        }

        if !Settings.initialized {
            Settings.initialized = true
            LoginItem.set(true)
            alert("Kıskaç çalışıyor",
                  "\(Settings.hotKey.display) tuşlarına bas ve alanı seç. ⌘C kopyalar, ⌘S kaydeder.\n\nKıskaç menü çubuğunda çalışır; kısayolu ve diğer ayarları oradan değiştirebilirsin. İlk çekimde macOS ekran kaydı izni isteyecek.",
                  style: .informational)
            requestPermission()
        }
    }

    // Ana kısayol + isteğe bağlı F13 (PC klavyelerindeki PrtSc tuşu macOS'ta F13 olarak gelir).
    // Ana kısayol kaydedildiyse true döner.
    @discardableResult
    private func applyHotKeys() -> Bool {
        hotKeys.unregisterAll()
        let main = Settings.hotKey
        let ok = hotKeys.register(main)
        let f13 = HotKey(keyCode: kVK_F13, modifiers: 0)
        if Settings.useF13 && main != f13 { hotKeys.register(f13) }

        captureItem.keyEquivalent = main.menuKeyEquivalent
        captureItem.keyEquivalentModifierMask = main.cocoaModifiers
        statusItem.button?.toolTip = "Kıskaç — \(main.display) ile ekran görüntüsü"
        return ok
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

    @objc private func checkForUpdates() {
        updater.check(userInitiated: true)
    }

    @objc private func openSettings() {
        settingsWindow.show()
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
        // Bir uyarı penceresi açıkken seçim ekranı onun üstünde açılıp girdiyi kilitlerdi.
        if let modal = NSApp.modalWindow {
            NSApp.activate(ignoringOtherApps: true)
            modal.makeKeyAndOrderFront(nil)
            return
        }
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
                if Settings.playSound { Self.shutterSound?.play() }
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
