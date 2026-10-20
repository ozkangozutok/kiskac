import AVFoundation
import AppKit

private let uiLevel = NSWindow.Level(rawValue: NSWindow.Level.statusBar.rawValue + 1)
private let uiBehavior: NSWindow.CollectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
private let red = NSColor(srgbRed: 235 / 255, green: 45 / 255, blue: 45 / 255, alpha: 1)
private let yellow = NSColor(srgbRed: 1, green: 190 / 255, blue: 0, alpha: 1)
private let green = NSColor(srgbRed: 70 / 255, green: 200 / 255, blue: 110 / 255, alpha: 1)
private let orange = NSColor(srgbRed: 1, green: 150 / 255, blue: 40 / 255, alpha: 1)
private let gray = NSColor(srgbRed: 140 / 255, green: 140 / 255, blue: 145 / 255, alpha: 1)
private let barColor = NSColor(srgbRed: 32 / 255, green: 32 / 255, blue: 36 / 255, alpha: 1)

// Kayıt alanının dışında kalan ince çerçeve. Tıklamalar alttaki pencerelere geçer; Kıskaç'ın pencereleri
// kayda girmediği için çerçeve de görüntüde görünmez.
@MainActor
final class RecordingBorder {
    static let thickness: CGFloat = 3
    private let window: NSWindow
    private let view: BorderView

    init(region: CGRect) {
        window = NSWindow(contentRect: region.insetBy(dx: -Self.thickness, dy: -Self.thickness),
                          styleMask: .borderless, backing: .buffered, defer: false)
        view = BorderView(frame: CGRect(origin: .zero, size: window.frame.size))
        window.contentView = view
        window.isOpaque = false
        window.backgroundColor = .clear
        window.hasShadow = false
        window.ignoresMouseEvents = true
        window.isReleasedWhenClosed = false
        window.level = uiLevel
        window.collectionBehavior = uiBehavior
        window.sharingType = .none
    }

    // Sarı: hazır, geri sayım ya da duraklatma; kırmızı: kayıt
    var paused = true {
        didSet { view.color = paused ? yellow : red }
    }

    func show() { window.orderFrontRegardless() }
    func hide() { window.orderOut(nil) }
    func close() { window.close() }

    private final class BorderView: NSView {
        var color = yellow { didSet { needsDisplay = true } }

        override func draw(_ dirtyRect: NSRect) {
            color.set()
            let t = RecordingBorder.thickness
            let p = NSBezierPath(rect: bounds)
            p.appendRect(bounds.insetBy(dx: t, dy: t))
            p.windingRule = .evenOdd
            p.fill()
        }
    }
}

// Kayıt çubuğu. Önce "Hazır" bekler (ses ayarlanır, ▶ ile başlatılır ya da iptal edilir), ▶ sonrası geri sayar,
// sonra süreyi, ses düğmelerini, duraklat ve durdur düğmelerini gösterir. Odak çalmaz, boş yerinden sürüklenir.
@MainActor
final class RecordingBar {
    enum State { case ready, countdown, recording, stopping }

    private let panel: NSPanel
    private let view: BarView
    private let tip = TipWindow()

    // Geri sayım bitti ya da "şimdi başla"ya basıldı
    var onBegin: (() -> Void)?
    var onPause: (() -> Void)?
    // Kayıtta: durdur; hazırken ve geri sayımda: iptal
    var onStop: (() -> Void)?
    var onAudioChanged: ((AudioMode) -> Void)?
    // (kaynak, yüzde): simgenin üzerinde kaydırmayla
    var onVolumeChanged: ((AudioMode, Int) -> Void)?
    // Mikrofon açılınca izin istemek için
    var onMicrophoneEnabled: (() -> Void)?

    // ▶'ye basılınca kaç saniye geri sayılacağı (0: hemen başlar)
    var countdownSeconds = 3

