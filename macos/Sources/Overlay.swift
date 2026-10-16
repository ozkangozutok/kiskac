import AppKit
import UniformTypeIdentifiers

enum Tool { case select, pen, line, arrow, rect, marker, text }

final class ToolButton {
    let id: String
    let tip: String
    let tool: Tool?
    let colorIndex: Int
    let separatorBefore: Bool
    var rect = CGRect.zero

    init(_ id: String, _ tip: String, tool: Tool? = nil, color: Int = -1, separatorBefore: Bool = false) {
        self.id = id
        self.tip = tip
        self.tool = tool
        self.colorIndex = color
        self.separatorBefore = separatorBefore
    }
}

// Ekran başına bir pencere. Spaces ekran başına ayrı olduğunda tek pencere birden
// fazla ekrana yayılamaz; bu yüzden her ekran kendi penceresini ve görüntüsünü alır.
final class OverlayWindow: NSWindow {
    init(screen: NSScreen) {
        super.init(contentRect: screen.frame, styleMask: .borderless, backing: .buffered, defer: false)
        setFrame(screen.frame, display: false)
        level = .screenSaver
        isOpaque = true
        backgroundColor = .black
        hasShadow = false
        isReleasedWhenClosed = false
        isMovable = false
        acceptsMouseMovedEvents = true
        animationBehavior = .none
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
    }

    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
}

// Bir çekim oturumu: tüm ekranlardaki pencereler ve kapanınca önceki uygulamaya dönüş.
@MainActor
final class OverlaySession {
    private var windows: [OverlayWindow] = []
    private var canvases: [CanvasView] = []
    private let previousApp: NSRunningApplication?
    private var closed = false
    private var resignObserver: NSObjectProtocol?
    var onClose: (() -> Void)?

    init(shots: [ScreenShot]) {
        let front = NSWorkspace.shared.frontmostApplication
        previousApp = front == NSRunningApplication.current ? nil : front

        for shot in shots {
            let w = OverlayWindow(screen: shot.screen)
            let size = shot.screen.frame.size
            let root = NSView(frame: CGRect(origin: .zero, size: size))
            root.wantsLayer = true
            let backdrop = BackdropView(frame: root.bounds, image: shot.image)
            let canvas = CanvasView(frame: root.bounds, image: shot.image, backdrop: backdrop,
                                    topInset: shot.screen.safeAreaInsets.top)
            canvas.session = self
            root.addSubview(backdrop)
            root.addSubview(canvas)
            w.contentView = root
            windows.append(w)
            canvases.append(canvas)
        }
    }

    func show() {
        NSApp.activate(ignoringOtherApps: true)
        for w in windows { w.orderFrontRegardless() }
        bringToFront()
        resignObserver = NotificationCenter.default.addObserver(
            forName: NSApplication.didResignActiveNotification, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.close(restoreFocus: false) }
        }
    }

    // İmlecin bulunduğu ekranın penceresi klavyeyi alır.
    func bringToFront() {
        NSApp.activate(ignoringOtherApps: true)
        let mouse = NSEvent.mouseLocation
        let i = windows.firstIndex { NSMouseInRect(mouse, $0.frame, false) } ?? 0
        guard i < windows.count else { return }
        windows[i].makeKeyAndOrderFront(nil)
        windows[i].makeFirstResponder(canvases[i])
        NSCursor.crosshair.set()
    }

    // Bir ekranda yeni seçim başlayınca diğer ekranlardaki seçim temizlenir.
    func selectionStarted(on canvas: CanvasView) {
        for c in canvases where c !== canvas { c.reset() }
    }

    func close(restoreFocus: Bool = true) {
        if closed { return }
        closed = true
        if let o = resignObserver { NotificationCenter.default.removeObserver(o) }
        for w in windows { w.orderOut(nil) }
        // Kapatma çoğu zaman bir görünümün olay işleyicisinden gelir; içerik bir sonraki turda bırakılır.
        let old = windows
        DispatchQueue.main.async { for w in old { w.contentView = nil } }
        windows.removeAll()
        canvases.removeAll()
        if restoreFocus, let app = previousApp {
            NSApp.yieldActivation(to: app)
            app.activate(options: [])
        }
        onClose?()
    }
}

// Ekran görüntüsü ve karartma katmanı. Görüntü bir kez GPU'ya verilir; fare hareketinde
// yalnızca karartmanın içindeki "delik" (seçim) değişir, piksel yeniden çizilmez.
final class BackdropView: NSView {
    private let imageLayer = CALayer()
    private let dimLayer = CAShapeLayer()

