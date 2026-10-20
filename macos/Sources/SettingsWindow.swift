import AppKit
import Carbon.HIToolbox
import SwiftUI

// Ayarlar penceresinin durumu: kısayol kaydı, başlangıç öğesi, kayıt ve video klasörü.
// Diğer ayarlar doğrudan @AppStorage ile UserDefaults'a yazılır.
@MainActor
final class SettingsModel: ObservableObject {
    @Published var hotKey = Settings.hotKey
    @Published var hotKeyMessage: String?
    @Published var recording = false
    @Published var loginEnabled = LoginItem.isEnabled
    @Published var saveDir = Settings.saveDir
    @Published var videoDir = Settings.videoDir

    // AppDelegate bağlar: kısayolları yeniden kaydeder, ana kısayol kaydedildiyse true döner.
    var applyHotKeys: () -> Bool = { true }
    var pauseHotKeys: () -> Void = {}
    var checkForUpdates: () -> Void = {}
    private var monitor: Any?

    func refresh() {
        hotKey = Settings.hotKey
        loginEnabled = LoginItem.isEnabled
        saveDir = Settings.saveDir
        videoDir = Settings.videoDir
        hotKeyMessage = nil
    }

    func toggleRecording() {
        recording ? stopRecording() : startRecording()
    }

    // Kayıt sırasında genel kısayollar susturulur; yoksa mevcut kısayola basmak çekim başlatır.
    private func startRecording() {
        pauseHotKeys()
        recording = true
        hotKeyMessage = nil
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] e in
            MainActor.assumeIsolated { self?.handle(e) }
            return nil
        }
    }

    func stopRecording() {
        guard recording else { return }
        if let m = monitor { NSEvent.removeMonitor(m) }
        monitor = nil
        recording = false
        _ = applyHotKeys()
    }

    private func handle(_ e: NSEvent) {
        let k = HotKey(event: e)
        if k.modifiers == 0 && (k.keyCode == kVK_Escape || k.keyCode == kVK_Delete) {
            stopRecording()
            return
        }
        if let p = k.problem {
            hotKeyMessage = p
            return
        }
        set(k)
    }

    func set(_ k: HotKey) {
        let old = Settings.hotKey
        Settings.hotKey = k
        hotKey = k
        if let m = monitor { NSEvent.removeMonitor(m) }
        monitor = nil
        recording = false
        if applyHotKeys() {
            hotKeyMessage = nil
        } else {
            Settings.hotKey = old
            hotKey = old
            _ = applyHotKeys()
            hotKeyMessage = "\(k.display) kaydedilemedi; macOS veya başka bir uygulama kullanıyor olabilir."
        }
    }

    func chooseSaveDir() {
        let p = NSOpenPanel()
        p.title = "Kayıt klasörünü seç"
        p.prompt = "Seç"
        p.canChooseDirectories = true
        p.canChooseFiles = false
        p.canCreateDirectories = true
        p.directoryURL = saveDir
        if p.runModal() == .OK, let url = p.url {
            Settings.saveDir = url
            saveDir = url
        }
    }

    func chooseVideoDir() {
        let p = NSOpenPanel()
        p.title = "Video klasörünü seç"
        p.prompt = "Seç"
        p.canChooseDirectories = true
        p.canChooseFiles = false
        p.canCreateDirectories = true
        p.directoryURL = videoDir
        if p.runModal() == .OK, let url = p.url {
            Settings.videoDir = url
            videoDir = url
        }
    }
}

struct SettingsView: View {
    @ObservedObject var model: SettingsModel
    @AppStorage(Settings.Key.useF13) private var useF13 = true
    @AppStorage(Settings.Key.playSound) private var playSound = false
    @AppStorage(Settings.Key.dimOpacity) private var dimOpacity = 0.45
    @AppStorage(Settings.Key.showHint) private var showHint = true
    @AppStorage(Settings.Key.rememberStyle) private var rememberStyle = true
    @AppStorage(Settings.Key.format) private var format = "png"
    @AppStorage(Settings.Key.jpegQuality) private var jpegQuality = 0.92
    @AppStorage(Settings.Key.quickSave) private var quickSave = false
    @AppStorage(Settings.Key.copyAfterSave) private var copyAfterSave = false
    @AppStorage(Settings.Key.downscale) private var downscale = false
    @AppStorage(Settings.Key.autoUpdate) private var autoUpdate = true
    @AppStorage(Settings.Key.recordAudio) private var recordAudio = AudioMode.system.rawValue
    @AppStorage(Settings.Key.systemVolume) private var systemVolume = 100
    @AppStorage(Settings.Key.micVolume) private var micVolume = 100
    @AppStorage(Settings.Key.videoFps) private var videoFps = 30
    @AppStorage(Settings.Key.gifFps) private var gifFps = 10
    @AppStorage(Settings.Key.recordCursor) private var recordCursor = true
    @AppStorage(Settings.Key.recordCountdown) private var recordCountdown = true