    init(recorder: ScreenRecorder, stopHint: String) {
        let o = recorder.options
        let hasAudio = o.format == .mp4
        let size = CGSize(width: hasAudio ? 300 : 236, height: 40)
        panel = NSPanel(contentRect: CGRect(origin: .zero, size: size),
                        styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        view = BarView(frame: CGRect(origin: .zero, size: size), recorder: recorder, hasAudio: hasAudio, stopHint: stopHint)
        panel.contentView = view
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.isFloatingPanel = true
        panel.becomesKeyOnlyIfNeeded = true
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.level = uiLevel
        panel.collectionBehavior = uiBehavior
        panel.sharingType = .none
        view.bar = self
        place(o.target.frame, size)
    }

    var isReady: Bool { view.state == .ready }

    func show() {
        panel.orderFrontRegardless()
        view.startTimer()
    }

    // ▶: geri sayımı başlatır (kısayolun tam ekran biçimi de bunu çağırır)
    func play() { view.play() }

    func showStopping() {
        view.state = .stopping
        tip.hide()
        view.needsDisplay = true
    }

    func close() {
        view.stopTimer()
        tip.hide()
        panel.close()
    }

    fileprivate func showTip(_ text: String?) {
        guard let text else { return tip.hide() }
        tip.show(text, below: panel.frame)
    }

    // Kayıt alanının altına, yer yoksa üstüne, o da yoksa alanın içine (kayda girmez) yerleşir.
    private func place(_ region: CGRect, _ size: CGSize) {
        let center = CGPoint(x: region.midX, y: region.midY)
        let screen = NSScreen.screens.first { $0.frame.contains(center) } ?? NSScreen.main ?? NSScreen.screens[0]
        let mon = screen.visibleFrame
        let gap: CGFloat = 8
        var x = region.midX - size.width / 2
        var y = region.minY - gap - size.height
        if y < mon.minY {
            y = region.maxY + gap
            if y + size.height > mon.maxY { y = region.minY + gap * 3 }
        }
        x = max(mon.minX + 4, min(x, mon.maxX - size.width - 4))
        y = max(mon.minY + 4, min(y, mon.maxY - size.height - 4))
        panel.setFrameOrigin(CGPoint(x: x.rounded(), y: y.rounded()))
    }
}

private final class BarView: NSView {
    enum Btn { case none, system, mic, pause, stop }

    weak var bar: RecordingBar?
    private let recorder: ScreenRecorder
    private let hasAudio: Bool
    private let stopHint: String
    var state = RecordingBar.State.ready
    private var countdownEnd = Date()
    private var timer: Timer?
    private var tick = 0
    private var blink = false
    private var hover = Btn.none
    private var audio: AudioMode
    private var sysVolume: Int
    private var micVolume: Int
    private var flash: (String, String)? // seviye değişince kısa süre sürenin yerine gösterilir
    private var flashUntil = Date()
    private var scrollAccum: CGFloat = 0
    private var symbols: [String: NSImage] = [:]

    private var sysRect = CGRect.zero, micRect = CGRect.zero, pauseRect = CGRect.zero, stopRect = CGRect.zero

    init(frame: CGRect, recorder: ScreenRecorder, hasAudio: Bool, stopHint: String) {
        self.recorder = recorder
        self.hasAudio = hasAudio
        self.stopHint = stopHint
        audio = recorder.options.audio
        sysVolume = recorder.options.systemVolume
        micVolume = recorder.options.micVolume
        super.init(frame: frame)
        let b: CGFloat = 30, top = (frame.height - b) / 2
        stopRect = CGRect(x: frame.width - 6 - b, y: top, width: b, height: b)
        pauseRect = CGRect(x: stopRect.minX - 2 - b, y: top, width: b, height: b)
        if hasAudio {
            micRect = CGRect(x: pauseRect.minX - 12 - b, y: top, width: b, height: b)
            sysRect = CGRect(x: micRect.minX - 2 - b, y: top, width: b, height: b)
        }
        addTrackingArea(NSTrackingArea(rect: .zero, options: [.mouseMoved, .mouseEnteredAndExited, .activeAlways, .inVisibleRect],
                                       owner: self, userInfo: nil))
    }

    required init?(coder: NSCoder) { fatalError() }

    override var isFlipped: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    func startTimer() {
        timer = Timer.scheduledTimer(withTimeInterval: 0.1, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.onTick() }
        }
    }

    func stopTimer() {
        timer?.invalidate()
        timer = nil
    }

    private func onTick() {
        if state == .countdown && Date() >= countdownEnd { beginNow() }
        tick += 1
        if tick % 3 == 0 { blink.toggle() }
        needsDisplay = true
    }

    func play() {
        guard state == .ready, let bar else { return }
        state = .countdown
        countdownEnd = Date().addingTimeInterval(TimeInterval(bar.countdownSeconds))
        if bar.countdownSeconds <= 0 { beginNow() }
        refreshTip()
        needsDisplay = true
    }

