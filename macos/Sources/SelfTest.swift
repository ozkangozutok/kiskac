import AVFoundation
import AppKit
import Carbon.HIToolbox
import ImageIO

// Gizli iç denetimler (Windows ve Linux'taki --selftest'in karşılığı):
//   Kiskac --selftest [günlük]        kısayol, kayıt saati, ses karışımı, GIF, MP4 ve Türkçe metin tanıma
//   Kiskac --video-test <klasör>      gerçek ekran kaydı (2 sn MP4 + GIF); ekran kaydı izni yoksa SKIP
// Başarısızlıkta çıkış kodu 1.
@MainActor
enum SelfTest {
    private static var log = ""
    private static var failed = 0

    private static func check(_ name: String, _ ok: Bool, _ detail: String = "") {
        let line = (ok ? "OK    " : "FAIL  ") + name + (detail.isEmpty ? "" : "  (\(detail))")
        log += line + "\n"
        print(line)
        if !ok { failed += 1 }
    }

    private static func note(_ s: String) {
        log += s + "\n"
        print(s)
    }

    private static func finish(_ path: String?) -> Bool {
        note(failed == 0 ? "Tüm denetimler geçti." : "\(failed) denetim başarısız.")
        if let path { try? log.write(toFile: path, atomically: true, encoding: .utf8) }
        return failed == 0
    }

    static func run(log path: String?) async -> Bool {
        hotKeys()
        clock()
        mixer()
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("kiskac-selftest-\(getpid())")
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        gif(dir)
        await mp4(dir)
        await ocr()
        return finish(path)
    }

    // ---------------------------------------------------------------- kısayol

    private static func hotKeys() {
        let main = HotKey(keyCode: kVK_ANSI_2, modifiers: cmdKey | shiftKey)
        check("Video kısayolu ⇧⌘2 → ⌥⇧⌘2", main.videoVariant == HotKey(keyCode: kVK_ANSI_2, modifiers: cmdKey | shiftKey | optionKey))
        let f13 = HotKey(keyCode: kVK_F13, modifiers: 0)
        check("Video kısayolu F13 → ⇧F13", f13.videoVariant == HotKey(keyCode: kVK_F13, modifiers: shiftKey))
        let full = HotKey(keyCode: kVK_ANSI_X, modifiers: cmdKey | shiftKey | optionKey)
        check("⌥⇧⌘X için video kısayolu yok", full.videoVariant == nil)
    }

    // ---------------------------------------------------------------- kayıt saati

    private static func clock() {
        let c = RecordingClock()
        c.start(at: 100)
        c.setPaused(true, at: 101)
        check("Saat: duraklamada zaman durur", c.now(at: 102) == 1)
        c.setPaused(false, at: 103)
        check("Saat: duraklamadaki an kayda girmez", c.toRecording(102) == nil)
        check("Saat: duraklama süresi çıkarılır", c.toRecording(104) == 2)
        check("Saat: başlangıçtan önceki an kayda girmez", c.toRecording(99) == nil)
        check("Saat: geçen süre", c.now(at: 105) == 3)
    }

    // ---------------------------------------------------------------- ses karışımı