    private let defaultKey = HotKey(keyCode: kVK_ANSI_2, modifiers: cmdKey | shiftKey)

    var body: some View {
        Form {
            Section("Kısayol") {
                LabeledContent("Ekran görüntüsü al") {
                    HStack(spacing: 8) {
                        if model.hotKey != defaultKey && !model.recording {
                            Button("Varsayılan") { model.set(defaultKey) }
                                .buttonStyle(.link)
                        }
                        Button(action: model.toggleRecording) {
                            Text(model.recording ? "Kısayola bas…" : model.hotKey.display)
                                .font(.system(.body, design: .rounded).weight(.medium))
                                .frame(minWidth: 110)
                        }
                        .controlSize(.large)
                        .tint(model.recording ? .accentColor : nil)
                        .buttonStyle(.bordered)
                    }
                }
                if let m = model.hotKeyMessage {
                    Text(m).font(.callout).foregroundStyle(.red)
                } else if model.recording {
                    Text("Yeni kısayola bas. Vazgeçmek için Esc.").font(.callout).foregroundStyle(.secondary)
                }
                Toggle("PC klavyesindeki PrtSc tuşu da çalışsın (F13)", isOn: $useF13)
                    .onChange(of: useF13) { _ = model.applyHotKeys() }
            }

            Section("Genel") {
                Toggle("Oturum açılınca başlat", isOn: Binding(
                    get: { model.loginEnabled },
                    set: { LoginItem.set($0); model.loginEnabled = LoginItem.isEnabled }))
                Toggle("Çekimde deklanşör sesi çal", isOn: $playSound)
            }

            Section("Seçim ekranı") {
                LabeledContent("Karartma") {
                    HStack {
                        Slider(value: $dimOpacity, in: 0.15...0.75).frame(width: 180)
                        Text("%\(Int((dimOpacity * 100).rounded()))").monospacedDigit().frame(width: 40, alignment: .trailing)
                    }
                }
                Toggle("Başta ipucu metnini göster", isOn: $showHint)
                Toggle("Son kullanılan renk ve kalınlığı hatırla", isOn: $rememberStyle)
            }

            Section("Kaydetme ve kopyalama") {
                LabeledContent("Kayıt klasörü") {
                    HStack {
                        Text(model.saveDir.path.replacingOccurrences(of: NSHomeDirectory(), with: "~"))
                            .lineLimit(1).truncationMode(.middle).foregroundStyle(.secondary)
                        Button("Değiştir…") { model.chooseSaveDir() }
                    }
                }
                Picker("Biçim", selection: $format) {
                    Text("PNG").tag("png")
                    Text("JPEG").tag("jpeg")
                }
                if format == "jpeg" {
                    LabeledContent("JPEG kalitesi") {
                        HStack {
                            Slider(value: $jpegQuality, in: 0.5...1).frame(width: 180)
                            Text("%\(Int((jpegQuality * 100).rounded()))").monospacedDigit().frame(width: 40, alignment: .trailing)
                        }
                    }
                }
                Toggle("⌘S sormadan doğrudan kayıt klasörüne kaydetsin", isOn: $quickSave)
                Toggle("Kaydettikten sonra panoya da kopyala", isOn: $copyAfterSave)
                VStack(alignment: .leading, spacing: 2) {
                    Toggle("Retina görüntüleri 1x boyutuna küçült", isOn: $downscale)
                    Text("Dosya küçülür; görüntü ekranda göründüğü nokta boyutunda olur.")
                        .font(.caption).foregroundStyle(.secondary)
                }
            }

            Section("Video kaydı (seçim ekranında ● veya GIF)") {
                LabeledContent("Video klasörü") {
                    HStack {
                        Text(model.videoDir.path.replacingOccurrences(of: NSHomeDirectory(), with: "~"))
                            .lineLimit(1).truncationMode(.middle).foregroundStyle(.secondary)
                        Button("Değiştir…") { model.chooseVideoDir() }
                    }
                }
                Picker("Ses (MP4)", selection: $recordAudio) {
                    Text("Ses yok").tag(0)
                    Text("Sistem sesi").tag(AudioMode.system.rawValue)
                    Text("Mikrofon").tag(AudioMode.microphone.rawValue)
                    Text("Sistem sesi + mikrofon").tag(AudioMode([.system, .microphone]).rawValue)
                }
                volumeRow("Sistem sesi seviyesi", $systemVolume)
                volumeRow("Mikrofon seviyesi", $micVolume)
                Picker("MP4 kare hızı", selection: $videoFps) {
                    ForEach(Settings.videoRates, id: \.self) { Text("\($0) kare/sn").tag($0) }
                }
                Picker("GIF kare hızı", selection: $gifFps) {
                    ForEach(Settings.gifRates, id: \.self) { Text("\($0) kare/sn").tag($0) }
                }
                Toggle("Fare imlecini kayda dahil et", isOn: $recordCursor)
                Toggle("▶ Başlat'a bastıktan sonra 3 saniye geri say", isOn: $recordCountdown)
                Text(videoNote).font(.caption).foregroundStyle(.secondary)
            }

            Section("Güncelleme") {
                LabeledContent("Sürüm") {
                    HStack {
                        Text(Updater.currentVersion).foregroundStyle(.secondary)
                        Button("Şimdi denetle") { model.checkForUpdates() }
                    }
                }
                Toggle("Güncellemeleri otomatik denetle", isOn: $autoUpdate)
            }
        }
        .formStyle(.grouped)
        // Form içerik yüksekliğini aşınca kendisi kayar; pencere küçük ekranlara da sığar.
        .frame(width: 520, height: Self.height)
    }

