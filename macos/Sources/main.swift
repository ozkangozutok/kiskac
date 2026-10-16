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

MainActor.assumeIsolated {
    let app = NSApplication.shared
    let delegate = AppDelegate()
    app.delegate = delegate
    app.setActivationPolicy(.accessory)
    withExtendedLifetime(delegate) { app.run() }
}