    init(frame: CGRect, image: CGImage) {
        super.init(frame: frame)
        let root = CALayer()
        layer = root
        wantsLayer = true

        imageLayer.frame = bounds
        imageLayer.contents = image
        imageLayer.contentsGravity = .resize
        imageLayer.magnificationFilter = .nearest
        imageLayer.minificationFilter = .nearest
        imageLayer.isOpaque = true
        root.addSublayer(imageLayer)

        dimLayer.frame = bounds
        dimLayer.fillRule = .evenOdd
        dimLayer.fillColor = CGColor(gray: 0, alpha: Settings.dimOpacity)
        dimLayer.actions = ["path": NSNull()]
        dimLayer.path = CGPath(rect: bounds, transform: nil)
        root.addSublayer(dimLayer)
    }

    required init?(coder: NSCoder) { fatalError() }

    // hole: sol üst orijinli seçim dikdörtgeni
    func setHole(_ hole: CGRect?) {
        let p = CGMutablePath()
        p.addRect(bounds)
        if let h = hole, h.width > 0, h.height > 0 {
            p.addRect(CGRect(x: h.minX, y: bounds.height - h.maxY, width: h.width, height: h.height))
        }
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        dimLayer.path = p
        CATransaction.commit()
    }
}

// Seçim, çizim ve araç çubuğu. Şeffaf katman; yalnızca değişen bölge yeniden çizilir.
final class CanvasView: NSView {
    static let palette: [NSColor] = [
        NSColor(srgbRed: 235 / 255, green: 45 / 255, blue: 45 / 255, alpha: 1),
        NSColor(srgbRed: 255 / 255, green: 195 / 255, blue: 0, alpha: 1),
        NSColor(srgbRed: 40 / 255, green: 185 / 255, blue: 85 / 255, alpha: 1),
        NSColor(srgbRed: 0, green: 125 / 255, blue: 255 / 255, alpha: 1),
        NSColor(srgbRed: 20 / 255, green: 20 / 255, blue: 20 / 255, alpha: 1),
        .white,
    ]
    static let widths: [CGFloat] = [2, 3, 5, 8, 12]
    static let hintText = "Alanı seçmek için sürükleyin   •   Tıklayın: tüm ekran   •   Esc: iptal"

    enum Mode { case none, selecting, moving, resizing, drawing }

    weak var session: OverlaySession?
    private let image: CGImage
    private let backdrop: BackdropView
    private let pixelScale: CGFloat
    private let topInset: CGFloat
    private let accent = NSColor.controlAccentColor
    private let uiFont = NSFont.systemFont(ofSize: 12, weight: .medium)

    private var sel = CGRect.zero
    private var hasSel = false
    private var mode = Mode.none
    private var dragStart = CGPoint.zero
    private var selStart = CGRect.zero
    private var resizeHandle = -1
    private var lastPoint = CGPoint.zero

    private var tool = Tool.select
    private var colorIndex = 0 {
        didSet { if Settings.rememberStyle { Settings.lastColor = colorIndex } }
    }
    private var penWidth: CGFloat = 3 {
        didSet { if Settings.rememberStyle { Settings.lastWidth = penWidth } }
    }
    private var shapes: [Shape] = []
    private var current: Shape?
    private var currentBounds = CGRect.null
    private var editing: TextShape?

    private var buttons: [ToolButton] = []
    private var toolbarRect = CGRect.zero
    private weak var hover: ToolButton?
    private var lastVisual = CGRect.null
    private var scrollAccum: CGFloat = 0
    private var symbols: [String: NSImage] = [:]

    init(frame: CGRect, image: CGImage, backdrop: BackdropView, topInset: CGFloat) {
        self.image = image
        self.backdrop = backdrop
        self.pixelScale = CGFloat(image.width) / max(frame.width, 1)
        self.topInset = topInset
        super.init(frame: frame)
        if Settings.rememberStyle {
            colorIndex = min(max(Settings.lastColor, 0), Self.palette.count - 1)
            penWidth = min(max(Settings.lastWidth, 1), 40)
        }
        wantsLayer = true
        layerContentsRedrawPolicy = .onSetNeedsDisplay
        buildButtons()
        addTrackingArea(NSTrackingArea(rect: .zero, options: [.mouseMoved, .activeAlways, .inVisibleRect],
                                       owner: self, userInfo: nil))
    }

    required init?(coder: NSCoder) { fatalError() }

    override var isFlipped: Bool { true }
    override var isOpaque: Bool { false }
    override var acceptsFirstResponder: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    private func buildButtons() {
        buttons = [
            ToolButton("select", "Seç / taşı (S)", tool: .select),
            ToolButton("pen", "Kalem (P)", tool: .pen),
            ToolButton("line", "Çizgi (L)  •  ⇧: 45°", tool: .line),
            ToolButton("arrow", "Ok (A)  •  ⇧: 45°", tool: .arrow),
            ToolButton("rect", "Dikdörtgen (R)  •  ⇧: kare", tool: .rect),
            ToolButton("marker", "Fosforlu kalem (M)", tool: .marker),
            ToolButton("text", "Metin (T)  •  ↩: bitir", tool: .text),
        ]
        for i in 0..<Self.palette.count {
            buttons.append(ToolButton("color", "Renk", color: i, separatorBefore: i == 0))
        }
        buttons.append(ToolButton("width", "Kalınlık  •  kaydırma"))
        buttons.append(ToolButton("undo", "Geri al (⌘Z)", separatorBefore: true))
        buttons.append(ToolButton("save", "Kaydet (⌘S)"))
        buttons.append(ToolButton("copy", "Kopyala (⌘C / ↩)"))
        buttons.append(ToolButton("close", "Kapat (Esc)"))
    }

