import AppKit

// Tüm şekiller ekranın nokta (point) koordinatlarında, sol üst köşe orijinli tutulur.
// Çizim o anki NSGraphicsContext'e yapılır; dışa aktarımda bağlam Retina ölçeğine büyütülür.
class Shape {
    var color: NSColor
    var width: CGFloat

    init(color: NSColor, width: CGFloat) {
        self.color = color
        self.width = width
    }

    func update(_ p: CGPoint, shift: Bool) {}
    func draw() {}
    var isEmpty: Bool { false }
    var bounds: CGRect { .null }

    static func snap45(_ a: CGPoint, _ b: CGPoint) -> CGPoint {
        let dx = b.x - a.x, dy = b.y - a.y
        let len = (dx * dx + dy * dy).squareRoot()
        let step = CGFloat.pi / 4
        let ang = (atan2(dy, dx) / step).rounded() * step
        return CGPoint(x: (a.x + cos(ang) * len).rounded(), y: (a.y + sin(ang) * len).rounded())
    }

    static func box(_ a: CGPoint, _ b: CGPoint) -> CGRect {
        CGRect(x: min(a.x, b.x), y: min(a.y, b.y), width: abs(b.x - a.x), height: abs(b.y - a.y))
    }
}

final class FreehandShape: Shape {
    private var pts: [CGPoint]
    private let marker: Bool
    private var box: CGRect

    init(_ start: CGPoint, color c: NSColor, width w: CGFloat, marker: Bool) {
        self.marker = marker
        pts = [start]
        box = CGRect(origin: start, size: .zero)
        super.init(color: marker ? c.withAlphaComponent(110 / 255) : c, width: marker ? max(w * 4, 12) : w)
    }

    override func update(_ p: CGPoint, shift: Bool) {
        if shift {
            // Shift: düz çizgi (fosforlu kalemde yatay)
            let a = pts[0]
            pts = [a, marker ? CGPoint(x: p.x, y: a.y) : p]
            box = Shape.box(pts[0], pts[1])
            return
        }
        if pts.last != p {
            pts.append(p)
            box = box.union(CGRect(origin: p, size: .zero))
        }
    }

    override var bounds: CGRect { box.insetBy(dx: -width, dy: -width) }

    override func draw() {
        color.set()
        if pts.count == 1 {
            NSBezierPath(ovalIn: CGRect(x: pts[0].x - width / 2, y: pts[0].y - width / 2, width: width, height: width)).fill()
            return
        }
        // Noktalar arası orta noktalardan geçen eğri: fare örneklemesindeki köşeleri yumuşatır.
        let path = NSBezierPath()
        path.lineWidth = width
        path.lineCapStyle = .round
        path.lineJoinStyle = .round
        path.move(to: pts[0])
        if pts.count > 2 {
            var prev = pts[0]
            for i in 1..<(pts.count - 1) {
                let q = pts[i]
                let mid = CGPoint(x: (q.x + pts[i + 1].x) / 2, y: (q.y + pts[i + 1].y) / 2)
                path.curve(to: mid,
                           controlPoint1: CGPoint(x: prev.x + 2 / 3 * (q.x - prev.x), y: prev.y + 2 / 3 * (q.y - prev.y)),
                           controlPoint2: CGPoint(x: mid.x + 2 / 3 * (q.x - mid.x), y: mid.y + 2 / 3 * (q.y - mid.y)))
                prev = mid
            }
        }
        path.line(to: pts[pts.count - 1])
        path.stroke()
    }
}

final class LineShape: Shape {
    private let a: CGPoint
    private var b: CGPoint
    private let arrow: Bool

    init(_ start: CGPoint, color: NSColor, width: CGFloat, arrow: Bool) {
        a = start
        b = start
        self.arrow = arrow
        super.init(color: color, width: width)
    }

    override func update(_ p: CGPoint, shift: Bool) {
        b = shift ? Shape.snap45(a, p) : p
    }

    override var isEmpty: Bool { abs(b.x - a.x) < 3 && abs(b.y - a.y) < 3 }

    override var bounds: CGRect {
        let pad = arrow ? max(12, width * 4) : width
        return Shape.box(a, b).insetBy(dx: -pad, dy: -pad)
    }

