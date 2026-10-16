import AppKit
import CryptoKit
import Security

// GitHub Releases üzerinden güncelleme: en son sürümü denetler, Kiskac-macOS.zip'i indirir,
// özetini ve kod imzasını doğrular, uygulama kapanınca eskisinin yerine koyup yeniden açar.
@MainActor
final class Updater {
    static let repo = "ozkangozutok/kiskac"
    static let assetName = "Kiskac-macOS.zip"
    static let teamID = "XZPCTAZZ9W"
    static let bundleID = "com.kiskac.app"
    private static let checkInterval: TimeInterval = 24 * 3600

    struct Release: Decodable {
        struct Asset: Decodable {
            let name: String
            let browser_download_url: URL
            let digest: String?
        }
        let tag_name: String
        let name: String?
        let body: String?
        let html_url: URL
        let draft: Bool
        let prerelease: Bool
        let assets: [Asset]

        var version: String { tag_name.hasPrefix("v") ? String(tag_name.dropFirst()) : tag_name }
    }

    enum UpdateError: LocalizedError {
        case message(String)
        var errorDescription: String? { if case .message(let m) = self { return m }; return nil }
    }

    static var currentVersion: String {
        Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "0"
    }

    private var busy = false
    private var timer: Timer?
    var onStateChange: ((String?) -> Void)? // menüde gösterilecek durum (nil: boşta)
    // Seçim ekranı açıkken otomatik denetim pencere açmaz (pencere ekranın altında kalıp onu kilitlerdi).
    var canInterrupt: () -> Bool = { true }