    private func beginNow() {
        guard state == .countdown else { return }
        state = .recording
        refreshTip()
        needsDisplay = true
        bar?.onBegin?()
    }

    private func button(at p: CGPoint) -> Btn {
        if state == .stopping { return .none }
        if hasAudio && sysRect.contains(p) { return .system }
        if hasAudio && micRect.contains(p) { return .mic }
        if pauseRect.contains(p) { return .pause }
        if stopRect.contains(p) { return .stop }
        return .none
    }

    private var micBlocked: Bool {
        if recorder.failedAudio.contains(.microphone) { return true }
        let s = AVCaptureDevice.authorizationStatus(for: .audio)
        return s == .denied || s == .restricted
    }

    // İpuçları da Kıskaç penceresi olduğu için kayda girmez; her durumda gösterilebilir.
    private func tipFor(_ b: Btn) -> String? {
        let wheel = "  •  tıkla: aç/kapat, kaydır: seviye"
        switch b {
        case .system:
            return (audio.contains(.system) ? "Sistem sesi açık, %\(sysVolume)" : "Sistem sesi kapalı") + wheel
        case .mic:
            if audio.contains(.microphone) && micBlocked {
                return "Mikrofon izni yok: Sistem Ayarları › Gizlilik ve Güvenlik › Mikrofon"
            }
            return (audio.contains(.microphone) ? "Mikrofon açık, %\(micVolume)" : "Mikrofon kapalı") + wheel
        case .pause:
            switch state {
            case .ready:
                let n = bar?.countdownSeconds ?? 0
                return n > 0 ? "Başlat (\(n) sn geri sayar)" : "Başlat"
            case .countdown: return "Şimdi başla"
            default: return recorder.paused ? "Devam et" : "Duraklat"
            }
        case .stop:
            return state == .recording ? "Durdur ve kaydet  •  \(stopHint)" : "İptal"
        case .none:
            return nil
        }
    }

    private func refreshTip() {
        bar?.showTip(state == .stopping ? nil : tipFor(hover))
    }

    // ---------------------------------------------------------------- çizim

    private let timeFont = NSFont.systemFont(ofSize: 14, weight: .semibold)
    private let smallFont = NSFont.systemFont(ofSize: 11)

    private func symbol(_ name: String, _ color: NSColor) -> NSImage? {
        let key = name + color.description
        if let s = symbols[key] { return s }
        let cfg = NSImage.SymbolConfiguration(pointSize: 14, weight: .semibold)
            .applying(NSImage.SymbolConfiguration(paletteColors: [color]))
        let img = NSImage(systemSymbolName: name, accessibilityDescription: nil)?.withSymbolConfiguration(cfg)
        symbols[key] = img
        return img
    }

    private func drawSymbol(_ name: String, _ color: NSColor, in r: CGRect) {
        guard let img = symbol(name, color) else { return }
        let s = img.size
        img.draw(in: CGRect(x: (r.midX - s.width / 2).rounded(), y: (r.midY - s.height / 2).rounded(), width: s.width, height: s.height),
                 from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true, hints: nil)
    }