    private static func pcm(_ frames: Int, _ value: Float, channels: AVAudioChannelCount = 2) -> AVAudioPCMBuffer {
        let f = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: AudioMixer.rate, channels: channels, interleaved: false)!
        let b = AVAudioPCMBuffer(pcmFormat: f, frameCapacity: AVAudioFrameCount(frames))!
        b.frameLength = AVAudioFrameCount(frames)
        for c in 0..<Int(channels) {
            for i in 0..<frames { b.floatChannelData![c][i] = value }
        }
        return b
    }

    private static func mixer() {
        let m = AudioMixer()
        m.mode = [.system, .microphone]
        m.setGain(.microphone, 0.5)
        m.add(pcm(4800, 0.25), source: .system, at: 0)
        m.add(pcm(4800, 0.5, channels: 1), source: .microphone, at: 0.05)
        // 2 ms oynamayla gelen sonraki paket kesintisiz devam etmeli
        m.add(pcm(4800, 0.25), source: .system, at: 0.102)
        guard let s = m.take(upTo: 9600) else { return check("Karışım: örnek üretildi", false) }
        check("Karışım: uzunluk", s.count == 9600 * 2)
        check("Karışım: tek kaynak", abs(s[100 * 2] - 0.25) < 1e-6, "\(s[200])")
        check("Karışım: kazançla toplanır (sol)", abs(s[3000 * 2] - 0.5) < 1e-6, "\(s[6000])")
        check("Karışım: mono kaynak iki kanala", abs(s[3000 * 2 + 1] - 0.5) < 1e-6)
        // Paket 4896. kareye değil, beklenen 4800. kareye oturmalı (arada mikrofon tek başına kalmamalı).
        check("Karışım: sonraki paket boşluksuz", abs(s[4850 * 2] - 0.5) < 1e-6 && abs(s[9599 * 2] - 0.25) < 1e-6,
              "\(s[9700]) \(s[19198])")
        m.mode = [.system]
        m.setGain(.system, 2)
        m.add(pcm(480, 0.8), source: .system, at: 0.2)
        let c = m.take(upTo: 9600 + 480) ?? []
        check("Karışım: taşma kırpılır", c.first == 1, "\(c.first ?? -1)")
        m.mode = []
        m.add(pcm(480, 0.8), source: .system, at: 0.21)
        let z = m.take(upTo: 9600 + 960) ?? []
        check("Karışım: kapalı kaynak sessiz", z.allSatisfy { $0 == 0 })
    }

    // ---------------------------------------------------------------- GIF

    private static func bgra(_ w: Int, _ h: Int, _ color: (Int, Int) -> UInt32) -> [UInt32] {
        var px = [UInt32](repeating: 0, count: w * h)
        for y in 0..<h { for x in 0..<w { px[y * w + x] = color(x, y) } }
        return px
    }

    private static func pixel(_ img: CGImage, _ x: Int, _ y: Int) -> (Int, Int, Int) {
        var p = [UInt8](repeating: 0, count: 4)
        let g = CGContext(data: &p, width: 1, height: 1, bitsPerComponent: 8, bytesPerRow: 4, space: CGColorSpace(name: CGColorSpace.sRGB)!,
                          bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        g.draw(img, in: CGRect(x: -x, y: -(img.height - 1 - y), width: img.width, height: img.height))
        return (Int(p[0]), Int(p[1]), Int(p[2]))
    }

    private static func gif(_ dir: URL) {
        let url = dir.appendingPathComponent("deneme.gif")
        let w = 40, h = 30
        let redPx = bgra(w, h) { _, _ in 0xFFEB2D2D }
        let half = bgra(w, h) { x, _ in x < w / 2 ? 0xFF007DFF : 0xFFEB2D2D }
        do {
            let g = try GifWriter(url: url, width: w, height: h)
            for (px, t) in [(redPx, 0.0), (redPx, 0.1), (half, 0.2)] {
                try px.withUnsafeBytes { try g.write(bgra: $0.baseAddress!, bytesPerRow: w * 4, time: t, duration: 0.1) }
            }
            try g.finish()
        } catch {
            return check("GIF yazıldı", false, error.localizedDescription)
        }
        guard let src = CGImageSourceCreateWithURL(url as CFURL, nil) else { return check("GIF okundu", false) }
        check("GIF: değişmeyen kare birleşir (2 kare)", CGImageSourceGetCount(src) == 2, "\(CGImageSourceGetCount(src))")
        func delay(_ i: Int) -> Double {
            let p = CGImageSourceCopyPropertiesAtIndex(src, i, nil) as? [CFString: Any]
            let d = p?[kCGImagePropertyGIFDictionary] as? [CFString: Any]
            return (d?[kCGImagePropertyGIFUnclampedDelayTime] as? Double) ?? -1
        }
        check("GIF: süreler (0,2 + 0,1 sn)", abs(delay(0) - 0.2) < 0.001 && abs(delay(1) - 0.1) < 0.001, "\(delay(0)) \(delay(1))")
        if let f0 = CGImageSourceCreateImageAtIndex(src, 0, nil), let f1 = CGImageSourceCreateImageAtIndex(src, 1, nil) {
            check("GIF: boyut", f0.width == w && f0.height == h)
            let a = pixel(f0, 30, 10), b = pixel(f1, 5, 10), c = pixel(f1, 30, 10)
            check("GIF: renkler", a == (235, 45, 45) && b == (0, 125, 255) && c == (235, 45, 45), "\(a) \(b) \(c)")
        } else {
            check("GIF: kareler çözüldü", false)
        }

        // 256'dan çok renk: medyan kesim
        let url2 = dir.appendingPathComponent("degrade.gif")
        let gw = 128, gh = 128
        let grad = bgra(gw, gh) { x, y in 0xFF000000 | UInt32(x * 2) << 16 | UInt32(y * 2) << 8 | UInt32((x + y) & 255) }
        do {
            let g = try GifWriter(url: url2, width: gw, height: gh)
            try grad.withUnsafeBytes { try g.write(bgra: $0.baseAddress!, bytesPerRow: gw * 4, time: 0, duration: 0.1) }
            try g.finish()
            guard let s2 = CGImageSourceCreateWithURL(url2 as CFURL, nil), let img = CGImageSourceCreateImageAtIndex(s2, 0, nil) else {
                return check("GIF: degrade çözüldü", false)
            }
            var worst = 0
            for (x, y) in [(0, 0), (64, 64), (127, 0), (0, 127), (100, 30)] {
                let p = pixel(img, x, y)
                worst = max(worst, abs(p.0 - x * 2), abs(p.1 - y * 2), abs(p.2 - ((x + y) & 255)))
            }
            check("GIF: 256'dan çok renkte sapma küçük", worst <= 40, "en çok \(worst)")
        } catch {
            check("GIF: degrade yazıldı", false, error.localizedDescription)
        }
    }

    // ---------------------------------------------------------------- MP4

    private static func mp4(_ dir: URL) async {
        let url = dir.appendingPathComponent("deneme.mp4")
        let w = 320, h = 240, fps = 30
        let writer: Mp4Writer
        do {
            writer = try Mp4Writer(url: url, width: w, height: h, fps: fps, realTime: false)
        } catch {
            return check("MP4 açıldı", false, error.localizedDescription)
        }
        func wait(_ ready: () -> Bool) {
            let end = Date().addingTimeInterval(2)
            while !ready() && Date() < end { usleep(2000) }
        }
        var audioPos = 0
        for i in 0..<fps {
            var pb: CVPixelBuffer?
            guard let pool = writer.pool, CVPixelBufferPoolCreatePixelBuffer(nil, pool, &pb) == kCVReturnSuccess, let pb else {
                return check("MP4: kare tamponu", false)
            }
            CVPixelBufferLockBaseAddress(pb, [])
            let base = CVPixelBufferGetBaseAddress(pb)!.assumingMemoryBound(to: UInt32.self)
            let stride = CVPixelBufferGetBytesPerRow(pb) / 4
            for y in 0..<h { for x in 0..<w { base[y * stride + x] = x < i * w / fps ? 0xFFEB2D2D : 0xFF202024 } }
            CVPixelBufferUnlockBaseAddress(pb, [])
            wait { writer.videoReady }
            writer.appendVideo(pb, index: Int64(i))
            // Her kareye 1600 örnek (48000 / 30) 440 Hz
            var s = [Float](repeating: 0, count: 1600 * 2)
            for k in 0..<1600 {
                let v = Float(sin(Double(audioPos + k) * 2 * .pi * 440 / AudioMixer.rate) * 0.3)
                s[k * 2] = v
                s[k * 2 + 1] = v
            }
            audioPos += 1600
            wait { writer.audioReady }
            writer.appendAudio(s)
        }
        check("MP4: tüm kareler yazıldı", writer.frameCount == fps, "\(writer.frameCount)")
        let err: Error? = await withCheckedContinuation { c in writer.finish(endTime: 1) { c.resume(returning: $0) } }
        check("MP4: dosya bitirildi", err == nil, err?.localizedDescription ?? "")
        let asset = AVURLAsset(url: url)
        do {
            let d = try await asset.load(.duration).seconds
            let v = try await asset.loadTracks(withMediaType: .video)
            let a = try await asset.loadTracks(withMediaType: .audio)
            check("MP4: süre ≈ 1 sn", abs(d - 1) < 0.05, String(format: "%.3f", d))
            check("MP4: bir video ve bir ses izi", v.count == 1 && a.count == 1, "\(v.count) + \(a.count)")
            if let t = v.first {
                let size = try await t.load(.naturalSize)
                check("MP4: çözünürlük", size == CGSize(width: w, height: h), "\(size)")
            }
        } catch {
            check("MP4 okundu", false, error.localizedDescription)
        }
    }

    // ---------------------------------------------------------------- metin tanıma

    private static func render(_ lines: [String]) -> CGImage {
        let scale: CGFloat = 2, size: CGFloat = 14
        let w = Int(760 * scale), h = Int(CGFloat(lines.count) * size * 1.7 * scale + 20 * scale)
        let g = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpace(name: CGColorSpace.sRGB)!,
                          bitmapInfo: CGImageAlphaInfo.noneSkipFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue)!
        g.setFillColor(CGColor(gray: 1, alpha: 1))
        g.fill(CGRect(x: 0, y: 0, width: w, height: h))
        g.scaleBy(x: scale, y: scale)
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(cgContext: g, flipped: false)
        for (i, l) in lines.enumerated() {
            (l as NSString).draw(at: CGPoint(x: 10, y: CGFloat(h) / scale - 10 - CGFloat(i + 1) * size * 1.7),
                                 withAttributes: [.font: NSFont.systemFont(ofSize: size), .foregroundColor: NSColor.black])
        }
        NSGraphicsContext.restoreGraphicsState()
        return g.makeImage()!
    }

    private static func ocr() async {
        let img = render([
            "Siparişiniz kargoya verildi.",
            "Müşteri hizmetleri: İSTANBUL",
            "This is an English line.",
        ])
        let start = Date()
        do {
            let text = try await TextRecognizer.recognize(img)
            let ms = Int(Date().timeIntervalSince(start) * 1000)
            note("      Vision Türkçe: \(TextRecognizer.supportsTurkish ? "var" : "yok, yazım denetleyicisiyle düzeltiliyor") • \(ms) ms")
            for l in text.split(separator: "\n") { note("      │ \(l)") }
            let lines = text.split(separator: "\n").map(String.init)
            check("Metin: satır sırası", lines.count == 3 && lines[0].hasPrefix("Sipari") && lines[2].hasPrefix("This"))
            check("Metin: Türkçe harfler (Siparişiniz, Müşteri, İSTANBUL)",
                  text.contains("Siparişiniz") && text.contains("Müşteri") && text.contains("İSTANBUL"))
            check("Metin: İngilizce satır değişmez", text.contains("This is an English line."))
        } catch {
            check("Metin tanındı", false, error.localizedDescription)
        }
    }

    // ---------------------------------------------------------------- gerçek ekran kaydı

    // Dosyanın ses izindeki en yüksek örnek (0–1)
    private static func audioPeak(_ asset: AVURLAsset) async -> Float {
        guard let track = try? await asset.loadTracks(withMediaType: .audio).first,
              let reader = try? AVAssetReader(asset: asset) else { return 0 }
        let out = AVAssetReaderTrackOutput(track: track, outputSettings: [
            AVFormatIDKey: kAudioFormatLinearPCM, AVLinearPCMBitDepthKey: 32, AVLinearPCMIsFloatKey: true,
            AVLinearPCMIsNonInterleaved: false, AVLinearPCMIsBigEndianKey: false,
        ])
        reader.add(out)
        guard reader.startReading() else { return 0 }
        var peak: Float = 0
        while let sb = out.copyNextSampleBuffer() {
            guard let block = CMSampleBufferGetDataBuffer(sb) else { continue }
            var length = 0
            var ptr: UnsafeMutablePointer<CChar>?
            guard CMBlockBufferGetDataPointer(block, atOffset: 0, lengthAtOffsetOut: nil, totalLengthOut: &length,
                                              dataPointerOut: &ptr) == noErr, let ptr else { continue }
            ptr.withMemoryRebound(to: Float.self, capacity: length / 4) { f in
                for i in 0..<(length / 4) { peak = max(peak, abs(f[i])) }
            }
        }
        return peak
    }

    static func videoTest(dir: String?) async -> Bool {
        let out = URL(fileURLWithPath: dir ?? FileManager.default.currentDirectoryPath, isDirectory: true)
        try? FileManager.default.createDirectory(at: out, withIntermediateDirectories: true)
        let logPath = out.appendingPathComponent("video-test.log").path
        guard ScreenCapture.hasPermission else {
            note("SKIP  ekran kaydı izni yok (Sistem Ayarları › Gizlilik ve Güvenlik › Ekran ve Sistem Sesi Kaydı)")
            return finish(logPath)
        }
        guard let screen = NSScreen.screens.first else {
            check("Ekran bulundu", false)
            return finish(logPath)
        }
        let f = screen.frame.size
        let region = CGRect(x: (f.width / 4).rounded(), y: (f.height / 4).rounded(), width: (f.width / 2).rounded(), height: (f.height / 2).rounded())
        for (format, name, target) in [(VideoFormat.mp4, "video-test.mp4", RecordingTarget.region(screen, region)),
                                       (.gif, "video-test.gif", .region(screen, region)),
                                       (.mp4, "video-test-tum.mp4", .allScreens())] {
            let url = out.appendingPathComponent(name)
            try? FileManager.default.removeItem(at: url)
            let opts = RecordingOptions(target: target, format: format, fps: format == .gif ? 10 : 30, cursor: true,
                                        audio: format == .gif ? [] : .system, systemVolume: 100, micVolume: 100, url: url)
            let rec = ScreenRecorder(options: opts)
            var peak: Float = 0
            // Kıskaç'ın kendi sesi kayda girmez; sistem sesi ayrı bir süreçten (afplay) çalınır.
            let sound = format == .mp4 ? Process.launchedProcess(launchPath: "/bin/sh", arguments: [
                "-c", "for i in 1 2 3 4 5 6; do /usr/bin/afplay /System/Library/Sounds/Glass.aiff; done",
            ]) : nil
            let result: (URL, Error?, String?) = await withCheckedContinuation { c in
                rec.finished = { c.resume(returning: ($0, $1, $2)) }
                rec.start()
                Task { @MainActor in
                    for step in 0..<50 { // 50 × 50 ms = 2,5 sn, ortadaki 0,5 sn duraklatılmış
                        if step == 20 { rec.isPaused = true }
                        if step == 30 { rec.isPaused = false }
                        peak = max(peak, rec.level(of: .system))
                        try? await Task.sleep(nanoseconds: 50_000_000)
                    }
                    rec.stop()
                }
            }
            sound?.terminate()
            check("\(name) kaydedildi", result.1 == nil, result.1?.localizedDescription ?? "")
            if let w = result.2 { note("      uyarı: \(w)") }
            guard result.1 == nil else { continue }
            if format == .gif {
                let src = CGImageSourceCreateWithURL(url as CFURL, nil)
                let n = src.map(CGImageSourceGetCount) ?? 0
                let img = src.flatMap { CGImageSourceCreateImageAtIndex($0, 0, nil) }
                check("\(name): \(rec.width)×\(rec.height), \(n) kare", img?.width == rec.width && img?.height == rec.height && n >= 1)
            } else {
                let asset = AVURLAsset(url: url)
                let d = (try? await asset.load(.duration).seconds) ?? 0
                let v = (try? await asset.loadTracks(withMediaType: .video)) ?? []
                let a = (try? await asset.loadTracks(withMediaType: .audio)) ?? []
                let size = try? await v.first?.load(.naturalSize)
                // 2,5 sn kayıt − 0,5 sn duraklama ≈ 2 sn
                check("\(name): süre ≈ 2 sn (duraklama çıkarılmış)", abs(d - 2) < 0.4, String(format: "%.2f sn", d))
                check("\(name): \(rec.width)×\(rec.height), video + ses izi",
                      size == CGSize(width: rec.width, height: rec.height) && v.count == 1 && a.count == 1, "\(String(describing: size))")
                check("\(name): sistem sesi geldi (çubuk göstergesi)", peak > 0.01, String(format: "en yüksek %.3f", peak))
                let filePeak = await audioPeak(asset)
                check("\(name): dosyadaki ses sessiz değil", filePeak > 0.01, String(format: "en yüksek %.3f", filePeak))
            }
        }
        return finish(logPath)
    }
}
