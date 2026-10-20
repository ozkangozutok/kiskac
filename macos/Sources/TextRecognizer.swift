import AppKit
import NaturalLanguage
import Vision

// Görüntüdeki yazıyı macOS'un yerleşik metin tanımasıyla (Vision) okur. Ek indirme ve internet bağlantısı yoktur.
// Vision Türkçe bilmiyorsa (macOS 15'te bilmiyor) ş, ğ, ı harflerini s, g, i okur; Türkçe satırlardaki
// kelimeler bu yüzden sistemin Türkçe yazım denetleyicisiyle düzeltilir (TurkishFixer).
enum TextRecognizer {
    struct Failure: LocalizedError {
        let errorDescription: String?
    }

    // Satırları alt alta döndürür; yazı yoksa boş dize. Okuma arka planda, düzeltme ana iş parçacığında yürür.
    static func recognize(_ image: CGImage) async throws -> String {
        let (lines, native) = try await Task.detached(priority: .userInitiated) { try readLines(image) }.value
        if native || lines.isEmpty { return lines.joined(separator: "\n") }
        return await TurkishFixer.fix(lines).joined(separator: "\n")
    }

    // Vision'ın bu sistemde Türkçe tanıyıp tanımadığı (yeni macOS sürümlerinde gelebilir)
    static var supportsTurkish: Bool {
        let langs = (try? VNRecognizeTextRequest().supportedRecognitionLanguages()) ?? []
        return langs.contains { $0.hasPrefix("tr") }
    }

    // (satırlar, Vision Türkçeyi kendisi tanıdı mı)
    static func readLines(_ image: CGImage) throws -> ([String], Bool) {
        let native = supportsTurkish
        let req = VNRecognizeTextRequest()
        req.recognitionLevel = .accurate
        // Türkçe yoksa dil düzeltmesi kelimeleri İngilizceye benzetip bozuyor ("İade" → "lade").
        req.recognitionLanguages = native ? ["tr-TR", "en-US"] : ["en-US"]
        req.usesLanguageCorrection = native

        // Küçük yazılar büyütülünce daha iyi tanınır (Windows sürümüyle aynı ölçek).
        let scale = image.height < 150 ? 3 : image.height < 400 ? 2 : 1
        let input = scale > 1 ? upscaled(image, scale) ?? image : image
        do {
            try VNImageRequestHandler(cgImage: input, options: [:]).perform([req])
        } catch {
            throw Failure(errorDescription: "Metin tanınamadı: \(error.localizedDescription)")
        }
        return (orderedLines(req.results ?? []), native)
    }

    private static func upscaled(_ img: CGImage, _ s: Int) -> CGImage? {
        guard let g = CGContext(data: nil, width: img.width * s, height: img.height * s, bitsPerComponent: 8, bytesPerRow: 0,
                                space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                bitmapInfo: CGImageAlphaInfo.noneSkipFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue)
        else { return nil }
        g.interpolationQuality = .high
        g.draw(img, in: CGRect(x: 0, y: 0, width: img.width * s, height: img.height * s))
        return g.makeImage()
    }

    // Parçalar yukarıdan aşağı satırlara toplanır; aynı satırdakiler soldan sağa, aralarında boşlukla birleşir.
    private static func orderedLines(_ obs: [VNRecognizedTextObservation]) -> [String] {
        let items = obs.compactMap { o -> (CGRect, String)? in
            guard let s = o.topCandidates(1).first?.string.trimmingCharacters(in: .whitespaces), !s.isEmpty else { return nil }
            return (o.boundingBox, s)
        }.sorted { $0.0.maxY > $1.0.maxY } // Vision'da orijin sol alttadır

        var rows: [[(CGRect, String)]] = []
        for it in items {
            if let first = rows.last?.first, it.0.midY >= first.0.minY && it.0.midY <= first.0.maxY {
                rows[rows.count - 1].append(it)
            } else {
                rows.append([it])
            }
        }
        return rows.map { $0.sorted { $0.0.minX < $1.0.minX }.map(\.1).joined(separator: " ") }
    }
}