    private var currentColor: NSColor { Self.palette[colorIndex] }
    private var toolbarVisible: Bool { hasSel && mode != .selecting }

    func reset() {
        guard hasSel || editing != nil else { return }
        hasSel = false
        sel = .zero
        shapes.removeAll()
        current = nil
        editing = nil
        tool = .select
        mode = .none
        hover = nil
        updateView()
    }

    private func point(_ e: NSEvent) -> CGPoint {
        convert(e.locationInWindow, from: nil)
    }

    private func clamp(_ p: CGPoint) -> CGPoint {
        CGPoint(x: min(max(p.x, 0), bounds.width), y: min(max(p.y, 0), bounds.height))
    }

    private static func normalize(_ a: CGPoint, _ b: CGPoint) -> CGRect {
        // Seçim tam noktalara oturur; böylece Retina'da piksel kaymadan kırpılır.
        let l = min(a.x, b.x).rounded(), t = min(a.y, b.y).rounded()
        let r = max(a.x, b.x).rounded(), bt = max(a.y, b.y).rounded()
        return CGRect(x: l, y: t, width: r - l, height: bt - t)
    }

    // ---------------------------------------------------------------- yerleşim

    private func layoutToolbar() {
        guard hasSel else {
            toolbarRect = .zero
            return
        }
        let cell: CGFloat = 32, sep: CGFloat = 10, pad: CGFloat = 5, gap: CGFloat = 8
        var w = pad * 2
        for b in buttons { w += cell + (b.separatorBefore ? sep : 0) }
        let h = cell + pad * 2

        let mon = bounds
        var x = sel.maxX - w
        var y = sel.maxY + gap
        if y + h > mon.maxY - 2 {
            y = sel.minY - gap - h
            if y < mon.minY + topInset + 2 { y = sel.maxY - h - gap }
        }
        x = max(mon.minX + 2, min(x, mon.maxX - w - 2))
        y = max(mon.minY + 2, min(y, mon.maxY - h - 2))
        toolbarRect = CGRect(x: x, y: y, width: w, height: h)

        var cx = x + pad
        for b in buttons {
            if b.separatorBefore { cx += sep }
            b.rect = CGRect(x: cx, y: y + pad, width: cell, height: cell)
            cx += cell
        }
    }

    private var uiAttrs: [NSAttributedString.Key: Any] { [.font: uiFont, .foregroundColor: NSColor.white] }

    private func textSize(_ s: String) -> CGSize {
        let sz = (s as NSString).size(withAttributes: uiAttrs)
        return CGSize(width: ceil(sz.width), height: ceil(sz.height))
    }

    private func tooltipRect() -> CGRect {
        guard let hv = hover, toolbarVisible else { return .null }
        let sz = textSize(hv.tip)
        let w = sz.width + 16, h = sz.height + 8
        var x = hv.rect.midX - w / 2
        var y = toolbarRect.minY - h - 6
        if y < topInset { y = toolbarRect.maxY + 6 }
        x = max(2, min(x, bounds.width - w - 2))
        return CGRect(x: x, y: y, width: w, height: h)
    }

    private var sizeText: String {
        "\(Int((sel.width * pixelScale).rounded())) × \(Int((sel.height * pixelScale).rounded()))"
    }

    private func sizeLabelRect() -> CGRect {
        let sz = textSize(sizeText)
        let w = sz.width + 12, h = sz.height + 4
        var x = sel.minX, y = sel.minY - h - 4
        if y < topInset {
            x = sel.minX + 4
            y = sel.minY + 4
        }
        return CGRect(x: x, y: y, width: w, height: h)
    }

    private func hintRect() -> CGRect {
        let sz = textSize(Self.hintText)
        let w = sz.width + 28, h = sz.height + 16
        return CGRect(x: ((bounds.width - w) / 2).rounded(), y: topInset + 40, width: w, height: h)
    }

    private func visualBounds() -> CGRect {
        guard hasSel else { return hintRect() }
        var r = sel.insetBy(dx: -12, dy: -12)
        r = r.union(sizeLabelRect().insetBy(dx: -2, dy: -2))
        if toolbarVisible {
            r = r.union(toolbarRect.insetBy(dx: -2, dy: -2))
            let t = tooltipRect()
            if !t.isNull { r = r.union(t.insetBy(dx: -2, dy: -2)) }
        }
        return r
    }