    @discardableResult
    private func drawText(_ s: String, _ font: NSFont, _ color: NSColor, x: CGFloat) -> CGFloat {
        let attrs: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: color]
        let sz = (s as NSString).size(withAttributes: attrs)
        (s as NSString).draw(at: CGPoint(x: x, y: ((bounds.height - sz.height) / 2).rounded()), withAttributes: attrs)
        return x + ceil(sz.width)
    }

    override func draw(_ dirtyRect: NSRect) {
        let bg = NSBezierPath(roundedRect: bounds, xRadius: 9, yRadius: 9)
        barColor.set()
        bg.fill()
        NSColor(white: 1, alpha: 0.18).set()
        let edge = NSBezierPath(roundedRect: bounds.insetBy(dx: 0.5, dy: 0.5), xRadius: 8.5, yRadius: 8.5)
        edge.lineWidth = 1
        edge.stroke()

        let paused = recorder.paused
        // durum noktası
        let d: CGFloat = 12
        let dot = CGRect(x: 14, y: (bounds.height - d) / 2, width: d, height: d)
        let dotColor: NSColor = state == .stopping ? .gray
            : state != .recording || paused ? yellow
            : blink ? red : NSColor(srgbRed: 150 / 255, green: 40 / 255, blue: 40 / 255, alpha: 1)
        dotColor.set()
        NSBezierPath(ovalIn: dot).fill()

        let tx = dot.maxX + 10
        let fmt = hasAudio ? "MP4" : "GIF"
        if state == .stopping {
            drawText("Kaydediliyor…", smallFont, .white, x: tx)
            return
        }
        if let f = flash, Date() < flashUntil {
            let end = drawText(f.0, timeFont, .white, x: tx)
            drawText(f.1, smallFont, gray, x: end + 4)
        } else {
            switch state {
            case .ready:
                let end = drawText("Hazır", timeFont, .white, x: tx)
                drawText(fmt, smallFont, gray, x: end + 4)
            case .countdown:
                let left = max(1, Int(ceil(countdownEnd.timeIntervalSinceNow)))
                drawText("Başlıyor  \(left)", timeFont, .white, x: tx)
            default:
                let t = Int(recorder.elapsed)
                let end = drawText(String(format: "%02d:%02d", t / 60, t % 60), timeFont, .white, x: tx)
                drawText(fmt, smallFont, gray, x: end + 4)
            }
        }

        // vurgu
        let hr: CGRect? = hover == .system ? sysRect : hover == .mic ? micRect : hover == .pause ? pauseRect
            : hover == .stop ? stopRect : nil
        if let hr {
            NSColor(white: 1, alpha: 60 / 255).set()
            NSBezierPath(roundedRect: hr, xRadius: 6, yRadius: 6).fill()
        }

        // ses düğmeleri: açıkken beyaz simge, kapalıyken üstü çizili gri; açılamadıysa turuncu
        if hasAudio {
            let failed = recorder.failedAudio
            let sysOn = audio.contains(.system), micOn = audio.contains(.microphone)
            drawSymbol(sysOn ? "speaker.wave.2.fill" : "speaker.slash.fill",
                       sysOn ? (failed.contains(.system) ? orange : .white) : gray, in: sysRect.offsetBy(dx: -2, dy: 0))
            drawSymbol(micOn ? "mic.fill" : "mic.slash.fill", micOn ? (micBlocked ? orange : .white) : gray,
                       in: micRect.offsetBy(dx: -2, dy: 0))
            if sysOn { drawMeter(sysRect, recorder.level(of: .system)) }
            if micOn { drawMeter(micRect, recorder.level(of: .microphone)) }
            NSColor(white: 1, alpha: 60 / 255).set()
            CGRect(x: pauseRect.minX - 7, y: 10, width: 1, height: bounds.height - 20).fill()
        }

        // başlat/duraklat ve durdur/iptal
        if state == .ready || state == .countdown {
            if state == .ready {
                // ▶ Başlat: yeşil üçgen
                let cx = pauseRect.midX + 1, cy = pauseRect.midY
                let tri = NSBezierPath()
                tri.move(to: CGPoint(x: cx - 5, y: cy - 7))
                tri.line(to: CGPoint(x: cx - 5, y: cy + 7))
                tri.line(to: CGPoint(x: cx + 7, y: cy))
                tri.close()
                green.set()
                tri.fill()
            } else {
                red.set()
                NSBezierPath(ovalIn: CGRect(x: pauseRect.midX - 6, y: pauseRect.midY - 6, width: 12, height: 12)).fill()
            }
            drawSymbol("xmark", .white, in: stopRect)
        } else {
            drawSymbol(paused ? "play.fill" : "pause.fill", .white, in: pauseRect)
            red.set()
            NSBezierPath(roundedRect: CGRect(x: stopRect.midX - 6, y: stopRect.midY - 6, width: 12, height: 12),
                         xRadius: 2, yRadius: 2).fill()
        }
    }

    // Simgenin sağında ince dikey seviye göstergesi: kaynaktan ses gelip gelmediği görülsün.
    private func drawMeter(_ r: CGRect, _ level: Float) {
        let full: CGFloat = 18, w: CGFloat = 3
        let x = r.maxX - w - 1, bottom = r.midY + full / 2
        NSColor(white: 1, alpha: 50 / 255).set()
        CGRect(x: x, y: bottom - full, width: w, height: full).fill()
        let h = (full * CGFloat(sqrt(max(0, min(1, level))))).rounded()
        guard h > 0 else { return }
        (level > 0.95 ? NSColor(srgbRed: 235 / 255, green: 70 / 255, blue: 60 / 255, alpha: 1) : level > 0.6 ? yellow : green).set()
        CGRect(x: x, y: bottom - h, width: w, height: h).fill()
    }

    // ---------------------------------------------------------------- fare

    override func scrollWheel(with e: NSEvent) {
        let b = button(at: convert(e.locationInWindow, from: nil))
        guard b == .system || b == .mic else { return }
        // İzleme dörtgeni çok sayıda küçük adım gönderir; biriktirip tek adıma çevrilir.
        var d = e.scrollingDeltaY
        if e.hasPreciseScrollingDeltas {
            scrollAccum += d
            if abs(scrollAccum) < 14 { return }
            d = scrollAccum
            scrollAccum = 0
        }
        if d == 0 { return }
        let step = d > 0 ? 10 : -10
        let sys = b == .system
        let v = max(0, min(200, (sys ? sysVolume : micVolume) + step))
        if sys { sysVolume = v } else { micVolume = v }
        flash = ("%\(v)", sys ? "sistem sesi" : "mikrofon")
        flashUntil = Date().addingTimeInterval(1.5)
        bar?.onVolumeChanged?(sys ? .system : .microphone, v)
        refreshTip()
        needsDisplay = true
    }

    override func mouseMoved(with e: NSEvent) {
        let h = button(at: convert(e.locationInWindow, from: nil))
        (h != .none ? NSCursor.pointingHand : NSCursor.openHand).set()
        guard h != hover else { return }
        hover = h
        refreshTip()
        needsDisplay = true
    }

    override func mouseExited(with e: NSEvent) {
        hover = .none
        bar?.showTip(nil)
        NSCursor.arrow.set()
        needsDisplay = true
    }

    override func mouseDown(with e: NSEvent) {
        guard state != .stopping else { return }
        switch button(at: convert(e.locationInWindow, from: nil)) {
        case .system:
            toggle(.system)
        case .mic:
            toggle(.microphone)
            if audio.contains(.microphone) { bar?.onMicrophoneEnabled?() }
        case .pause:
            if state == .ready {
                play()
            } else if state == .countdown {
                beginNow()
            } else {
                bar?.onPause?()
            }
        case .stop:
            bar?.onStop?()
            return
        case .none:
            // boş yerden sürükle
            NSCursor.closedHand.set()
            bar?.showTip(nil)
            window?.performDrag(with: e)
            return
        }
        refreshTip()
        needsDisplay = true
    }

    private func toggle(_ flag: AudioMode) {
        audio.formSymmetricDifference(flag)
        bar?.onAudioChanged?(audio)
    }
}

