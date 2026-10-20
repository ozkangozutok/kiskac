import Foundation

// Hareketli GIF yazar (Windows'taki GifSink'in karşılığı). ImageIO'nun GIF kodlayıcısı bütün kareleri dosya
// kapanana kadar bellekte işlemeden tuttuğu için kendi kodlayıcımız var:
//  • her kareye kendi 256 renklik paleti (medyan kesim),
//  • yalnızca önceki kareden değişen dikdörtgen yazılır, değişmeyen kareler öncekinin süresine eklenir,
//  • LZW sıkıştırma (GIF89a).
final class GifWriter {
    static let maxSide = 1280

    let width: Int
    let height: Int
    private let file: FileHandle
    private var out: [UInt8] = []
    private var prev: [UInt32]
    private var cur: [UInt32]
    private var hasPrev = false
    private let quantizer = Quantizer()
    private let lzw = LzwEncoder()

    private struct Rect { var x = 0, y = 0, w = 0, h = 0 }

    private struct Frame {
        var r: Rect
        var palette: [UInt32]
        var bits: Int
        var indices: [UInt8]
        var time: Double
    }

    // Yazılmayı bekleyen kare: süresi bir sonraki farklı kare gelince belli olur.
    private var pending: Frame?
    private var lastEnd: Double = 0

    init(url: URL, width: Int, height: Int) throws {
        self.width = width
        self.height = height
        prev = [UInt32](repeating: 0, count: width * height)
        cur = prev
        guard FileManager.default.createFile(atPath: url.path, contents: nil) else {
            throw CocoaError(.fileWriteUnknown, userInfo: [NSFilePathErrorKey: url.path])
        }
        file = try FileHandle(forWritingTo: url)

        put("GIF89a")
        putShort(width)
        putShort(height)
        out += [0x70, 0, 0] // genel renk tablosu yok, renk çözünürlüğü 8 bit
        // Sonsuz döngü (NETSCAPE2.0)
        out += [0x21, 0xFF, 0x0B]
        put("NETSCAPE2.0")
        out += [0x03, 0x01, 0x00, 0x00, 0x00]
    }

    // bgra: üstten alta BGRA satırlar (en az width*4 bayt). time ve duration saniye cinsinden.
    func write(bgra: UnsafeRawPointer, bytesPerRow: Int, time: Double, duration: Double) throws {
        let w = width
        cur.withUnsafeMutableBytes { dst in
            for y in 0..<height {
                memcpy(dst.baseAddress! + y * w * 4, bgra + y * bytesPerRow, w * 4)
            }
        }
        lastEnd = time + duration

        let r: Rect
        if !hasPrev {
            r = Rect(x: 0, y: 0, w: width, h: height)
        } else if let c = changed() {
            r = c
        } else {
            return // değişiklik yok: önceki kare daha uzun görünür
        }
        try flush(endTime: time)
        pending = encode(r, time)
        swap(&prev, &cur) // cur bir sonraki karede baştan yazılır
        hasPrev = true
    }

    func finish() throws {
        try flush(endTime: lastEnd)
        out.append(0x3B) // dosya sonu
        try drain()
        try file.close()
    }

    // Önceki kareden farklı piksellerin sınır dikdörtgeni (alfa yok sayılır)
    private func changed() -> Rect? {
        let w = width, h = height
        return cur.withUnsafeBufferPointer { c in
            prev.withUnsafeBufferPointer { p in
                func differs(_ i: Int) -> Bool { (c[i] ^ p[i]) & 0xFFFFFF != 0 }
                func rowDiffers(_ y: Int) -> Bool {
                    let row = y * w
                    for x in 0..<w where differs(row + x) { return true }
                    return false
                }
                guard let top = (0..<h).first(where: rowDiffers) else { return nil }
                let bottom = (top..<h).reversed().first(where: rowDiffers) ?? top
                var left = w, right = -1
                for y in top...bottom {
                    let row = y * w
                    var x = 0
                    while x < left {
                        if differs(row + x) { left = x; break }
                        x += 1
                    }
                    x = w - 1
                    while x > right {
                        if differs(row + x) { right = x; break }
                        x -= 1
                    }
                }
                return Rect(x: left, y: top, w: right - left + 1, h: bottom - top + 1)
            }
        }
    }

    private func encode(_ r: Rect, _ time: Double) -> Frame {
        var px = [UInt32](repeating: 0, count: r.w * r.h)
        let w = width
        cur.withUnsafeBufferPointer { c in
            px.withUnsafeMutableBufferPointer { d in
                for y in 0..<r.h {
                    let src = (r.y + y) * w + r.x
                    for x in 0..<r.w { d[y * r.w + x] = c[src + x] }
                }
            }
        }
        var indices = [UInt8](repeating: 0, count: px.count)
        let palette = quantizer.quantize(px, &indices)
        var bits = 1
        while (1 << bits) < palette.count { bits += 1 }
        return Frame(r: r, palette: palette, bits: bits, indices: indices, time: time)
    }