    // Açılıştan biraz sonra ve sonra her birkaç saatte bir; son denetim 24 saatten eskiyse.
    func startAutomaticChecks() {
        DispatchQueue.main.asyncAfter(deadline: .now() + 20) { [weak self] in self?.checkIfDue() }
        timer = Timer.scheduledTimer(withTimeInterval: 3 * 3600, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.checkIfDue() }
        }
    }

    private func checkIfDue() {
        guard Settings.autoUpdate, canInterrupt() else { return }
        if let last = Settings.lastUpdateCheck, Date().timeIntervalSince(last) < Self.checkInterval { return }
        check(userInitiated: false)
    }

    func check(userInitiated: Bool) {
        if busy { return }
        busy = true
        Task {
            defer { busy = false }
            do {
                let r = try await fetchLatest()
                // İstek sürerken çekim başladıysa sonraki denetime bırakılır.
                if !userInitiated && !canInterrupt() { return }
                Settings.lastUpdateCheck = Date()
                let newer = Self.compare(r.version, Self.currentVersion) > 0
                if newer && (userInitiated || Settings.skippedVersion != r.version) {
                    offer(r)
                } else if userInitiated {
                    info("Kıskaç güncel", "Kullandığın \(Self.currentVersion) sürümü en son sürüm.")
                }
            } catch {
                if userInitiated { info("Güncellemeler denetlenemedi", error.localizedDescription, style: .warning) }
            }
        }
    }

    private func fetchLatest() async throws -> Release {
        var req = URLRequest(url: URL(string: "https://api.github.com/repos/\(Self.repo)/releases/latest")!)
        req.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        req.setValue("Kiskac/\(Self.currentVersion)", forHTTPHeaderField: "User-Agent")
        req.timeoutInterval = 20
        let (data, resp) = try await URLSession.shared.data(for: req)
        guard (resp as? HTTPURLResponse)?.statusCode == 200 else {
            throw UpdateError.message("GitHub yanıt vermedi (HTTP \((resp as? HTTPURLResponse)?.statusCode ?? 0)).")
        }
        return try JSONDecoder().decode(Release.self, from: data)
    }

    // "1.10.0" > "1.9.2": sayısal karşılaştırma
    static func compare(_ a: String, _ b: String) -> Int {
        let x = a.split(separator: ".").map { Int($0.prefix { $0.isNumber }) ?? 0 }
        let y = b.split(separator: ".").map { Int($0.prefix { $0.isNumber }) ?? 0 }
        for i in 0..<max(x.count, y.count) {
            let p = i < x.count ? x[i] : 0, q = i < y.count ? y[i] : 0
            if p != q { return p < q ? -1 : 1 }
        }
        return 0
    }

    private func offer(_ r: Release) {
        let a = NSAlert()
        a.messageText = "Kıskaç \(r.version) hazır"
        a.informativeText = "Kullandığın sürüm: \(Self.currentVersion). Güncelleme indirilip kurulduktan sonra Kıskaç yeniden açılır."
        a.addButton(withTitle: "Şimdi güncelle")
        a.addButton(withTitle: "Sonra")
        a.addButton(withTitle: "Bu sürümü atla")
        if let body = r.body, !body.isEmpty { a.accessoryView = notesView(body) }
        NSApp.activate(ignoringOtherApps: true)
        switch a.runModal() {
        case .alertFirstButtonReturn:
            Task { await install(r) }
        case .alertThirdButtonReturn:
            Settings.skippedVersion = r.version
        default:
            break
        }
    }

    private func notesView(_ markdown: String) -> NSView {
        let scroll = NSScrollView(frame: CGRect(x: 0, y: 0, width: 380, height: 180))
        scroll.hasVerticalScroller = true
        scroll.borderType = .bezelBorder
        let tv = NSTextView(frame: scroll.bounds)
        tv.isEditable = false
        tv.textContainerInset = NSSize(width: 6, height: 6)
        tv.autoresizingMask = [.width]
        let opts = AttributedString.MarkdownParsingOptions(interpretedSyntax: .inlineOnlyPreservingWhitespace)
        if let s = try? NSAttributedString(AttributedString(markdown: markdown, options: opts)) {
            let m = NSMutableAttributedString(attributedString: s)
            m.addAttributes([.foregroundColor: NSColor.labelColor], range: NSRange(location: 0, length: m.length))
            m.enumerateAttribute(.font, in: NSRange(location: 0, length: m.length)) { v, range, _ in
                let bold = (v as? NSFont)?.fontDescriptor.symbolicTraits.contains(.bold) ?? false
                m.addAttribute(.font, value: NSFont.systemFont(ofSize: 12, weight: bold ? .semibold : .regular), range: range)
            }
            tv.textStorage?.setAttributedString(m)
        } else {
            tv.string = markdown
        }
        scroll.documentView = tv
        return scroll
    }

    // ---------------------------------------------------------------- kurulum

    private func install(_ r: Release) async {
        guard let asset = r.assets.first(where: { $0.name == Self.assetName }) else {
            info("Güncelleme bulunamadı", "\(r.tag_name) sürümünde \(Self.assetName) yok.", style: .warning)
            NSWorkspace.shared.open(r.html_url)
            return
        }
        onStateChange?("Güncelleme indiriliyor…")
        defer { onStateChange?(nil) }

        let target = Bundle.main.bundleURL
        let parent = target.deletingLastPathComponent()
        guard FileManager.default.isWritableFile(atPath: parent.path) else {
            info("Güncelleme kurulamadı",
                 "\(parent.path) klasörüne yazma iznin yok. Yeni sürümü indirme sayfasından elle kurabilirsin.", style: .warning)
            NSWorkspace.shared.open(r.html_url)
            return
        }

        // Yeni sürüm hedefle aynı klasörde hazırlanır; yer değiştirme tek bir taşıma olur.
        let staging = parent.appendingPathComponent(".Kiskac-update-\(UUID().uuidString)", isDirectory: true)
        do {
            let newApp = try await prepare(asset, version: r.version, staging: staging)
            try relaunch(replacing: target, with: newApp, staging: staging)
        } catch {
            try? FileManager.default.removeItem(at: staging)
            info("Güncelleme kurulamadı", error.localizedDescription, style: .warning)
        }
    }

    // İndirir, özeti denetler, açar ve doğrular; hazır Kiskac.app yolunu döner.
    func prepare(_ asset: Release.Asset, version: String, staging: URL) async throws -> URL {
        var req = URLRequest(url: asset.browser_download_url)
        req.setValue("Kiskac/\(Self.currentVersion)", forHTTPHeaderField: "User-Agent")
        let (file, resp) = try await URLSession.shared.download(for: req)
        defer { try? FileManager.default.removeItem(at: file) }
        guard (resp as? HTTPURLResponse)?.statusCode == 200 else {
            throw UpdateError.message("İndirme başarısız (HTTP \((resp as? HTTPURLResponse)?.statusCode ?? 0)).")
        }

        if let digest = asset.digest, digest.hasPrefix("sha256:") {
            let data = try Data(contentsOf: file, options: .mappedIfSafe)
            let hex = SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
            guard hex == digest.dropFirst(7).lowercased() else {
                throw UpdateError.message("İndirilen dosyanın özeti tutmuyor.")
            }
        }

        try FileManager.default.createDirectory(at: staging, withIntermediateDirectories: true)
        try run("/usr/bin/ditto", ["-x", "-k", file.path, staging.path])
        let newApp = staging.appendingPathComponent("Kiskac.app")
        try verify(newApp, expectedVersion: version)
        try? run("/usr/bin/xattr", ["-dr", "com.apple.quarantine", newApp.path])
        return newApp
    }

    private func verify(_ app: URL, expectedVersion: String) throws {
        guard let b = Bundle(url: app), b.bundleIdentifier == Self.bundleID else {
            throw UpdateError.message("İndirilen paket Kıskaç değil.")
        }
        let v = b.infoDictionary?["CFBundleShortVersionString"] as? String ?? ""
        guard Self.compare(v, expectedVersion) == 0 else {
            throw UpdateError.message("İndirilen sürüm (\(v)) beklenenle (\(expectedVersion)) aynı değil.")
        }
        // Çalışan uygulama Developer ID ile imzalıysa yenisi de aynı ekipten olmak zorunda.
        let teamReq = "anchor apple generic and identifier \"\(Self.bundleID)\" and certificate leaf[subject.OU] = \"\(Self.teamID)\""
        let mustMatchTeam = Self.satisfies(Bundle.main.bundleURL, teamReq)
        guard Self.satisfies(app, mustMatchTeam ? teamReq : nil) else {
            throw UpdateError.message("İndirilen uygulamanın imzası doğrulanamadı.")
        }
    }

    static func satisfies(_ url: URL, _ requirement: String?) -> Bool {
        var code: SecStaticCode?
        guard SecStaticCodeCreateWithPath(url as CFURL, [], &code) == errSecSuccess, let code else { return false }
        var req: SecRequirement?
        if let r = requirement, SecRequirementCreateWithString(r as CFString, [], &req) != errSecSuccess { return false }
        let flags = SecCSFlags(rawValue: kSecCSCheckAllArchitectures | kSecCSCheckNestedCode | kSecCSStrictValidate)
        return SecStaticCodeCheckValidity(code, flags, req) == errSecSuccess
    }

    // Uygulama kapandıktan sonra eskisini yenisiyle değiştirip açan küçük kabuk betiği.
    private func relaunch(replacing target: URL, with newApp: URL, staging: URL) throws {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/sh")
        p.arguments = ["-c", Self.swapScript, "kiskac-update", String(getpid()), newApp.path, target.path, staging.path]
        try p.run()
        NSApp.terminate(nil)
    }

    // $1: beklenecek süreç, $2: yeni uygulama, $3: hedef, $4: hazırlık klasörü
    static let swapScript = """
        while kill -0 "$1" 2>/dev/null; do sleep 0.2; done
        old="$3.old-$$"
        if mv "$3" "$old"; then
            if mv "$2" "$3"; then rm -rf "$old"; else mv "$old" "$3"; fi
        fi
        rm -rf "$4"
        open "$3"
        """

    private func run(_ tool: String, _ args: [String]) throws {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: tool)
        p.arguments = args
        try p.run()
        p.waitUntilExit()
        guard p.terminationStatus == 0 else {
            throw UpdateError.message("\((tool as NSString).lastPathComponent) başarısız (\(p.terminationStatus)).")
        }
    }

    private func info(_ title: String, _ text: String, style: NSAlert.Style = .informational) {
        let a = NSAlert()
        a.messageText = title
        a.informativeText = text
        a.alertStyle = style
        NSApp.activate(ignoringOtherApps: true)
        a.runModal()
    }
}