// Kayıt çubuğunun altında beliren açıklama.
@MainActor
private final class TipWindow {
    private var panel: NSPanel?
    private var label: NSTextField?

    func show(_ text: String, below anchor: CGRect) {
        if panel == nil {
            let p = NSPanel(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
            p.isOpaque = false
            p.backgroundColor = .clear
            p.hasShadow = false
            p.ignoresMouseEvents = true
            p.level = NSWindow.Level(rawValue: uiLevel.rawValue + 1)
            p.collectionBehavior = uiBehavior
            p.isReleasedWhenClosed = false
            let bg = NSView()
            bg.wantsLayer = true
            bg.layer?.backgroundColor = NSColor(srgbRed: 25 / 255, green: 25 / 255, blue: 28 / 255, alpha: 240 / 255).cgColor
            bg.layer?.cornerRadius = 6
            let l = NSTextField(labelWithString: "")
            l.font = NSFont.systemFont(ofSize: 12, weight: .medium)
            l.textColor = .white
            bg.addSubview(l)
            p.contentView = bg
            panel = p
            label = l
        }
        guard let panel, let label else { return }
        label.stringValue = text
        label.sizeToFit()
        let size = CGSize(width: ceil(label.frame.width) + 16, height: ceil(label.frame.height) + 8)
        label.setFrameOrigin(CGPoint(x: 8, y: 4))
        let screen = NSScreen.screens.first { $0.frame.intersects(anchor) } ?? NSScreen.main
        let vis = screen?.visibleFrame ?? anchor
        var y = anchor.minY - 4 - size.height
        if y < vis.minY { y = anchor.maxY + 4 }
        let x = max(vis.minX + 2, min(anchor.minX, vis.maxX - size.width - 2))
        panel.setFrame(CGRect(x: x, y: y, width: size.width, height: size.height), display: true)
        panel.orderFrontRegardless()
    }

    func hide() { panel?.orderOut(nil) }
}

// Ekranın sağ üstünde birkaç saniye görünen bildirim (Windows'taki balon bildirimin karşılığı).
// İzin gerektirmez; tıklanırsa verilen eylem çalışır.
@MainActor
enum Toast {
    private static var current: NSPanel?
    private static var hideWork: DispatchWorkItem?