    private func flush(endTime: Double) throws {
        guard let f = pending else { return }
        pending = nil

        // Mutlak zamanlar yuvarlanır: kısa kareler birikip toplam süreyi kaydırmasın.
        var delay = Int((endTime * 100).rounded()) - Int((f.time * 100).rounded())
        delay = max(2, min(65535, delay)) // tarayıcılar 2'nin altını 10 sayar

        // Grafik denetim uzantısı: imha yöntemi 1 (yerinde bırak), saydamlık yok
        out += [0x21, 0xF9, 0x04, 0x04]
        putShort(delay)
        out += [0, 0]

        // Görüntü tanımlayıcı + yerel renk tablosu
        out.append(0x2C)
        putShort(f.r.x)
        putShort(f.r.y)
        putShort(f.r.w)
        putShort(f.r.h)
        out.append(UInt8(0x80 | (f.bits - 1)))
        var table = [UInt8](repeating: 0, count: (1 << f.bits) * 3)
        for (i, c) in f.palette.enumerated() {
            table[i * 3] = UInt8((c >> 16) & 255)
            table[i * 3 + 1] = UInt8((c >> 8) & 255)
            table[i * 3 + 2] = UInt8(c & 255)
        }
        out += table

        lzw.encode(&out, f.indices, minCodeSize: max(2, f.bits))
        if out.count > 1 << 20 { try drain() }
    }

    private func drain() throws {
        guard !out.isEmpty else { return }
        try file.write(contentsOf: out)
        out.removeAll(keepingCapacity: true)
    }

    private func put(_ ascii: String) {
        out += Array(ascii.utf8)
    }

    private func putShort(_ v: Int) {
        out.append(UInt8(v & 255))
        out.append(UInt8((v >> 8) & 255))
    }
}

// Medyan kesim ile en fazla 256 renk seçer. Renkler 5-5-5 bit kutulara toplanır (32768 kutu); ekran
// görüntülerinde genelde 256'dan az kutu dolu olur ve o zaman renkler neredeyse birebir korunur.
private final class Quantizer {
    private var count = [Int](repeating: 0, count: 32768)
    private var sumR = [Int](repeating: 0, count: 32768)
    private var sumG = [Int](repeating: 0, count: 32768)
    private var sumB = [Int](repeating: 0, count: 32768)
    private var map = [Int](repeating: 0, count: 32768)

    @inline(__always) private static func key(_ c: UInt32) -> Int {
        let r: UInt32 = (c >> 19) & 31, g: UInt32 = (c >> 11) & 31, b: UInt32 = (c >> 3) & 31
        return Int(r << 10 | g << 5 | b)
    }

    func quantize(_ px: [UInt32], _ indices: inout [UInt8]) -> [UInt32] {
        for i in 0..<32768 {
            count[i] = 0
            sumR[i] = 0
            sumG[i] = 0
            sumB[i] = 0
        }
        px.withUnsafeBufferPointer { p in
            for c in p {
                let k = Self.key(c)
                count[k] += 1
                sumR[k] += Int((c >> 16) & 255)
                sumG[k] += Int((c >> 8) & 255)
                sumB[k] += Int(c & 255)
            }
        }

        var bins: [Int] = []
        for k in 0..<32768 where count[k] > 0 { bins.append(k) }

        let palette: [UInt32]
        if bins.count <= 256 {
            palette = bins.indices.map { i in
                map[bins[i]] = i
                return average(bins, i, 1)
            }
        } else {
            palette = medianCut(&bins, 256)
        }

        px.withUnsafeBufferPointer { p in
            indices.withUnsafeMutableBufferPointer { d in
                for i in 0..<p.count { d[i] = UInt8(map[Self.key(p[i])]) }
            }
        }
        return palette
    }

    private struct Box {
        var start: Int
        var length: Int
    }

    private func medianCut(_ bins: inout [Int], _ maxColors: Int) -> [UInt32] {
        var boxes = [Box(start: 0, length: bins.count)]
        while boxes.count < maxColors {
            // Bölünecek kutu: piksel sayısı × en geniş renk aralığı en büyük olan
            var best = -1, bestAxis = 0, bestScore = 0
            for (i, bx) in boxes.enumerated() where bx.length >= 2 {
                let (axis, range) = longestAxis(bins, bx)
                var pixels = 0
                for j in bx.start..<(bx.start + bx.length) { pixels += count[bins[j]] }
                let score = pixels * range
                if score > bestScore {
                    bestScore = score
                    best = i
                    bestAxis = axis
                }
            }
            if best < 0 { break }

            let b = boxes[best]
            let shift = bestAxis == 0 ? 10 : bestAxis == 1 ? 5 : 0
            let sorted = bins[b.start..<(b.start + b.length)].sorted { ($0 >> shift) & 31 < ($1 >> shift) & 31 }
            bins.replaceSubrange(b.start..<(b.start + b.length), with: sorted)

            // Piksel sayısına göre medyan
            var total = 0, half = 0
            for j in b.start..<(b.start + b.length) { total += count[bins[j]] }
            var split = b.start + 1
            for j in b.start..<(b.start + b.length - 1) {
                half += count[bins[j]]
                split = j + 1
                if half * 2 >= total { break }
            }
            boxes[best] = Box(start: b.start, length: split - b.start)
            boxes.append(Box(start: split, length: b.start + b.length - split))
        }

        return boxes.indices.map { i in
            for j in boxes[i].start..<(boxes[i].start + boxes[i].length) { map[bins[j]] = i }
            return average(bins, boxes[i].start, boxes[i].length)
        }
    }