// Türkçe harf düzeltmesi: s→ş, g→ğ, i→ı, c→ç, o→ö, u→ü (büyük harflerde I↔İ) değişimleriyle yazım
// denetleyicisinin önerdiği kelime seçilir. Yalnızca Türkçe olduğu anlaşılan satırlara uygulanır.
// Yaklaşıktır: iki biçimi de geçerli olan kelimelerde (sis/şiş) yanılabilir.
@MainActor
enum TurkishFixer {
    private static let tr = Locale(identifier: "tr_TR")
    private static let allowed: [Character: Set<Character>] = [
        "s": ["ş"], "g": ["ğ"], "i": ["ı"], "ı": ["i"], "c": ["ç"], "o": ["ö"], "u": ["ü"],
    ]
    // Çok uzun metinde ekranı kilitlemesin: süre dolunca kalan kelimeler olduğu gibi bırakılır.
    private static let budget: TimeInterval = 8

    static func fix(_ lines: [String]) async -> [String] {
        let sc = NSSpellChecker.shared
        guard sc.availableLanguages.contains("tr") else { return lines }
        let overall = NLLanguageRecognizer.dominantLanguage(for: lines.joined(separator: "\n"))
        let deadline = Date().addingTimeInterval(budget)
        var cache: [String: String] = [:]
        var out: [String] = []

        for line in lines {
            // Kısa satırlarda dil tespiti güvenilmez: tüm metnin dili geçerli.
            let words = line.split(separator: " ").count
            let lang = words >= 3 ? NLLanguageRecognizer.dominantLanguage(for: line) : overall
            guard lang == .turkish else {
                out.append(line)
                continue
            }
            var result = ""
            var word = ""
            for ch in line + "\u{0}" {
                if ch.isLetter {
                    word.append(ch)
                    continue
                }
                if !word.isEmpty {
                    if let c = cache[word] {
                        result += c
                    } else if Date() < deadline {
                        let f = fixWord(word, sc)
                        cache[word] = f
                        result += f
                        await Task.yield() // menü çubuğu ve diğer olaylar beklemesin
                    } else {
                        result += word
                    }
                    word = ""
                }
                if ch != "\u{0}" { result.append(ch) }
            }
            out.append(result)
        }
        return out
    }

    static func fixWord(_ w: String, _ sc: NSSpellChecker = .shared) -> String {
        guard w.count >= 2, let first = w.first else { return w }
        let upper = w.filter(\.isUppercase).count, letters = w.count
        let caps = upper >= 2 && upper * 2 >= letters // OCR büyük harfli kelimede bazen küçük i okur (SiSLI)
        let low = caps ? w.lowercased(with: tr)
            : first.isUppercase ? String(first).lowercased(with: tr) + w.dropFirst() : w
        guard low.contains(where: { allowed[$0] != nil }) else { return w }

        let valid = sc.checkSpelling(of: low, startingAt: 0, language: "tr", wrap: false,
                                     inSpellDocumentWithTag: 0, wordCount: nil).location == NSNotFound
        let guesses = sc.guesses(forWordRange: NSRange(location: 0, length: (low as NSString).length), in: low,
                                 language: "tr", inSpellDocumentWithTag: 0) ?? []
        // Hatalıysa uyan ilk öneri; denetleyici kabul ettiyse (Türkçe ekler sayesinde "islemleri" de geçer)
        // yalnızca ilk öneri uyuyorsa.
        let pick = valid ? guesses.first.flatMap { matches($0, low) ? $0 : nil } : guesses.first { matches($0, low) }
        guard let p = pick else { return w }
        if caps { return p.uppercased(with: tr) }
        if first.isUppercase, let pf = p.first { return String(pf).uppercased(with: tr) + p.dropFirst() }
        return p
    }

    // Öneri, kelimeden yalnızca izin verilen harf değişimleriyle mi ayrılıyor
    private static func matches(_ cand: String, _ low: String) -> Bool {
        let a = Array(cand), b = Array(low)
        guard a.count == b.count, a != b else { return false }
        for (x, y) in zip(a, b) where x != y {
            if !(allowed[y]?.contains(x) ?? false) { return false }
        }
        return true
    }
}
