import AppKit
import ScreenCaptureKit

struct ScreenShot {
    let screen: NSScreen
    let image: CGImage
}

extension NSScreen {
    var displayID: CGDirectDisplayID {
        (deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value ?? 0
    }
}

enum ScreenCapture {
    static var hasPermission: Bool { CGPreflightScreenCaptureAccess() }

    // Tüm ekranları aynı anda, her birini kendi piksel çözünürlüğünde (Retina) yakalar.
    @MainActor
    static func captureAll() async throws -> [ScreenShot] {
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
        let jobs: [(Int, SCDisplay, Int, Int)] = NSScreen.screens.enumerated().compactMap { i, screen in
            guard let display = content.displays.first(where: { $0.displayID == screen.displayID }) else { return nil }
            let s = screen.backingScaleFactor
            return (i, display, Int((screen.frame.width * s).rounded()), Int((screen.frame.height * s).rounded()))
        }

        let images = try await withThrowingTaskGroup(of: (Int, CGImage).self) { group in
            for (i, display, w, h) in jobs {
                group.addTask {
                    let cfg = SCStreamConfiguration()
                    cfg.width = w
                    cfg.height = h
                    cfg.showsCursor = false
                    cfg.captureResolution = .best
                    let filter = SCContentFilter(display: display, excludingWindows: [])
                    let img = try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: cfg)
                    return (i, img)
                }
            }
            var out: [Int: CGImage] = [:]
            for try await (i, img) in group { out[i] = img }
            return out
        }

        let screens = NSScreen.screens
        return images.keys.sorted().compactMap { i in
            i < screens.count ? ScreenShot(screen: screens[i], image: images[i]!) : nil
        }
    }
}