    override func draw() {
        color.set()
        let path = NSBezierPath()
        path.lineWidth = width
        path.lineCapStyle = .round
        if !arrow {
            path.move(to: a)
            path.line(to: b)
            path.stroke()
            return
        }

        let dx = b.x - a.x, dy = b.y - a.y
        let len = (dx * dx + dy * dy).squareRoot()
        if len < 1 { return }
        let ux = dx / len, uy = dy / len
        let head = min(max(12, width * 4), len)
        let half = head * 0.5
        let bx = b.x - ux * head, by = b.y - uy * head

        // Gövde ok başının içinde biter, yuvarlak uç başın dışına taşmaz.
        path.move(to: a)
        path.line(to: CGPoint(x: b.x - ux * head * 0.6, y: b.y - uy * head * 0.6))
        path.stroke()
        let tip = NSBezierPath()
        tip.move(to: b)
        tip.line(to: CGPoint(x: bx - uy * half, y: by + ux * half))
        tip.line(to: CGPoint(x: bx + uy * half, y: by - ux * half))
        tip.close()
        tip.fill()
    }
}

final class RectShape: Shape {
    private let a: CGPoint
    private var b: CGPoint

    init(_ start: CGPoint, color: NSColor, width: CGFloat) {
        a = start
        b = start
        super.init(color: color, width: width)
    }

    override func update(_ p: CGPoint, shift: Bool) {
        if shift {
            let dx = p.x - a.x, dy = p.y - a.y
            let d = max(abs(dx), abs(dy))
            b = CGPoint(x: a.x + (dx < 0 ? -d : d), y: a.y + (dy < 0 ? -d : d))
        } else {
            b = p
        }
    }

    override var isEmpty: Bool { abs(b.x - a.x) < 2 && abs(b.y - a.y) < 2 }
    override var bounds: CGRect { Shape.box(a, b).insetBy(dx: -width, dy: -width) }

    override func draw() {
        color.set()
        let path = NSBezierPath(rect: Shape.box(a, b))
        path.lineWidth = width
        path.lineJoinStyle = .miter
        path.stroke()
    }
}

final class TextShape: Shape {
    private let p: CGPoint
    var text = ""

    init(_ at: CGPoint, color: NSColor, width: CGFloat) {
        p = at
        super.init(color: color, width: width)
    }

    override var isEmpty: Bool { text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }

    private var font: NSFont { .systemFont(ofSize: 14 + width * 2.5, weight: .bold) }

    private func layout() -> (lines: [String], attrs: [NSAttributedString.Key: Any], lh: CGFloat, origin: CGPoint, maxW: CGFloat) {
        let f = font
        let attrs: [NSAttributedString.Key: Any] = [.font: f, .foregroundColor: color]
        let lines = text.components(separatedBy: "\n")
        let lh = ceil(f.ascender - f.descender + f.leading)
        let maxW = lines.map { ($0 as NSString).size(withAttributes: attrs).width }.max() ?? 0
        return (lines, attrs, lh, CGPoint(x: p.x, y: p.y - lh / 2), maxW)
    }

    override var bounds: CGRect {
        let l = layout()
        return CGRect(x: l.origin.x - 8, y: l.origin.y - 6, width: max(l.maxW, 8) + 20, height: CGFloat(l.lines.count) * l.lh + 12)
    }

    override func draw() { render(editing: false) }
    func drawEditing() { render(editing: true) }

    private func render(editing: Bool) {
        let l = layout()
        for (i, line) in l.lines.enumerated() {
            (line as NSString).draw(at: CGPoint(x: l.origin.x, y: l.origin.y + CGFloat(i) * l.lh), withAttributes: l.attrs)
        }
        if !editing { return }

        let lastW = (l.lines[l.lines.count - 1] as NSString).size(withAttributes: l.attrs).width
        let cx = l.origin.x + lastW + 1
        let cy = l.origin.y + CGFloat(l.lines.count - 1) * l.lh
        color.set()
        let caret = NSBezierPath()
        caret.lineWidth = max(1.5, (14 + width * 2.5) / 14)
        caret.move(to: CGPoint(x: cx, y: cy + 2))
        caret.line(to: CGPoint(x: cx, y: cy + l.lh - 2))
        caret.stroke()

        NSColor(white: 1, alpha: 170 / 255).set()
        let frame = NSBezierPath(rect: CGRect(x: l.origin.x - 5, y: l.origin.y - 3,
                                              width: max(l.maxW, 8) + 12, height: CGFloat(l.lines.count) * l.lh + 6))
        frame.lineWidth = 1
        frame.setLineDash([4, 3], count: 2, phase: 0)
        frame.stroke()
    }
}