    // Yalnızca değişen bölgeyi yeniden çizer; 5K ekranda bile akıcı kalsın diye.
    private func updateView() {
        layoutToolbar()
        let nb = visualBounds()
        setNeedsDisplay(lastVisual.isNull ? nb : lastVisual.union(nb))
        lastVisual = nb
        backdrop.setHole(hasSel ? sel : nil)
    }

    // Çizim sırasında yalnızca çizilen şeklin alanı yenilenir.
    private func updateCurrentShape() {
        guard let c = current else { return }
        let nb = c.bounds.intersection(sel.insetBy(dx: -1, dy: -1))
        setNeedsDisplay(currentBounds.isNull ? nb : currentBounds.union(nb))
        currentBounds = nb
    }

    private func handlePoint(_ i: Int) -> CGPoint {
        let l = sel.minX, t = sel.minY, r = sel.maxX, b = sel.maxY
        let mx = sel.midX, my = sel.midY
        switch i {
        case 0: return CGPoint(x: l, y: t)
        case 1: return CGPoint(x: mx, y: t)
        case 2: return CGPoint(x: r, y: t)
        case 3: return CGPoint(x: r, y: my)
        case 4: return CGPoint(x: r, y: b)
        case 5: return CGPoint(x: mx, y: b)
        case 6: return CGPoint(x: l, y: b)
        default: return CGPoint(x: l, y: my)
        }
    }

    private func hitHandle(_ p: CGPoint) -> Int {
        let tol: CGFloat = 7
        for i in 0..<8 {
            let h = handlePoint(i)
            if abs(p.x - h.x) <= tol && abs(p.y - h.y) <= tol { return i }
        }
        return -1
    }

    private func hitButton(_ p: CGPoint) -> ToolButton? {
        buttons.first { $0.rect.contains(p) }
    }

