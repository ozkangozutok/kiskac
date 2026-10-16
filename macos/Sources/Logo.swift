import AppKit
import ImageIO
import UniformTypeIdentifiers

// Kıskaç logosu: mercan zemin, beyaz yengeç kıskacı, ağzında sarı bir ekran parçası.
// Windows sürümündeki Logo.cs ile aynı geometri; Core Graphics ile çizilir.
// Tüm çizimler 32 birimlik, y ekseni aşağı bakan ızgarada yapılır.
enum Logo {
    static let bgTop = CGColor(srgbRed: 255 / 255, green: 122 / 255, blue: 69 / 255, alpha: 1)
    static let bgBottom = CGColor(srgbRed: 226 / 255, green: 36 / 255, blue: 90 / 255, alpha: 1)
    static let piece = CGColor(srgbRed: 255 / 255, green: 214 / 255, blue: 72 / 255, alpha: 1)

    // Isırık çemberinin merkezi gövdenin içinde olduğu için ağız kenarda daralır
    // ve çeneler içe doğru kıvrılır: kıskaç görünümü buradan gelir.
    static let bodyCenter = CGPoint(x: 14.5, y: 17.5)
    static let bodyRadius: CGFloat = 10
    static let biteCenter = CGPoint(x: 19.2, y: 12.3)
    static let biteRadius: CGFloat = 5.6
    static let pieceCenter = CGPoint(x: 19.6, y: 11.9)

    // Logo (zemin dahil). Bağlamın 32x32 birim, y aşağı olacak şekilde dönüştürülmüş olması beklenir.
    static func draw(in g: CGContext, pixelSize: CGFloat) {
        let bg = CGPath(roundedRect: CGRect(x: 1, y: 1, width: 30, height: 30), cornerWidth: 7.5, cornerHeight: 7.5, transform: nil)
        let rgb = CGColorSpace(name: CGColorSpace.sRGB)!

        g.saveGState()
        g.addPath(bg)
        g.clip()
        let grad = CGGradient(colorsSpace: rgb, colors: [bgTop, bgBottom] as CFArray, locations: [0, 1])!
        g.drawLinearGradient(grad, start: CGPoint(x: 1, y: 1), end: CGPoint(x: 31, y: 31),
                             options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])
        // üstte hafif parlama
        g.clip(to: CGRect(x: 0, y: 0, width: 32, height: 15.9))
        let gloss = CGGradient(colorsSpace: rgb, colors: [CGColor(gray: 1, alpha: 50 / 255), CGColor(gray: 1, alpha: 0)] as CFArray,
                               locations: [0, 1])!
        g.drawLinearGradient(gloss, start: .zero, end: CGPoint(x: 0, y: 16), options: [])
        g.restoreGState()

        let claw = clawWithArm()
        if pixelSize >= 24 {
            // gölge: kıskaç ve kol tek parça gibi kararsın diye saydamlık katmanında çizilir
            g.saveGState()
            g.translateBy(x: 0, y: 1.1)
            g.setAlpha(55 / 255)
            g.beginTransparencyLayer(auxiliaryInfo: nil)
            g.setFillColor(CGColor(srgbRed: 90 / 255, green: 0, blue: 30 / 255, alpha: 1))
            for p in claw { g.addPath(p); g.fillPath() }
            g.endTransparencyLayer()
            g.restoreGState()
        }
        g.setFillColor(.white)
        for p in claw { g.addPath(p); g.fillPath() }