    static func show(_ title: String, _ text: String, onClick: (() -> Void)? = nil) {
        current?.orderOut(nil)
        hideWork?.cancel()

        let width: CGFloat = 340
        let titleField = NSTextField(labelWithString: title)
        titleField.font = NSFont.systemFont(ofSize: 13, weight: .semibold)
        titleField.textColor = .labelColor
        let body = NSTextField(wrappingLabelWithString: text)
        body.font = NSFont.systemFont(ofSize: 12)
        body.textColor = .secondaryLabelColor
        body.maximumNumberOfLines = 4
        body.preferredMaxLayoutWidth = width - 72
        let icon = NSImageView(image: NSApp.applicationIconImage ?? NSImage())
        icon.imageScaling = .scaleProportionallyUpOrDown

        let textStack = NSStackView(views: [titleField, body])
        textStack.orientation = .vertical
        textStack.alignment = .leading
        textStack.spacing = 2
        let row = NSStackView(views: [icon, textStack])
        row.orientation = .horizontal
        row.alignment = .top
        row.spacing = 10
        row.edgeInsets = NSEdgeInsets(top: 12, left: 12, bottom: 12, right: 14)
        icon.widthAnchor.constraint(equalToConstant: 34).isActive = true
        icon.heightAnchor.constraint(equalToConstant: 34).isActive = true
        body.widthAnchor.constraint(lessThanOrEqualToConstant: width - 72).isActive = true

        let fx = ToastView()
        fx.material = .hudWindow
        fx.blendingMode = .behindWindow
        fx.state = .active
        fx.wantsLayer = true
        fx.layer?.cornerRadius = 12
        fx.layer?.masksToBounds = true
        fx.onClick = {
            hide()
            onClick?()
        }
        row.translatesAutoresizingMaskIntoConstraints = false
        fx.addSubview(row)
        NSLayoutConstraint.activate([
            row.leadingAnchor.constraint(equalTo: fx.leadingAnchor),
            row.trailingAnchor.constraint(equalTo: fx.trailingAnchor),
            row.topAnchor.constraint(equalTo: fx.topAnchor),
            row.bottomAnchor.constraint(equalTo: fx.bottomAnchor),
            fx.widthAnchor.constraint(equalToConstant: width),
        ])
        let height = max(58, fx.fittingSize.height)

        let p = NSPanel(contentRect: CGRect(x: 0, y: 0, width: width, height: height),
                        styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        p.isOpaque = false
        p.backgroundColor = .clear
        p.hasShadow = true
        p.level = uiLevel
        p.collectionBehavior = uiBehavior
        p.isReleasedWhenClosed = false
        p.contentView = fx
        let vis = (NSScreen.screens.first ?? NSScreen.main)?.visibleFrame ?? .zero
        p.setFrameOrigin(CGPoint(x: vis.maxX - width - 12, y: vis.maxY - height - 12))
        p.alphaValue = 0
        p.orderFrontRegardless()
        NSAnimationContext.runAnimationGroup { $0.duration = 0.15; p.animator().alphaValue = 1 }
        current = p

        let work = DispatchWorkItem { MainActor.assumeIsolated { hide() } }
        hideWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + (text.count > 80 ? 7 : 5), execute: work)
    }

    static func hide() {
        hideWork?.cancel()
        guard let p = current else { return }
        current = nil
        NSAnimationContext.runAnimationGroup({ $0.duration = 0.2; p.animator().alphaValue = 0 },
                                             completionHandler: { p.orderOut(nil) })
    }

    private final class ToastView: NSVisualEffectView {
        var onClick: (() -> Void)?
        override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
        override func hitTest(_ point: NSPoint) -> NSView? { frame.contains(point) ? self : nil }
        override func mouseDown(with event: NSEvent) { onClick?() }
    }
}