    private func longestAxis(_ bins: [Int], _ b: Box) -> (axis: Int, range: Int) {
        var rMin = 31, rMax = 0, gMin = 31, gMax = 0, bMin = 31, bMax = 0
        for j in b.start..<(b.start + b.length) {
            let k = bins[j]
            let r = k >> 10, g = (k >> 5) & 31, bl = k & 31
            rMin = min(rMin, r); rMax = max(rMax, r)
            gMin = min(gMin, g); gMax = max(gMax, g)
            bMin = min(bMin, bl); bMax = max(bMax, bl)
        }
        let rr = rMax - rMin, gr = gMax - gMin, br = bMax - bMin
        if gr >= rr && gr >= br { return (1, gr) }
        if rr >= br { return (0, rr) }
        return (2, br)
    }

    private func average(_ bins: [Int], _ start: Int, _ length: Int) -> UInt32 {
        var n = 0, r = 0, g = 0, b = 0
        for j in start..<(start + length) {
            let k = bins[j]
            n += count[k]
            r += sumR[k]
            g += sumG[k]
            b += sumB[k]
        }
        if n == 0 { return 0 }
        return UInt32((r / n) << 16 | (g / n) << 8 | (b / n))
    }
}

// GIF LZW kodlayıcı (değişken kod uzunluğu, en fazla 12 bit). Klasik compress algoritması;
// sözlük her temizlemede silinmek yerine "kuşak" numarasıyla geçersiz kılınır.
private final class LzwEncoder {
    private static let maxBits = 12, maxCodeLimit = 4096
    private var dict = [Int32](repeating: 0, count: 1 << 20)
    private var gen = [Int32](repeating: 0, count: 1 << 20)
    private var generation: Int32 = 0

    private var block = [UInt8](repeating: 0, count: 256)
    private var blockLen = 0
    private var acc = 0, accBits = 0
    private var initBits = 0, codeSize = 0, maxCode = 0, clearCode = 0, eoiCode = 0, nextCode = 0
    private var clearFlag = false

    func encode(_ out: inout [UInt8], _ pixels: [UInt8], minCodeSize: Int) {
        out.append(UInt8(minCodeSize))
        blockLen = 0
        acc = 0
        accBits = 0

        initBits = minCodeSize + 1
        codeSize = initBits
        maxCode = (1 << codeSize) - 1
        clearCode = 1 << minCodeSize
        eoiCode = clearCode + 1
        nextCode = clearCode + 2
        clearFlag = false
        generation += 1

        output(clearCode, &out)
        pixels.withUnsafeBufferPointer { px in
            var ent = px.isEmpty ? 0 : Int(px[0])
            var i = 1
            while i < px.count {
                let c = Int(px[i])
                i += 1
                let key = (c << Self.maxBits) + ent
                if gen[key] == generation {
                    ent = Int(dict[key])
                    continue
                }
                output(ent, &out)
                ent = c
                if nextCode < Self.maxCodeLimit {
                    dict[key] = Int32(nextCode)
                    gen[key] = generation
                    nextCode += 1
                } else {
                    // Sözlük doldu: temizle
                    generation += 1
                    nextCode = clearCode + 2
                    clearFlag = true
                    output(clearCode, &out)
                }
            }
            output(ent, &out)
        }
        output(eoiCode, &out)

        if blockLen > 0 { flushBlock(&out) }
        out.append(0) // blok sonu
    }

    private func output(_ code: Int, _ out: inout [UInt8]) {
        acc |= code << accBits
        accBits += codeSize
        while accBits >= 8 {
            putByte(UInt8(acc & 255), &out)
            acc >>= 8
            accBits -= 8
        }

        // Kod uzunluğu, son eklenen kod sığmayınca bir sonraki çıktıdan itibaren artar.
        if nextCode > maxCode || clearFlag {
            if clearFlag {
                codeSize = initBits
                maxCode = (1 << codeSize) - 1
                clearFlag = false
            } else {
                codeSize += 1
                maxCode = codeSize == Self.maxBits ? Self.maxCodeLimit : (1 << codeSize) - 1
            }
        }

        if code == eoiCode {
            while accBits > 0 {
                putByte(UInt8(acc & 255), &out)
                acc >>= 8
                accBits -= 8
            }
            accBits = 0
            acc = 0
        }
    }

    private func putByte(_ b: UInt8, _ out: inout [UInt8]) {
        block[blockLen] = b
        blockLen += 1
        if blockLen == 255 { flushBlock(&out) }
    }

    private func flushBlock(_ out: inout [UInt8]) {
        out.append(UInt8(blockLen))
        out += block[0..<blockLen]
        blockLen = 0
    }
}