    private static var height: CGFloat {
        min(860, (NSScreen.main?.visibleFrame.height ?? 900) - 80)
    }

    private var videoNote: String {
        let main = model.hotKey.display
        let start = model.hotKey.videoVariant.map { "\($0.display) imlecin bulunduğu ekranın kayıt çubuğunu açar, yeniden basınca kayıt başlar. " }
            ?? "Tam ekran video menü çubuğundaki menüden başlatılır. "
        return start + "Kaydı durdurmak için kontrol çubuğundaki ■ düğmesine, menü çubuğundaki kırmızı simgeye ya da yeniden \(main) kısayoluna bas."
    }

    private func volumeRow(_ title: String, _ value: Binding<Int>) -> some View {
        LabeledContent(title) {
            HStack {
                Slider(value: Binding(get: { Double(value.wrappedValue) }, set: { value.wrappedValue = Int($0.rounded()) }),
                       in: 0...200, step: 10).frame(width: 180)
                Text("%\(value.wrappedValue)").monospacedDigit().frame(width: 44, alignment: .trailing)
            }
        }
    }
}

@MainActor
final class SettingsWindowController: NSObject, NSWindowDelegate {
    let model = SettingsModel()
    private var window: NSWindow?

    func show() {
        model.refresh()
        if window == nil {
            let host = NSHostingController(rootView: SettingsView(model: model))
            host.sizingOptions = .preferredContentSize
            let w = SettingsPanelWindow(contentViewController: host)
            w.title = "Kıskaç Ayarları"
            w.styleMask = [.titled, .closable]
            w.isReleasedWhenClosed = false
            w.delegate = self
            w.center()
            window = w
        }
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }

    func windowWillClose(_ notification: Notification) {
        model.stopRecording()
    }

    // Başka uygulamaya geçilirse kayıt biter; yoksa genel kısayollar kapalı kalırdı.
    func windowDidResignKey(_ notification: Notification) {
        model.stopRecording()
    }
}

// Uygulamanın ana menüsü olmadığı için ⌘W burada karşılanır.
final class SettingsPanelWindow: NSWindow {
    override func performKeyEquivalent(with e: NSEvent) -> Bool {
        if e.modifierFlags.intersection(.deviceIndependentFlagsMask) == .command,
           e.charactersIgnoringModifiers?.lowercased() == "w" {
            performClose(nil)
            return true
        }
        return super.performKeyEquivalent(with: e)
    }
}