    private func cursor(for p: CGPoint) -> NSCursor {
        guard hasSel else { return .crosshair }
        if toolbarRect.contains(p) { return .arrow }
        if tool == .select {
            let h = hitHandle(p)
            if h >= 0 {
                if #available(macOS 15.0, *) {
                    let pos: [NSCursor.FrameResizePosition] = [.topLeft, .top, .topRight, .right, .bottomRight, .bottom, .bottomLeft, .left]
                    return NSCursor.frameResize(position: pos[h], directions: .all)
                }
                switch h {
                case 1, 5: return .resizeUpDown
                case 3, 7: return .resizeLeftRight
                default: return .crosshair
                }
            }
            return sel.contains(p) ? .openHand : .crosshair
        }
        if sel.contains(p) { return tool == .text ? .iBeam : .crosshair }
        return .arrow
    }

    // ---------------------------------------------------------------- fare

    override func mouseDown(with e: NSEvent) {
        let p = point(e)
        lastPoint = p

        if toolbarVisible && toolbarRect.contains(p) {
            if let b = hitButton(p) { press(b) }
            return
        }

        if e.clickCount == 2 && hasSel && tool == .select && sel.contains(p) {
            copyAndClose()
            return
        }

        if editing != nil { commitText() }

        if !hasSel || tool == .select {
            let h = hasSel ? hitHandle(p) : -1
            if h >= 0 {
                mode = .resizing
                resizeHandle = h
            } else if hasSel && sel.contains(p) {
                mode = .moving
                NSCursor.closedHand.set()
            } else {
                session?.selectionStarted(on: self)
                mode = .selecting
                hasSel = true
                sel = CGRect(origin: CGPoint(x: p.x.rounded(), y: p.y.rounded()), size: .zero)
                shapes.removeAll()
            }
            dragStart = p
            selStart = sel
        } else {
            guard sel.contains(p) else { return }
            let c = currentColor
            switch tool {
            case .text:
                editing = TextShape(p, color: c, width: penWidth)
                updateView()
                return
            case .pen: current = FreehandShape(p, color: c, width: penWidth, marker: false)
            case .marker: current = FreehandShape(p, color: c, width: penWidth, marker: true)
            case .line: current = LineShape(p, color: c, width: penWidth, arrow: false)
            case .arrow: current = LineShape(p, color: c, width: penWidth, arrow: true)
            case .rect: current = RectShape(p, color: c, width: penWidth)
            case .select: break
            }
            mode = .drawing
            currentBounds = .null
            updateCurrentShape()
            return
        }
        updateView()
    }

    override func mouseDragged(with e: NSEvent) {
        let p = point(e)
        lastPoint = p
        switch mode {
        case .selecting:
            sel = Self.normalize(dragStart, clamp(p))
            updateView()
        case .moving:
            var r = selStart.offsetBy(dx: (p.x - dragStart.x).rounded(), dy: (p.y - dragStart.y).rounded())
            r.origin.x = max(0, min(r.minX, bounds.width - r.width))
            r.origin.y = max(0, min(r.minY, bounds.height - r.height))
            sel = r
            updateView()
        case .resizing:
            sel = resizeRect(selStart, resizeHandle, p.x - dragStart.x, p.y - dragStart.y)
            updateView()
        case .drawing:
            current?.update(p, shift: e.modifierFlags.contains(.shift))
            updateCurrentShape()
        case .none:
            break
        }
    }

    override func mouseMoved(with e: NSEvent) {
        let p = point(e)
        let b = toolbarVisible && toolbarRect.contains(p) ? hitButton(p) : nil
        if b !== hover {
            hover = b
            updateView()
        }
        cursor(for: p).set()
    }

    override func flagsChanged(with e: NSEvent) {
        // Shift fare kıpırdamadan basılsa da çizgi/kare hemen güncellensin.
        if mode == .drawing {
            current?.update(lastPoint, shift: e.modifierFlags.contains(.shift))
            updateCurrentShape()
        }
    }

    private func resizeRect(_ r: CGRect, _ handle: Int, _ dx: CGFloat, _ dy: CGFloat) -> CGRect {
        var l = r.minX, t = r.minY, rt = r.maxX, b = r.maxY
        if handle == 0 || handle == 6 || handle == 7 { l += dx }
        if handle == 2 || handle == 3 || handle == 4 { rt += dx }
        if handle == 0 || handle == 1 || handle == 2 { t += dy }
        if handle == 4 || handle == 5 || handle == 6 { b += dy }
        return Self.normalize(clamp(CGPoint(x: l, y: t)), clamp(CGPoint(x: rt, y: b)))
    }

    override func mouseUp(with e: NSEvent) {
        let m = mode
        mode = .none
        if m == .selecting && (sel.width < 4 || sel.height < 4) {
            sel = bounds // tek tıklama: o ekranın tamamı
        } else if m == .drawing, let c = current {
            if !c.isEmpty { shapes.append(c) }
            current = nil
            setNeedsDisplay(currentBounds)
            currentBounds = .null
        }
        updateView()
        cursor(for: point(e)).set()
    }

    override func rightMouseDown(with e: NSEvent) {
        if editing != nil { cancelText() } else { session?.close() }
    }

    override func scrollWheel(with e: NSEvent) {
        // İzleme dörtgeni çok sayıda küçük adım gönderir; biriktirip tek adıma çevrilir.
        var d = e.scrollingDeltaY
        if e.hasPreciseScrollingDeltas {
            scrollAccum += d
            if abs(scrollAccum) < 14 { return }
            d = scrollAccum
            scrollAccum = 0
        }
        if d == 0 { return }
        penWidth = max(1, min(40, penWidth + (d > 0 ? 1 : -1)))
        editing?.width = penWidth
        updateView()
    }

    // ---------------------------------------------------------------- klavye

    override func keyDown(with e: NSEvent) {
        let flags = e.modifierFlags.intersection(.deviceIndependentFlagsMask)
        let cmd = flags.contains(.command)
        let key = e.charactersIgnoringModifiers?.lowercased() ?? ""
        let isReturn = e.keyCode == 36 || e.keyCode == 76
        let isEsc = e.keyCode == 53

        if let t = editing {
            if isEsc {
                cancelText()
            } else if isReturn {
                if flags.contains(.shift) {
                    t.text += "\n"
                    updateView()
                } else {
                    commitText()
                }
            } else if e.keyCode == 51 { // silme
                if !t.text.isEmpty { t.text.removeLast() }
                updateView()
            } else if cmd {
                switch key {
                case "v":
                    if let s = NSPasteboard.general.string(forType: .string) {
                        t.text += s.replacingOccurrences(of: "\r\n", with: "\n")
                        updateView()
                    }
                case "c": commitText(); copyAndClose()
                case "s": commitText(); save()
                case "z": cancelText()
                default: break
                }
            } else if !flags.contains(.control), let s = e.characters, isPrintable(s) {
                // Türkçe karakterler ve ⌥ ile yazılanlar (ör. @) doğrudan gelir.
                t.text += s
                updateView()
            }
            return
        }

        if isEsc { session?.close(); return }
        if isReturn || (cmd && key == "c") {
            if hasSel { copyAndClose() }
            return
        }
        if cmd {
            switch key {
            case "s": if hasSel { save() }
            case "z": undo()
            default: super.keyDown(with: e)
            }
            return
        }
        switch key {
        case "s": setTool(.select)
        case "p": setTool(.pen)
        case "l": setTool(.line)
        case "a": setTool(.arrow)
        case "r": setTool(.rect)
        case "m": setTool(.marker)
        case "t": setTool(.text)
        default: break
        }
    }

    private func isPrintable(_ s: String) -> Bool {
        !s.isEmpty && s.unicodeScalars.allSatisfy {
            !CharacterSet.controlCharacters.contains($0) && !(0xF700...0xF8FF).contains($0.value)
        }
    }

    // ---------------------------------------------------------------- eylemler

    private func setTool(_ t: Tool) {
        guard hasSel else { return }
        tool = t
        updateView()
    }

    private func press(_ b: ToolButton) {
        if let t = b.tool {
            if editing != nil { commitText() }
            tool = t
        } else if b.colorIndex >= 0 {
            colorIndex = b.colorIndex
            editing?.color = currentColor
        } else {
            switch b.id {
            case "width":
                penWidth = Self.widths.first { $0 > penWidth } ?? Self.widths[0]
                editing?.width = penWidth
            case "undo": undo()
            case "save": save(); return
            case "copy": copyAndClose(); return
            case "close": session?.close(); return
            default: break
            }
        }
        updateView()
    }

    private func undo() {
        if editing != nil {
            cancelText()
        } else if !shapes.isEmpty {
            shapes.removeLast()
        }
        updateView()
    }

    private func commitText() {
        guard let t = editing else { return }
        if !t.isEmpty { shapes.append(t) }
        editing = nil
        updateView()
    }

    private func cancelText() {
        editing = nil
        updateView()
    }

    // Seçili alanı tam Retina çözünürlüğünde, çizimlerle birlikte üretir.
    private func renderResult() -> CGImage? {
        let px = CGRect(x: sel.minX * pixelScale, y: sel.minY * pixelScale,
                        width: sel.width * pixelScale, height: sel.height * pixelScale).integral
        guard px.width >= 1, px.height >= 1, let crop = image.cropping(to: px) else { return nil }
        if shapes.isEmpty { return crop } // çizim yoksa pikseller olduğu gibi

        let space = image.colorSpace ?? CGColorSpace(name: CGColorSpace.sRGB)!
        guard let g = CGContext(data: nil, width: crop.width, height: crop.height, bitsPerComponent: 8, bytesPerRow: 0,
                                space: space,
                                bitmapInfo: CGImageAlphaInfo.noneSkipFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue)
        else { return nil }
        g.interpolationQuality = .none
        g.draw(crop, in: CGRect(x: 0, y: 0, width: crop.width, height: crop.height))
        g.translateBy(x: 0, y: CGFloat(crop.height))
        g.scaleBy(x: pixelScale, y: -pixelScale)
        g.translateBy(x: -sel.minX, y: -sel.minY)
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(cgContext: g, flipped: true)
        for s in shapes { s.draw() }
        NSGraphicsContext.restoreGraphicsState()
        return g.makeImage()
    }

    private func copyAndClose() {
        guard hasSel, sel.width >= 1, sel.height >= 1 else { return }
        commitText()
        guard let img = renderResult() else { return }
        let size = sel.size
        session?.close()
        CATransaction.flush() // pencereler kodlama başlamadan kaybolsun
        Output.copy(img, pointSize: size)
    }

    private func save() {
        guard hasSel, sel.width >= 1, sel.height >= 1 else { return }
        commitText()
        guard let img = renderResult() else { return }
        if Output.save(img, pointSize: sel.size, aboveLevel: window?.level ?? .screenSaver) {
            session?.close()
        } else {
            window?.makeKeyAndOrderFront(nil)
            window?.makeFirstResponder(self)
        }
    }

    // ---------------------------------------------------------------- çizim

    override func draw(_ dirtyRect: NSRect) {
        NSColor.clear.set()
        dirtyRect.fill(using: .copy)

        guard hasSel else {
            if Settings.showHint { drawPill(hintRect(), alpha: 225 / 255, text: Self.hintText) }
            return
        }

        NSGraphicsContext.saveGraphicsState()
        NSBezierPath(rect: sel).addClip()
        for s in shapes { s.draw() }
        current?.draw()
        editing?.drawEditing()
        NSGraphicsContext.restoreGraphicsState()

        drawFrame()
        drawPill(sizeLabelRect(), alpha: 210 / 255, text: sizeText)
        if toolbarVisible { drawToolbar() }
    }

    private func drawFrame() {
        let r = sel.insetBy(dx: -0.5, dy: -0.5)
        let white = NSBezierPath(rect: r)
        white.lineWidth = 1
        NSColor.white.set()
        white.stroke()
        let dash = NSBezierPath(rect: r)
        dash.lineWidth = 1
        dash.setLineDash([4, 4], count: 2, phase: 0)
        accent.set()
        dash.stroke()

        if tool != .select || mode == .selecting { return }
        let hs: CGFloat = 8
        for i in 0..<8 {
            let p = handlePoint(i)
            let hr = CGRect(x: p.x - hs / 2, y: p.y - hs / 2, width: hs, height: hs)
            NSColor.white.set()
            hr.fill()
            accent.set()
            let b = NSBezierPath(rect: hr.insetBy(dx: 0.5, dy: 0.5))
            b.lineWidth = 1
            b.stroke()
        }
    }

    private func drawPill(_ r: CGRect, alpha: CGFloat, text: String) {
        NSColor(srgbRed: 25 / 255, green: 25 / 255, blue: 28 / 255, alpha: alpha).set()
        NSBezierPath(roundedRect: r, xRadius: 6, yRadius: 6).fill()
        let sz = textSize(text)
        (text as NSString).draw(at: CGPoint(x: r.midX - sz.width / 2, y: r.midY - sz.height / 2), withAttributes: uiAttrs)
    }

    private func drawToolbar() {
        let bg = NSBezierPath(roundedRect: toolbarRect, xRadius: 9, yRadius: 9)
        NSColor(srgbRed: 32 / 255, green: 32 / 255, blue: 36 / 255, alpha: 245 / 255).set()
        bg.fill()
        NSColor(white: 1, alpha: 70 / 255).set()
        bg.lineWidth = 1
        let edge = NSBezierPath(roundedRect: toolbarRect.insetBy(dx: 0.5, dy: 0.5), xRadius: 8.5, yRadius: 8.5)
        edge.lineWidth = 1
        edge.stroke()

        for b in buttons {
            if b.separatorBefore {
                NSColor(white: 1, alpha: 60 / 255).set()
                CGRect(x: b.rect.minX - 5.5, y: b.rect.minY + 6, width: 1, height: b.rect.height - 12).fill()
            }
            let active = b.tool != nil && b.tool == tool
            if active || b === hover {
                (active ? accent : NSColor(white: 1, alpha: 60 / 255)).set()
                NSBezierPath(roundedRect: b.rect.insetBy(dx: 2, dy: 2), xRadius: 6, yRadius: 6).fill()
            }
            drawIcon(b)
        }

        let tip = tooltipRect()
        if !tip.isNull, let hv = hover { drawPill(tip, alpha: 240 / 255, text: hv.tip) }
    }

    private static let symbolNames = [
        "select": "cursorarrow", "pen": "scribble", "line": "line.diagonal", "arrow": "arrow.up.right",
        "rect": "rectangle", "marker": "highlighter", "text": "textformat",
        "undo": "arrow.uturn.backward", "save": "square.and.arrow.down", "copy": "doc.on.doc", "close": "xmark",
    ]

    private func symbol(_ id: String) -> NSImage? {
        if let s = symbols[id] { return s }
        guard let name = Self.symbolNames[id] else { return nil }
        let cfg = NSImage.SymbolConfiguration(pointSize: 15, weight: .medium)
            .applying(NSImage.SymbolConfiguration(paletteColors: [.white]))
        let img = NSImage(systemSymbolName: name, accessibilityDescription: nil)?.withSymbolConfiguration(cfg)
        symbols[id] = img
        return img
    }

    private func drawIcon(_ b: ToolButton) {
        let c = CGPoint(x: b.rect.midX, y: b.rect.midY)
        switch b.id {
        case "color":
            let d: CGFloat = 15
            if b.colorIndex == colorIndex {
                NSColor.white.set()
                let ring = NSBezierPath(ovalIn: CGRect(x: c.x - 11, y: c.y - 11, width: 22, height: 22))
                ring.lineWidth = 2
                ring.stroke()
            }
            let dot = NSBezierPath(ovalIn: CGRect(x: c.x - d / 2, y: c.y - d / 2, width: d, height: d))
            Self.palette[b.colorIndex].set()
            dot.fill()
            NSColor(white: 1, alpha: 90 / 255).set()
            dot.lineWidth = 1
            dot.stroke()
        case "width":
            let d = min(4 + penWidth * 1.1, 20)
            let dot = NSBezierPath(ovalIn: CGRect(x: c.x - d / 2, y: c.y - d / 2, width: d, height: d))
            currentColor.set()
            dot.fill()
            NSColor(white: 1, alpha: 120 / 255).set()
            dot.lineWidth = 1
            dot.stroke()
        default:
            guard let img = symbol(b.id) else { return }
            let s = img.size
            img.draw(in: CGRect(x: (c.x - s.width / 2).rounded(), y: (c.y - s.height / 2).rounded(), width: s.width, height: s.height),
                     from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true, hints: nil)
        }
    }
}

