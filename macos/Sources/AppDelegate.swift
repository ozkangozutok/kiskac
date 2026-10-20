import AVFoundation
import AppKit
import Carbon.HIToolbox

// Uygulamanın gövdesi: menü çubuğu simgesi, kısayol, çekimi ve video kaydını başlatma.
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private var statusItem: NSStatusItem!
    private var menu: NSMenu!
    private let hotKeys = HotKeys()
    private var session: OverlaySession?
    private var capturing = false
    private var captureItem: NSMenuItem!
    private var videoItem: NSMenuItem!
    private var fullScreenItem: NSMenuItem!
    private var fullScreenMenu: NSMenu!
    private var loginItem: NSMenuItem!
    private var permissionItem: NSMenuItem!
    private var updateItem: NSMenuItem!
    private let updater = Updater()
    private var recorder: ScreenRecorder?
    private var recBorder: RecordingBorder?
    private var recBar: RecordingBar?
    private var recStarted = false // geri sayım bitti mi
    private var terminating = false
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

        menu = NSMenu()
        menu.delegate = self
        captureItem = NSMenuItem(title: "Ekran görüntüsü al", action: #selector(captureFromMenu), keyEquivalent: "")
        menu.addItem(captureItem)
        videoItem = NSMenuItem(title: "Video kaydet…", action: #selector(videoFromMenu), keyEquivalent: "")
        menu.addItem(videoItem)
        fullScreenItem = NSMenuItem(title: "Tam ekran video", action: nil, keyEquivalent: "")
        fullScreenMenu = NSMenu()
        fullScreenMenu.delegate = self // açılırken ekranlarla doldurulur
        fullScreenItem.submenu = fullScreenMenu
        menu.addItem(fullScreenItem)
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Ekran görüntüsü klasörünü aç", action: #selector(openSaveDir), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "Video klasörünü aç", action: #selector(openVideoDir), keyEquivalent: ""))
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
        updater.canInterrupt = { [weak self] in self?.session == nil && self?.capturing == false && self?.recorder == nil }
        updater.startAutomaticChecks()

        if !applyHotKeys() {
            alert("Kısayol kaydedilemedi",
                  "\(Settings.hotKey.display) başka bir uygulama tarafından kullanılıyor. Ayarlar'dan başka bir kısayol seçebilirsin.")
        }

        checkFolders()

        if !Settings.initialized {
            Settings.initialized = true
            LoginItem.set(true)
            alert("Kıskaç çalışıyor",
                  "\(Settings.hotKey.display) tuşlarına bas ve alanı seç. ⌘C kopyalar, ⌘S kaydeder.\n\nKıskaç menü çubuğunda çalışır; kısayolu ve diğer ayarları oradan değiştirebilirsin. İlk çekimde macOS ekran kaydı izni isteyecek.",
                  style: .informational)
            requestPermission()
        }
    }

    // Açılışta kayıt klasörlerine yazılabildiği denetlenir; izin yoksa önceden uyarılır.
    private func checkFolders() {
        let problems = [Settings.saveDir, Settings.videoDir].compactMap(FolderAccess.problem)
        guard let first = problems.first else { return }
        Toast.show("Klasöre yazma izni yok", first + " Ayarları açmak için tıkla.") {
            NSWorkspace.shared.open(FolderAccess.privacyURL)
        }
    }

    // Ana kısayol + isteğe bağlı F13 (PC klavyelerindeki PrtSc tuşu macOS'ta F13 olarak gelir).
    // Her birinin tam ekran video biçimi (⇧ ya da ⌥ eklenmiş hâli) de kaydedilir.
    // Ana kısayol kaydedildiyse true döner.
    @discardableResult
    private func applyHotKeys() -> Bool {
        hotKeys.unregisterAll()
        let main = Settings.hotKey
        let ok = hotKeys.register(main) { [weak self] in self?.startCapture() }
        let f13 = HotKey(keyCode: kVK_F13, modifiers: 0)
        let useF13 = Settings.useF13 && main != f13
        if useF13 { hotKeys.register(f13) { [weak self] in self?.startCapture() } }
        for v in [main.videoVariant, useF13 ? f13.videoVariant : nil].compactMap({ $0 }) where v != main && v != f13 {
            hotKeys.register(v) { [weak self] in self?.toggleFullScreenRecording() }
        }

        captureItem.keyEquivalent = main.menuKeyEquivalent
        captureItem.keyEquivalentModifierMask = main.cocoaModifiers
        if recorder == nil || !recStarted { statusItem.button?.toolTip = "Kıskaç — \(main.display) ile ekran görüntüsü" }
        return ok
    }

    // Uygulama zaten çalışırken yeniden açılırsa (Finder, Spotlight, Dock): ekran görüntüsü al (kayıt sürüyorsa durdur).
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        startCapture()
        return false
    }

    // Kayıt sürerken çıkılırsa (menü, oturum kapatma) dosya yarım kalmasın: önce kayıt bitirilir.
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        guard recorder != nil else { return .terminateNow }
        if !recStarted {
            cancelRecording()
            return .terminateNow
        }
        terminating = true
        stopRecording()
        DispatchQueue.main.asyncAfter(deadline: .now() + 15) { [weak self] in
            if self?.recorder != nil { NSApp.reply(toApplicationShouldTerminate: true) }
        }
        return .terminateLater
    }

    func menuNeedsUpdate(_ m: NSMenu) {
        if m === fullScreenMenu {
            fillFullScreenMenu()
            return
        }
        loginItem.state = LoginItem.isEnabled ? .on : .off
        permissionItem.isHidden = ScreenCapture.hasPermission
        videoItem.title = recorder != nil ? "Kaydı durdur" : "Video kaydet…"
        let main = Settings.hotKey
        videoItem.keyEquivalent = recorder != nil ? main.menuKeyEquivalent : ""
        videoItem.keyEquivalentModifierMask = main.cocoaModifiers
        fullScreenItem.isHidden = recorder != nil
    }

    // Kayıt sürerken simgeye tıklamak kaydı durdurur; sağ tık menüyü açar.
    func menuDidClose(_ m: NSMenu) {
        guard m === menu, recorder != nil, recStarted else { return }
        DispatchQueue.main.async { [weak self] in
            if self?.recorder != nil && self?.recStarted == true { self?.statusItem.menu = nil }
        }
    }

    // Menü kapanma animasyonu görüntüye girmesin.
    @objc private func captureFromMenu() {
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) { [weak self] in self?.startCapture() }
    }

    @objc private func videoFromMenu() {
        if recorder != nil {
            stopRecording()
            return
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) { [weak self] in self?.startCapture(video: true) }
    }

    @objc private func openSaveDir() {
        NSWorkspace.shared.open(Settings.saveDir)
    }

    @objc private func openVideoDir() {
        NSWorkspace.shared.open(Settings.videoDir)
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

    func startCapture(video: Bool = false) {
        // Bir uyarı penceresi açıkken seçim ekranı onun üstünde açılıp girdiyi kilitlerdi.
        if let modal = NSApp.modalWindow {
            NSApp.activate(ignoringOtherApps: true)
            modal.makeKeyAndOrderFront(nil)
            return
        }
        // Kayıt sürerken kısayol (veya yeniden açma) kaydı durdurur.
        if recorder != nil {
            stopRecording()
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
                if Settings.playSound && !video { Self.shutterSound?.play() }
                let s = OverlaySession(shots: shots, videoHint: video)
                s.onClose = { [weak self] in self?.session = nil }
                s.onRecord = { [weak self] screen, rect, format in
                    self?.startRecording(.region(screen, rect), format)
                }
                s.onOcr = { [weak self] img in self?.copyText(from: img) }
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

    // ---------------------------------------------------------------- metin kopyalama (OCR)

    private func copyText(from image: CGImage) {
        Task { @MainActor in
            let text: String
            do {
                text = try await TextRecognizer.recognize(image)
            } catch {
                Toast.show("Metin okunamadı", error.localizedDescription)
                return
            }
            if text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                Toast.show("Yazı bulunamadı",
                           "Seçili alanda okunabilir bir yazı yok. Daha geniş ya da daha net bir alan seçmeyi dene.")
                return
            }
            let pb = NSPasteboard.general
            pb.clearContents()
            pb.setString(text, forType: .string)
            let lines = text.split(separator: "\n", omittingEmptySubsequences: false).count
            var preview = text.replacingOccurrences(of: "\n", with: " ")
            if preview.count > 120 { preview = String(preview.prefix(117)) + "…" }
            Toast.show("Metin kopyalandı (\(lines) satır)", preview)
        }
    }

    // ---------------------------------------------------------------- video kaydı

    // Tam ekran video kısayolu: imlecin bulunduğu ekran için kayıt çubuğunu açar; çubuk hazırken ▶ gibi başlatır,
    // kayıt sürüyorsa durdurur.
    private func toggleFullScreenRecording() {
        if recorder != nil {
            if !recStarted, let bar = recBar, bar.isReady { bar.play() } else { stopRecording() }
            return
        }
        if session != nil || capturing || NSApp.modalWindow != nil { return } // seçim ekranı açıkken karışmasın
        startRecording(.screen(screenUnderMouse()), .mp4)
    }

    private func screenUnderMouse() -> NSScreen {
        let m = NSEvent.mouseLocation
        return NSScreen.screens.first { NSMouseInRect(m, $0.frame, false) } ?? NSScreen.main ?? NSScreen.screens[0]
    }

    private func fillFullScreenMenu() {
        fullScreenMenu.removeAllItems()
        let current = NSMenuItem(title: "İmlecin bulunduğu ekran", action: #selector(recordCurrentScreen), keyEquivalent: "")
        current.target = self
        if let v = Settings.hotKey.videoVariant {
            current.keyEquivalent = v.menuKeyEquivalent
            current.keyEquivalentModifierMask = v.cocoaModifiers
        }
        fullScreenMenu.addItem(current)

        let screens = NSScreen.screens
        guard screens.count > 1 else { return }
        fullScreenMenu.addItem(.separator())
        for (i, s) in screens.enumerated() {
            let px = s.frame.size.applying(CGAffineTransform(scaleX: s.backingScaleFactor, y: s.backingScaleFactor))
            let item = NSMenuItem(title: "Ekran \(i + 1)  —  \(Int(px.width))×\(Int(px.height))\(i == 0 ? " (ana)" : "")",
                                  action: #selector(recordScreen(_:)), keyEquivalent: "")
            item.target = self
            item.tag = i
            fullScreenMenu.addItem(item)
        }
        let all = RecordingTarget.allScreens()
        let item = NSMenuItem(title: "Tüm ekranlar  —  \(Int(all.size.width * all.scale))×\(Int(all.size.height * all.scale))",
                              action: #selector(recordAllScreens), keyEquivalent: "")
        item.target = self
        fullScreenMenu.addItem(item)
    }

    @objc private func recordCurrentScreen() {
        startRecordingDelayed { [weak self] in .screen(self?.screenUnderMouse() ?? NSScreen.screens[0]) }
    }

    @objc private func recordScreen(_ sender: NSMenuItem) {
        let screens = NSScreen.screens
        guard sender.tag < screens.count else { return }
        let s = screens[sender.tag]
        startRecordingDelayed { .screen(s) }
    }

    @objc private func recordAllScreens() {
        startRecordingDelayed { .allScreens() }
    }

    // Menüden başlatınca menünün kapanma animasyonu kayda girmesin.
    private func startRecordingDelayed(_ target: @escaping () -> RecordingTarget) {
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) { [weak self] in
            self?.startRecording(target(), .mp4)
        }
    }

    private func startRecording(_ target: RecordingTarget, _ format: VideoFormat) {
        guard recorder == nil, !terminating else { return }
        if !requestPermission() { return }
        let gif = format == .gif
        // Video klasörüne yazılamıyorsa kayıt kaybolmasın: Filmler › Kıskaç'a yazılır.
        let (usable, folderWarning) = FolderAccess.usable(Settings.videoDir, fallback: Settings.defaultVideoDir)
        guard let dir = usable else {
            Toast.show("Video kaydedilemez", (folderWarning ?? "Video klasörüne yazılamıyor.") + " Ayarları açmak için tıkla.") {
                NSWorkspace.shared.open(FolderAccess.privacyURL)
            }
            return
        }
        if let w = folderWarning {
            Toast.show("Video klasörüne yazılamıyor", w + " Bu kayıt Filmler › Kıskaç klasörüne kaydedilecek.") {
                NSWorkspace.shared.open(FolderAccess.privacyURL)
            }
        }
        let ext = gif ? "gif" : "mp4"
        let base = Output.baseName()
        var url = dir.appendingPathComponent(base + "." + ext)
        var n = 2
        while FileManager.default.fileExists(atPath: url.path) {
            url = dir.appendingPathComponent("\(base) (\(n)).\(ext)")
            n += 1
        }

        let options = RecordingOptions(target: target, format: format, fps: gif ? Settings.gifFps : Settings.videoFps,
                                       cursor: Settings.recordCursor, audio: gif ? [] : Settings.recordAudio,
                                       systemVolume: Settings.systemVolume, micVolume: Settings.micVolume, url: url)
        let rec = ScreenRecorder(options: options)
        rec.finished = { [weak self, weak rec] url, error, warning in
            guard let self, let rec else { return }
            self.recordingFinished(rec, url, error, warning)
        }

        // Bir ekranın tamamı kaydediliyorsa çerçeve gereksiz.
        let border = target.wholeScreen ? nil : RecordingBorder(region: target.frame)
        let bar = RecordingBar(recorder: rec, stopHint: Settings.hotKey.display)
        bar.countdownSeconds = Settings.recordCountdown ? 3 : 0
        bar.onPause = { [weak self, weak rec] in
            guard let rec else { return }
            rec.isPaused.toggle()
            self?.recBorder?.paused = rec.isPaused
        }
        bar.onStop = { [weak self] in self?.stopRecording() }
        bar.onAudioChanged = { [weak rec] mode in
            rec?.setAudio(mode)
            Settings.recordAudio = mode // son seçim hatırlanır
        }
        bar.onVolumeChanged = { [weak rec] source, percent in
            rec?.setVolume(source, percent)
            if source == .system { Settings.systemVolume = percent } else { Settings.micVolume = percent }
        }
        bar.onMicrophoneEnabled = { [weak self] in self?.requestMicrophone() }
        // Geri sayım bitince (ya da "şimdi başla") kayıt gerçekten başlar.
        bar.onBegin = { [weak self, weak rec] in
            guard let self, let rec, self.recorder === rec else { return }
            self.recStarted = true
            self.recBorder?.paused = false
            self.setRecordingIndicator(true)
            rec.start()
        }

        recorder = rec
        recBorder = border
        recBar = bar
        recStarted = false
        border?.paused = true // hazırken ve geri sayımda sarı
        border?.show()
        bar.show() // çubuk "Hazır" açılır; kayıt ▶'ye basılınca (geri sayımdan sonra) başlar
        if options.audio.contains(.microphone) { requestMicrophone() }
    }

    // Mikrofon izni kayıt başlamadan istenir; sistem penceresi kayda girmesin.
    private func requestMicrophone() {
        switch AVCaptureDevice.authorizationStatus(for: .audio) {
        case .notDetermined:
            AVCaptureDevice.requestAccess(for: .audio) { _ in }
        case .denied, .restricted:
            Toast.show("Mikrofon izni yok",
                       "Sistem Ayarları › Gizlilik ve Güvenlik › Mikrofon bölümünden Kıskaç'ı aç. Ayarları açmak için tıkla.") {
                if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone") {
                    NSWorkspace.shared.open(url)
                }
            }
        default:
            break
        }
    }

    private func stopRecording() {
        guard let rec = recorder else { return }
        if !recStarted {
            cancelRecording() // hazırken ya da geri sayımda: iptal, dosya oluşmaz
            return
        }
        rec.stop()
        recBorder?.hide()
        recBar?.showStopping()
    }

    private func cancelRecording() {
        closeRecordingUi()
        recorder = nil
    }

    private func closeRecordingUi() {
        recBorder?.close()
        recBorder = nil
        recBar?.close()
        recBar = nil
        setRecordingIndicator(false)
    }

    private func recordingFinished(_ rec: ScreenRecorder, _ url: URL, _ error: Error?, _ warning: String?) {
        guard rec === recorder else { return }
        recorder = nil
        recStarted = false
        closeRecordingUi()
        if terminating {
            NSApp.reply(toApplicationShouldTerminate: true)
            return
        }
        if let error {
            if FolderAccess.isPermissionError(error) {
                Toast.show("Kayıt başarısız: yazma izni yok",
                           FolderAccess.message(EPERM, url.deletingLastPathComponent()) + " Ayarları açmak için tıkla.") {
                    NSWorkspace.shared.open(FolderAccess.privacyURL)
                }
            } else {
                Toast.show("Kayıt başarısız", error.localizedDescription)
            }
            return
        }
        var text = "\(url.lastPathComponent) kaydedildi. Açmak için tıkla."
        if let warning { text += "\n" + warning }
        // Tıklayınca dosya varsayılan uygulamada (MP4: QuickTime, GIF: Önizleme) açılır.
        Toast.show(rec.options.format == .gif ? "GIF kaydedildi" : "Video kaydedildi", text) {
            NSWorkspace.shared.open(url)
        }
    }

    // Kayıt sürerken menü çubuğunda kırmızı durdurma simgesi; tıklamak kaydı durdurur.
    private func setRecordingIndicator(_ on: Bool) {
        guard let b = statusItem.button else { return }
        if on {
            let cfg = NSImage.SymbolConfiguration(pointSize: 15, weight: .regular)
                .applying(NSImage.SymbolConfiguration(paletteColors: [.systemRed]))
            b.image = NSImage(systemSymbolName: "stop.circle.fill", accessibilityDescription: "Kaydı durdur")?
                .withSymbolConfiguration(cfg)
            b.toolTip = "Kıskaç — kaydediliyor (\(Settings.hotKey.display): durdur)"
            statusItem.menu = nil
            b.target = self
            b.action = #selector(statusClicked)
            b.sendAction(on: [.leftMouseUp, .rightMouseUp])
        } else {
            b.image = Logo.menuBarImage()
            b.toolTip = "Kıskaç — \(Settings.hotKey.display) ile ekran görüntüsü"
            b.action = nil
            statusItem.menu = menu
        }
    }

    @objc private func statusClicked() {
        if NSApp.currentEvent?.type == .rightMouseUp {
            statusItem.menu = menu
            statusItem.button?.performClick(nil)
        } else {
            stopRecording()
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
