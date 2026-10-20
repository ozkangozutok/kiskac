import AppKit

// Derleme sırasında uygulama simgesi üretmek için: build.sh bu modu çağırır.
let args = CommandLine.arguments
if args.count == 3 && args[1] == "--export-iconset" {
    do {
        try Logo.writeIconset(to: URL(fileURLWithPath: args[2]))
        exit(0)
    } catch {
        FileHandle.standardError.write("Simge üretilemedi: \(error)\n".data(using: .utf8)!)
        exit(1)
    }
}

// Gizli iç denetimler (bkz. SelfTest.swift)
if args.count >= 2 && (args[1] == "--selftest" || args[1] == "--video-test") {
    let param = args.count >= 3 ? args[2] : nil
    let video = args[1] == "--video-test"
    Task { @MainActor in
        let ok = video ? await SelfTest.videoTest(dir: param) : await SelfTest.run(log: param)
        exit(ok ? 0 : 1)
    }
    dispatchMain()
}

MainActor.assumeIsolated {
    let app = NSApplication.shared
    let delegate = AppDelegate()
    app.delegate = delegate
    app.setActivationPolicy(.accessory)
    withExtendedLifetime(delegate) { app.run() }
}