// Pano ve dosyaya yazma.
@MainActor
enum Output {
    // Ayar açıksa Retina görüntü nokta boyutuna küçültülür.
    private static func prepared(_ img: CGImage, _ pointSize: CGSize) -> CGImage {
        let w = Int(pointSize.width.rounded()), h = Int(pointSize.height.rounded())
        guard Settings.downscale, w > 0, h > 0, img.width > w,
              let g = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
                                space: img.colorSpace ?? CGColorSpace(name: CGColorSpace.sRGB)!,
                                bitmapInfo: CGImageAlphaInfo.noneSkipFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue)
        else { return img }
        g.interpolationQuality = .high
        g.draw(img, in: CGRect(x: 0, y: 0, width: w, height: h))
        return g.makeImage() ?? img
    }

    private static func rep(_ img: CGImage, _ pointSize: CGSize) -> NSBitmapImageRep {
        let r = NSBitmapImageRep(cgImage: prepared(img, pointSize))
        r.size = pointSize // Retina görüntü yapıştırıldığı yerde doğru boyutta (144 dpi) görünsün
        return r
    }

    private static func encode(_ r: NSBitmapImageRep, jpeg: Bool) -> Data? {
        jpeg
            ? r.representation(using: .jpeg, properties: [.compressionFactor: Settings.jpegQuality])
            : r.representation(using: .png, properties: [:])
    }

    static func copy(_ img: CGImage, pointSize: CGSize) {
        let r = rep(img, pointSize)
        let item = NSPasteboardItem()
        if let png = r.representation(using: .png, properties: [:]) { item.setData(png, forType: .png) }
        if let tiff = r.tiffRepresentation { item.setData(tiff, forType: .tiff) }
        let pb = NSPasteboard.general
        pb.clearContents()
        pb.writeObjects([item])
    }

    private static func baseName() -> String {
        let stamp = DateFormatter()
        stamp.dateFormat = "yyyy-MM-dd_HH-mm-ss"
        return "Kıskaç_" + stamp.string(from: Date())
    }

    // Kaydedildiyse true döner. Hızlı kaydetme açıksa panel açılmadan kayıt klasörüne yazar.
    static func save(_ img: CGImage, pointSize: CGSize, aboveLevel: NSWindow.Level) -> Bool {
        let level = NSWindow.Level(rawValue: aboveLevel.rawValue + 1)
        let url: URL
        if Settings.quickSave {
            let dir = Settings.saveDir
            let ext = Settings.jpeg ? "jpg" : "png"
            var candidate = dir.appendingPathComponent(baseName() + "." + ext)
            var n = 2
            while FileManager.default.fileExists(atPath: candidate.path) {
                candidate = dir.appendingPathComponent(baseName() + " (\(n))." + ext)
                n += 1
            }
            url = candidate
        } else {
            let panel = NSSavePanel()
            panel.title = "Ekran görüntüsünü kaydet"
            panel.level = level
            panel.directoryURL = Settings.saveDir
            panel.canCreateDirectories = true
            panel.isExtensionHidden = false
            panel.allowedContentTypes = [Settings.jpeg ? .jpeg : .png]
            panel.nameFieldStringValue = baseName() + (Settings.jpeg ? ".jpg" : ".png")

            let format = FormatPicker(panel: panel, jpeg: Settings.jpeg)
            panel.accessoryView = format.view

            let ok = withExtendedLifetime(format) { panel.runModal() == .OK }
            guard ok, let u = panel.url else { return false }
            url = u
        }

        let r = rep(img, pointSize)
        let isJpeg = ["jpg", "jpeg"].contains(url.pathExtension.lowercased())
        do {
            guard let d = encode(r, jpeg: isJpeg) else { throw CocoaError(.fileWriteUnknown) }
            try d.write(to: url, options: .atomic)
            if !Settings.quickSave { Settings.saveDir = url.deletingLastPathComponent() }
        } catch {
            let a = NSAlert()
            a.messageText = "Kaydedilemedi"
            a.informativeText = error.localizedDescription
            a.alertStyle = .warning
            a.window.level = level
            a.runModal()
            return false
        }
        if Settings.copyAfterSave { copy(img, pointSize: pointSize) }
        return true
    }
}