        // Kıskacın ağzındaki ekran parçası (eğik)
        g.saveGState()
        g.translateBy(x: pieceCenter.x, y: pieceCenter.y)
        g.rotate(by: -12 * .pi / 180)
        let ps: CGFloat = pixelSize <= 20 ? 6.4 : 5.6
        let pp = CGPath(roundedRect: CGRect(x: -ps / 2, y: -ps / 2, width: ps, height: ps),
                        cornerWidth: ps * 0.22, cornerHeight: ps * 0.22, transform: nil)
        g.addPath(pp)
        g.setFillColor(piece)
        g.fillPath()
        if pixelSize >= 32 {
            g.addPath(pp)
            g.setStrokeColor(CGColor(srgbRed: 150 / 255, green: 60 / 255, blue: 0, alpha: 120 / 255))
            g.setLineWidth(0.5)
            g.strokePath()
        }
        g.restoreGState()
    }

    // Kıskaç ve sol alta uzanan, yuvarlak biten kısa bilek.
    static func clawWithArm() -> [CGPath] {
        let arm = CGMutablePath()
        arm.move(to: bodyCenter)
        arm.addLine(to: CGPoint(x: 7, y: 25))
        return [clawPath(), arm.copy(strokingWithWidth: 6.5, lineCap: .round, lineJoin: .round, miterLimit: 10)]
    }

    // Büyük bir daireden ikinci bir daireyi "ısırarak" iki sivri çeneli kıskaç oluşturur.
    static func clawPath() -> CGPath {
        let c1 = bodyCenter, c2 = biteCenter
        let r1 = bodyRadius, r2 = biteRadius
        let dx = c2.x - c1.x, dy = c2.y - c1.y
        let d = (dx * dx + dy * dy).squareRoot()
        let a = (r1 * r1 - r2 * r2 + d * d) / (2 * d)
        let h = (r1 * r1 - a * a).squareRoot()
        let ux = dx / d, uy = dy / d
        let pA = CGPoint(x: c1.x + a * ux - h * uy, y: c1.y + a * uy + h * ux)
        let pB = CGPoint(x: c1.x + a * ux + h * uy, y: c1.y + a * uy - h * ux)

        let toBite = deg(atan2(dy, dx))
        let toBody = deg(atan2(-dy, -dx))

        // Gövde çemberinde, ısırık tarafını içermeyen uzun yay
        let aA = angle(c1, pA), aB = angle(c1, pB)
        var start = aA, sweep = mod(aB - aA)
        if inSweep(start, sweep, toBite) {
            start = aB
            sweep = mod(aA - aB)
        }
        let end = start == aA ? pB : pA
        let begin = start == aA ? pA : pB

        // Isırık çemberinde, gövdenin içinde kalan (içbükey) yay: end -> begin
        let e2 = angle(c2, end), b2 = angle(c2, begin)
        let cw = mod(b2 - e2)
        let sweep2 = inSweep(e2, cw, toBody) ? cw : -(360 - cw)

        let p = CGMutablePath()
        p.addArc(center: c1, radius: r1, startAngle: rad(start), endAngle: rad(start + sweep), clockwise: false)
        p.addArc(center: c2, radius: r2, startAngle: rad(e2), endAngle: rad(e2 + sweep2), clockwise: sweep2 < 0)
        p.closeSubpath()
        return p
    }

    private static func deg(_ r: CGFloat) -> CGFloat { r * 180 / .pi }
    private static func rad(_ d: CGFloat) -> CGFloat { d * .pi / 180 }
    private static func mod(_ v: CGFloat) -> CGFloat { let m = v.truncatingRemainder(dividingBy: 360); return m < 0 ? m + 360 : m }
    private static func angle(_ c: CGPoint, _ p: CGPoint) -> CGFloat { deg(atan2(p.y - c.y, p.x - c.x)) }
    private static func inSweep(_ start: CGFloat, _ sweep: CGFloat, _ a: CGFloat) -> Bool { mod(a - start) <= sweep }

    // Menü çubuğu simgesi: tek renk şablon görüntü, açık/koyu menü çubuğuna kendiliğinden uyar.
    static func menuBarImage() -> NSImage {
        let img = NSImage(size: NSSize(width: 18, height: 18), flipped: true) { r in
            guard let g = NSGraphicsContext.current?.cgContext else { return false }
            g.scaleBy(x: r.width / 28, y: r.height / 28)
            g.translateBy(x: -2.5, y: -1.5) // kıskaç 32'lik ızgarada yaklaşık 2.5..29.5 aralığında
            g.setFillColor(.black)
            for p in clawWithArm() { g.addPath(p); g.fillPath() }
            // ekran parçası: çenelerden ince bir boşlukla ayrılmış dolu kare
            g.translateBy(x: pieceCenter.x, y: pieceCenter.y)
            g.rotate(by: -12 * .pi / 180)
            func square(_ s: CGFloat) -> CGPath {
                CGPath(roundedRect: CGRect(x: -s / 2, y: -s / 2, width: s, height: s),
                       cornerWidth: s * 0.22, cornerHeight: s * 0.22, transform: nil)
            }
            g.setBlendMode(.clear)
            g.addPath(square(9))
            g.fillPath()
            g.setBlendMode(.normal)
            g.addPath(square(6.2))
            g.fillPath()
            return true
        }
        img.isTemplate = true
        img.accessibilityDescription = "Kıskaç"
        return img
    }

    // macOS simge ızgarası: 1024'lük tuvalde gövde 824 piksel, çevresinde boşluk ve hafif gölge.
    static func appIcon(pixels: Int) -> CGImage {
        let n = CGFloat(pixels)
        let g = CGContext(data: nil, width: pixels, height: pixels, bitsPerComponent: 8, bytesPerRow: 0,
                          space: CGColorSpace(name: CGColorSpace.sRGB)!,
                          bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        g.interpolationQuality = .high
        g.translateBy(x: 0, y: n)
        g.scaleBy(x: 1, y: -1)
        let unit = n * (824.0 / 1024.0) / 30.0
        g.translateBy(x: n / 2 - 16 * unit, y: n / 2 - 16 * unit)
        g.scaleBy(x: unit, y: unit)
        if pixels >= 64 {
            g.setShadow(offset: CGSize(width: 0, height: -n * 0.01), blur: n * 0.025,
                        color: CGColor(gray: 0, alpha: 0.3))
            g.beginTransparencyLayer(auxiliaryInfo: nil)
            draw(in: g, pixelSize: n)
            g.endTransparencyLayer()
        } else {
            draw(in: g, pixelSize: n)
        }
        return g.makeImage()!
    }

    static func writeIconset(to dir: URL) throws {
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        for base in [16, 32, 128, 256, 512] {
            for scale in [1, 2] {
                let name = "icon_\(base)x\(base)\(scale == 2 ? "@2x" : "").png"
                let url = dir.appendingPathComponent(name) as CFURL
                guard let dest = CGImageDestinationCreateWithURL(url, UTType.png.identifier as CFString, 1, nil) else {
                    throw CocoaError(.fileWriteUnknown)
                }
                CGImageDestinationAddImage(dest, appIcon(pixels: base * scale), nil)
                if !CGImageDestinationFinalize(dest) { throw CocoaError(.fileWriteUnknown) }
            }
        }
    }
}