// Kaydet panelinde PNG / JPEG seçimi.
@MainActor
final class FormatPicker: NSObject {
    let view: NSView
    private let popup: NSPopUpButton
    private weak var panel: NSSavePanel?

    init(panel: NSSavePanel, jpeg: Bool) {
        self.panel = panel
        popup = NSPopUpButton(frame: .zero, pullsDown: false)
        popup.addItems(withTitles: ["PNG", "JPEG"])
        popup.selectItem(at: jpeg ? 1 : 0)
        let label = NSTextField(labelWithString: "Biçim:")
        let stack = NSStackView(views: [label, popup])
        stack.orientation = .horizontal
        stack.edgeInsets = NSEdgeInsets(top: 8, left: 12, bottom: 8, right: 12)
        stack.frame = CGRect(x: 0, y: 0, width: 220, height: 40)
        view = stack
        super.init()
        popup.target = self
        popup.action = #selector(changed)
    }

    @objc private func changed() {
        guard let panel else { return }
        let jpeg = popup.indexOfSelectedItem == 1
        panel.allowedContentTypes = [jpeg ? .jpeg : .png]
        let name = (panel.nameFieldStringValue as NSString).deletingPathExtension
        panel.nameFieldStringValue = name + (jpeg ? ".jpg" : ".png")
    }
}
