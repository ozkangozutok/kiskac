import AVFoundation
import AppKit
import CoreImage
import ScreenCaptureKit

enum VideoFormat { case mp4, gif }

// 0: ses yok, 1: sistem sesi, 2: mikrofon, 3: ikisi (değerler Windows sürümüyle aynı)
struct AudioMode: OptionSet {
    let rawValue: Int
    static let system = AudioMode(rawValue: 1)
    static let microphone = AudioMode(rawValue: 2)
}

// Kaydedilecek alan. Seçim ekranından gelen alan tek ekrandadır; "tüm ekranlar" her ekranı ayrı yakalayıp
// tek bir tuvalde birleştirir.
struct RecordingTarget {
    struct Part {
        let display: CGDirectDisplayID
        let rect: CGRect   // ekrandaki alan: nokta, sol üst orijin (ScreenCaptureKit'in sourceRect'i)
        let offset: CGPoint // tuvaldeki yeri: nokta, sol üst orijin
    }

    let parts: [Part]
    let size: CGSize    // tuval, nokta
    let scale: CGFloat  // nokta başına piksel (en yüksek)
    let frame: CGRect   // genel AppKit koordinatları (çerçeve ve kontrol çubuğu için)
    let wholeScreen: Bool

    // r: ekranın sol üst köşesine göre, nokta
    static func region(_ screen: NSScreen, _ r: CGRect) -> RecordingTarget {
        let f = screen.frame
        return RecordingTarget(parts: [Part(display: screen.displayID, rect: r, offset: .zero)], size: r.size,
                               scale: screen.backingScaleFactor,
                               frame: CGRect(x: f.minX + r.minX, y: f.maxY - r.maxY, width: r.width, height: r.height),
                               wholeScreen: r.size == f.size)
    }

    static func screen(_ s: NSScreen) -> RecordingTarget {
        region(s, CGRect(origin: .zero, size: s.frame.size))
    }

    static func allScreens() -> RecordingTarget {
        let screens = NSScreen.screens
        if screens.count == 1, let s = screens.first { return screen(s) }
        let u = screens.reduce(CGRect.null) { $0.union($1.frame) }
        let parts = screens.map { s in
            Part(display: s.displayID, rect: CGRect(origin: .zero, size: s.frame.size),
                 offset: CGPoint(x: s.frame.minX - u.minX, y: u.maxY - s.frame.maxY))
        }
        return RecordingTarget(parts: parts, size: u.size, scale: screens.map(\.backingScaleFactor).max() ?? 1,
                               frame: u, wholeScreen: true)
    }
}

struct RecordingOptions {
    var target: RecordingTarget
    var format: VideoFormat
    var fps: Int
    var cursor: Bool
    var audio: AudioMode
    var systemVolume: Int // yüzde
    var micVolume: Int    // yüzde
    var url: URL          // son dosya yolu
}

// Duraklatılabilen kayıt saati. Video ve ses aynı saati kullanır, böylece senkron kalır. "Mutlak" zaman
// sistemin ana saatidir (saniye); ScreenCaptureKit karelerinin ve ses paketlerinin zaman damgaları da onda.
final class RecordingClock: @unchecked Sendable {
    private let lock = NSLock()
    private var start = -1.0, pausedTotal = 0.0, pauseStart = -1.0
    private var pauses: [(Double, Double)] = [] // tamamlanmış duraklamalar

    static var hostNow: Double { CMClockGetTime(CMClockGetHostTimeClock()).seconds }

    var running: Bool { lock.withLock { start >= 0 } }

    func start(at abs: Double) {
        lock.withLock {
            guard start < 0 else { return }
            start = abs
            // İlk kare gelmeden duraklatıldıysa duraklama kaydın başında başlamış sayılır.
            if pauseStart >= 0 { pauseStart = max(pauseStart, abs) }
        }
    }

    var paused: Bool {
        get { lock.withLock { pauseStart >= 0 } }
        set { setPaused(newValue, at: Self.hostNow) }
    }

    func setPaused(_ p: Bool, at abs: Double) {
        lock.withLock {
            if p == (pauseStart >= 0) { return }
            if p {
                pauseStart = abs
            } else {
                pauses.append((pauseStart, abs))
                pausedTotal += abs - pauseStart
                pauseStart = -1
            }
        }
    }

    // Duraklamalar çıkarılmış geçen süre, saniye
    var now: Double { now(at: Self.hostNow) }

    func now(at abs: Double) -> Double {
        lock.withLock {
            if start < 0 { return 0 }
            let a = pauseStart >= 0 ? pauseStart : abs
            return max(0, a - start - pausedTotal)
        }
    }

    // Mutlak bir anın kayıttaki yeri. Kayıt öncesi ya da duraklama sırasındaysa nil.
    func toRecording(_ abs: Double) -> Double? {
        lock.withLock {
            if start < 0 || abs < start { return nil }
            if pauseStart >= 0 && abs >= pauseStart { return nil }
            var skipped = 0.0
            for p in pauses {
                if abs >= p.1 { skipped += p.1 - p.0 } else if abs >= p.0 { return nil }
            }
            return abs - start - skipped
        }
    }
}

// İki ses kaynağını (sistem sesi, mikrofon) kayıt zaman çizelgesinde 48 kHz stereo olarak karıştırır.
// Kapalı kaynağın yerine sessizlik yazılır; böylece kaynaklar kayıt sırasında açılıp kapatılabilir.
final class AudioMixer: @unchecked Sendable {
    static let rate = 48000.0
    static let sources: [AudioMode] = [.system, .microphone]

    private let lock = NSLock()
    private var mix: [Float] = []        // base karesinden başlayan, iç içe (L R L R…) örnekler
    private var base: Int64 = 0          // yazılmış kare sayısı
    private var expected: [Int64?] = [nil, nil] // kaynağın bir sonraki beklenen karesi (kayma düzeltmesi)
    private var gains: [Float] = [1, 1]
    private var enabled: AudioMode = []
    private var peaks: [(Float, Double)] = [(0, 0), (0, 0)]

    private static func index(_ m: AudioMode) -> Int { m == .microphone ? 1 : 0 }

    var mode: AudioMode {
        get { lock.withLock { enabled } }
        set { lock.withLock { enabled = newValue } }
    }

    func setGain(_ source: AudioMode, _ g: Float) {
        lock.withLock { gains[Self.index(source)] = g }
    }

    // Göstergedeki anlık seviye (0–1); kaynak susunca kısa sürede söner.
    func level(of source: AudioMode) -> Float {
        lock.withLock {
            let (p, t) = peaks[Self.index(source)]
            let age = RecordingClock.hostNow - t
            return age > 0.4 ? 0 : p * Float(1 - age / 0.4)
        }
    }

    // pcm: 48 kHz Float32 ayrık kanallar (1 ya da 2). at: kayıttaki zaman (saniye).
    func add(_ pcm: AVAudioPCMBuffer, source: AudioMode, at time: Double) {
        let n = Int(pcm.frameLength)
        guard n > 0, let ch = pcm.floatChannelData else { return }
        let i = Self.index(source)
        lock.lock()
        defer { lock.unlock() }
        guard enabled.contains(source) else {
            expected[i] = nil
            peaks[i] = (0, 0)
            return
        }
        // Ardışık paketler arasındaki küçük oynamalar yok sayılır; yoksa her pakette çıtırtı olur.
        var pos = Int64((time * Self.rate).rounded())
        if let e = expected[i], abs(pos - e) < Int64(Self.rate * 0.08) { pos = e }
        expected[i] = pos + Int64(n)

        let g = gains[i]
        let left = ch[0], right = pcm.format.channelCount > 1 ? ch[1] : ch[0]
        var peak: Float = 0
        let start = Int(pos - base)
        let first = max(0, -start) // zaten yazılmış kısım atlanır
        guard first < n else { return }
        let need = (start + n) * 2
        if mix.count < need { mix.append(contentsOf: repeatElement(0, count: need - mix.count)) }
        mix.withUnsafeMutableBufferPointer { m in
            for k in first..<n {
                let l = left[k] * g, r = right[k] * g
                m[(start + k) * 2] += l
                m[(start + k) * 2 + 1] += r
                peak = max(peak, abs(l), abs(r))
            }
        }
        peaks[i] = (min(peak, 1), RecordingClock.hostNow)
    }

    // frame karesine kadar karışımı verir (eksik yerler sessizlik), yazılmış sayılır.
    func take(upTo frame: Int64) -> [Float]? {
        lock.withLock {
            let n = Int(frame - base)
            guard n > 0 else { return nil }
            if mix.count < n * 2 { mix.append(contentsOf: repeatElement(0, count: n * 2 - mix.count)) }
            let out = mix[0..<(n * 2)].map { min(1, max(-1, $0)) }
            mix.removeFirst(n * 2)
            base = frame
            return out
        }
    }

    var framesTaken: Int64 { lock.withLock { base } }
}

// H.264 video ve AAC ses ile MP4 yazar. Video BGRA kareler, ses 48 kHz stereo Float32 alır.
final class Mp4Writer {
    let width: Int
    let height: Int
    let fps: Int
    private let writer: AVAssetWriter
    private let video: AVAssetWriterInput
    private let adaptor: AVAssetWriterInputPixelBufferAdaptor
    private let audio: AVAssetWriterInput
    private var audioFormat: CMAudioFormatDescription?
    private(set) var audioFrames: Int64 = 0
    private(set) var lastIndex: Int64 = -1
    private(set) var frameCount = 0

    // Apple'ın H.264 kodlayıcısı 4096×2304'ten büyük kareleri kabul etmez.
    static let maxSize = CGSize(width: 4096, height: 2304)

    init(url: URL, width: Int, height: Int, fps: Int, realTime: Bool = true) throws {
        self.width = width
        self.height = height
        self.fps = fps
        writer = try AVAssetWriter(outputURL: url, fileType: .mp4)

        let bitrate = max(2_000_000, min(40_000_000, width * height * fps / 6))
        video = AVAssetWriterInput(mediaType: .video, outputSettings: [
            AVVideoCodecKey: AVVideoCodecType.h264,
            AVVideoWidthKey: width,
            AVVideoHeightKey: height,
            AVVideoColorPropertiesKey: [
                AVVideoColorPrimariesKey: AVVideoColorPrimaries_ITU_R_709_2,
                AVVideoTransferFunctionKey: AVVideoTransferFunction_ITU_R_709_2,
                AVVideoYCbCrMatrixKey: AVVideoYCbCrMatrix_ITU_R_709_2,
            ],
            AVVideoCompressionPropertiesKey: [
                AVVideoAverageBitRateKey: bitrate,
                AVVideoExpectedSourceFrameRateKey: fps,
                AVVideoMaxKeyFrameIntervalKey: fps * 2,
                AVVideoProfileLevelKey: AVVideoProfileLevelH264HighAutoLevel,
            ],
        ])
        video.expectsMediaDataInRealTime = realTime
        adaptor = AVAssetWriterInputPixelBufferAdaptor(assetWriterInput: video, sourcePixelBufferAttributes: [
            kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA,
            kCVPixelBufferWidthKey as String: width,
            kCVPixelBufferHeightKey as String: height,
            kCVPixelBufferIOSurfacePropertiesKey as String: [:] as [String: Any],
        ])
        audio = AVAssetWriterInput(mediaType: .audio, outputSettings: [
            AVFormatIDKey: kAudioFormatMPEG4AAC,
            AVSampleRateKey: AudioMixer.rate,
            AVNumberOfChannelsKey: 2,
            AVEncoderBitRateKey: 192_000,
        ])
        audio.expectsMediaDataInRealTime = realTime
        guard writer.canAdd(video), writer.canAdd(audio) else { throw Self.failure("MP4 kodlayıcısı açılamadı.") }
        writer.add(video)
        writer.add(audio)

        var asbd = AudioStreamBasicDescription(
            mSampleRate: AudioMixer.rate, mFormatID: kAudioFormatLinearPCM,
            mFormatFlags: kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked,
            mBytesPerPacket: 8, mFramesPerPacket: 1, mBytesPerFrame: 8, mChannelsPerFrame: 2, mBitsPerChannel: 32, mReserved: 0)
        CMAudioFormatDescriptionCreate(allocator: nil, asbd: &asbd, layoutSize: 0, layout: nil, magicCookieSize: 0,
                                       magicCookie: nil, extensions: nil, formatDescriptionOut: &audioFormat)

        guard writer.startWriting() else {
            throw writer.error ?? Self.failure("MP4 dosyası açılamadı.")
        }
        writer.startSession(atSourceTime: .zero)
    }

    static func failure(_ s: String) -> Error {
        NSError(domain: "Kiskac", code: 1, userInfo: [NSLocalizedDescriptionKey: s])
    }

    var pool: CVPixelBufferPool? { adaptor.pixelBufferPool }
    var videoReady: Bool { video.isReadyForMoreMediaData }
    var audioReady: Bool { audio.isReadyForMoreMediaData }
    var failed: Error? { writer.status == .failed ? (writer.error ?? Self.failure("MP4 yazılamadı.")) : nil }

    // index: kare sırası (zaman = index / fps). Kodlayıcı hazır değilse kare atlanır.
    @discardableResult
    func appendVideo(_ pb: CVPixelBuffer, index: Int64) -> Bool {
        guard index > lastIndex, writer.status == .writing, video.isReadyForMoreMediaData else { return false }
        guard adaptor.append(pb, withPresentationTime: CMTime(value: index, timescale: CMTimeScale(fps))) else { return false }
        lastIndex = index
        frameCount += 1
        return true
    }

    // İç içe stereo örnekler, kaldığı yerden devam eder.
    @discardableResult
    func appendAudio(_ samples: [Float]) -> Bool {
        let n = samples.count / 2
        guard n > 0, writer.status == .writing, let fmt = audioFormat else { return false }
        let bytes = n * 8
        var block: CMBlockBuffer?
        guard CMBlockBufferCreateWithMemoryBlock(allocator: nil, memoryBlock: nil, blockLength: bytes, blockAllocator: nil,
                                                 customBlockSource: nil, offsetToData: 0, dataLength: bytes,
                                                 flags: kCMBlockBufferAssureMemoryNowFlag, blockBufferOut: &block) == noErr,
              let block else { return false }
        _ = samples.withUnsafeBytes { CMBlockBufferReplaceDataBytes(with: $0.baseAddress!, blockBuffer: block, offsetIntoDestination: 0, dataLength: bytes) }
        var sb: CMSampleBuffer?
        guard CMAudioSampleBufferCreateReadyWithPacketDescriptions(
            allocator: nil, dataBuffer: block, formatDescription: fmt, sampleCount: n,
            presentationTimeStamp: CMTime(value: audioFrames, timescale: CMTimeScale(AudioMixer.rate)),
            packetDescriptions: nil, sampleBufferOut: &sb) == noErr, let sb else { return false }
        audioFrames += Int64(n)
        return audio.append(sb)
    }

    func finish(endTime: Double, completion: @escaping @Sendable (Error?) -> Void) {
        if let e = failed {
            completion(e)
            return
        }
        video.markAsFinished()
        audio.markAsFinished()
        let lastFrameEnd = Double(lastIndex + 1) / Double(fps)
        writer.endSession(atSourceTime: CMTime(seconds: max(endTime, lastFrameEnd), preferredTimescale: 600))
        let w = writer
        writer.finishWriting {
            completion(w.status == .completed ? nil : (w.error ?? Self.failure("MP4 dosyası bitirilemedi.")))
        }
    }

    func cancel() {
        if writer.status == .writing { writer.cancelWriting() }
    }
}

// Mikrofon: varsayılan kayıt aygıtı, 48 kHz Float32. Zaman damgaları ana saate çevrilir.
final class MicCapture: NSObject, AVCaptureAudioDataOutputSampleBufferDelegate, @unchecked Sendable {
    private let session = AVCaptureSession()
    private let control = DispatchQueue(label: "Kıskaç mikrofon")
    private let queue: DispatchQueue
    private let onSample: (CMSampleBuffer, Double) -> Void
    private var configured = false

    init(queue: DispatchQueue, onSample: @escaping (CMSampleBuffer, Double) -> Void) {
        self.queue = queue
        self.onSample = onSample
    }

    static var authorized: Bool { AVCaptureDevice.authorizationStatus(for: .audio) == .authorized }

    // completion: mikrofon açıldı mı (kontrol kuyruğunda çağrılır)
    func start(_ completion: @escaping @Sendable (Bool) -> Void) {
        control.async { [self] in
            guard Self.authorized else { return completion(false) }
            if !configured {
                guard let dev = AVCaptureDevice.default(for: .audio), let input = try? AVCaptureDeviceInput(device: dev),
                      session.canAddInput(input) else { return completion(false) }
                session.addInput(input)
                let out = AVCaptureAudioDataOutput()
                out.audioSettings = [
                    AVFormatIDKey: kAudioFormatLinearPCM,
                    AVSampleRateKey: AudioMixer.rate,
                    AVLinearPCMBitDepthKey: 32,
                    AVLinearPCMIsFloatKey: true,
                    AVLinearPCMIsNonInterleaved: true,
                ]
                out.setSampleBufferDelegate(self, queue: queue)
                guard session.canAddOutput(out) else { return completion(false) }
                session.addOutput(out)
                configured = true
            }
            if !session.isRunning { session.startRunning() }
            completion(session.isRunning)
        }
    }

    func stop() {
        control.async { [self] in if session.isRunning { session.stopRunning() } }
    }

    func captureOutput(_ output: AVCaptureOutput, didOutput sb: CMSampleBuffer, from connection: AVCaptureConnection) {
        var pts = CMSampleBufferGetPresentationTimeStamp(sb)
        if let clock = session.synchronizationClock {
            pts = CMSyncConvertTime(pts, from: clock, to: CMClockGetHostTimeClock())
        }
        onSample(sb, pts.seconds)
    }
}

// Seçili alanı ScreenCaptureKit ile yakalayıp MP4 ya da GIF olarak yazar. Kareler ekran değiştikçe gelir;
// kare hızı sınırı ve zaman ızgarası (index / fps) uygulanır, değişmeyen ekranda son kare uzar.
final class ScreenRecorder: NSObject, SCStreamOutput, SCStreamDelegate, @unchecked Sendable {
    let options: RecordingOptions
    let clock = RecordingClock()
    private let mixer = AudioMixer()
    private let queue = DispatchQueue(label: "Kıskaç ekran kaydı", qos: .userInteractive)
    private let gifQueue = DispatchQueue(label: "Kıskaç GIF", qos: .userInitiated)
    private let temp: URL
    let width: Int
    let height: Int

    // Yalnızca queue üzerinde
    private var streams: [SCStream] = []
    private var mp4: Mp4Writer?
    private var gif: GifWriter?
    private var gifLast: Int64 = -1
    private var gifBacklog = 0
    private var gifError: Error?
    private var gifFrames = 0
    private var latest: [CVPixelBuffer?]
    private var pausedFrame: CVPixelBuffer?
    private var started = false
    private var stopping = false
    private var timer: DispatchSourceTimer?
    private var converters: [Int: (AVAudioFormat, AVAudioConverter)] = [:]
    private var mic: MicCapture?
    private var micRunning = false
    private lazy var ci = CIContext(options: [.cacheIntermediates: false, .workingColorSpace: CGColorSpace(name: CGColorSpace.sRGB)!])
    private var ownPool: CVPixelBufferPool?

    // Ana iş parçacığında: (dosya yolu, hata, uyarı)
    var finished: ((URL, Error?, String?) -> Void)?
    private let stateLock = NSLock()
    private var failedMode: AudioMode = []
    private var warning: String?
    private var stopTime: Double?

    var elapsed: Double { clock.now }
    var paused: Bool { clock.paused }
    var failedAudio: AudioMode { stateLock.withLock { failedMode } }
    func level(of source: AudioMode) -> Float { mixer.level(of: source) }

    init(options: RecordingOptions) {
        self.options = options
        let t = options.target
        let raw = CGSize(width: t.size.width * t.scale, height: t.size.height * t.scale)
        let s = options.format == .gif
            ? Self.fit(raw, CGSize(width: GifWriter.maxSide, height: GifWriter.maxSide), even: false)
            : Self.fit(raw, Mp4Writer.maxSize, even: true)
        width = s.0
        height = s.1
        latest = Array(repeating: nil, count: t.parts.count)
        let ext = options.url.pathExtension
        temp = options.url.deletingPathExtension().appendingPathExtension("kayit").appendingPathExtension(ext)
        super.init()
        mixer.mode = options.format == .mp4 ? options.audio : []
        mixer.setGain(.system, Float(options.systemVolume) / 100)
        mixer.setGain(.microphone, Float(options.micVolume) / 100)
    }

    // En-boy oranını koruyarak sığdırır; even=true ise H.264 için çift sayıya yuvarlar.
    static func fit(_ size: CGSize, _ maxSize: CGSize, even: Bool) -> (Int, Int) {
        let s = min(1, min(maxSize.width / size.width, maxSize.height / size.height))
        var w = max(2, Int(size.width * s)), h = max(2, Int(size.height * s))
        if even {
            w &= ~1
            h &= ~1
        }
        return (w, h)
    }

    // ---------------------------------------------------------------- ses ayarları (kayıttan önce ya da sırasında)

    func setAudio(_ mode: AudioMode) {
        guard options.format == .mp4 else { return }
        mixer.mode = mode
        queue.async { [self] in if !streams.isEmpty && !stopping { updateMic() } }
    }

    func setVolume(_ source: AudioMode, _ percent: Int) {
        mixer.setGain(source, Float(percent) / 100)
    }

    var isPaused: Bool {
        get { clock.paused }
        set {
            clock.paused = newValue
            // Duraklamada ekran değiştiyse kayıt yeni görüntüyle sürsün.
            if !newValue { queue.async { [self] in resumeFrame() } }
        }
    }

    // ---------------------------------------------------------------- başlat / durdur

    @MainActor
    func start() {
        Task { @MainActor in
            do {
                try await begin()
            } catch {
                NSLog("Kıskaç: kayıt başlatılamadı: \(error)")
                queue.async { [self] in fail(error) }
            }
        }
    }

    @MainActor
    private func begin() async throws {
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
        // Kıskaç'ın kendi pencereleri (çerçeve, kontrol çubuğu, bildirimler) kayda girmez.
        let me = content.applications.first { $0.processID == ProcessInfo.processInfo.processIdentifier }
        try? FileManager.default.removeItem(at: temp)

        try queue.sync {
            if options.format == .gif {
                gif = try GifWriter(url: temp, width: width, height: height)
            } else {
                mp4 = try Mp4Writer(url: temp, width: width, height: height, fps: options.fps)
            }
        }

        let t = options.target
        let sx = CGFloat(width) / t.size.width, sy = CGFloat(height) / t.size.height
        var list: [SCStream] = []
        for (i, part) in t.parts.enumerated() {
            guard let display = content.displays.first(where: { $0.displayID == part.display }) else {
                throw Mp4Writer.failure("Kaydedilecek ekran bulunamadı.")
            }
            let filter = me.map { SCContentFilter(display: display, excludingApplications: [$0], exceptingWindows: []) }
                ?? SCContentFilter(display: display, excludingWindows: [])
            let cfg = SCStreamConfiguration()
            cfg.sourceRect = part.rect
            if t.parts.count == 1 {
                cfg.width = width
                cfg.height = height
            } else {
                cfg.width = max(2, Int((part.rect.width * sx).rounded()))
                cfg.height = max(2, Int((part.rect.height * sy).rounded()))
            }
            cfg.scalesToFit = true
            cfg.pixelFormat = kCVPixelFormatType_32BGRA
            cfg.colorSpaceName = CGColorSpace.sRGB
            cfg.minimumFrameInterval = CMTime(value: 1, timescale: CMTimeScale(options.fps))
            cfg.queueDepth = 6
            cfg.showsCursor = options.cursor
            let audio = options.format == .mp4 && i == 0
            if audio {
                cfg.capturesAudio = true
                cfg.sampleRate = Int(AudioMixer.rate)
                cfg.channelCount = 2
                cfg.excludesCurrentProcessAudio = true
            }
            let stream = SCStream(filter: filter, configuration: cfg, delegate: self)
            try stream.addStreamOutput(self, type: .screen, sampleHandlerQueue: queue)
            if audio { try stream.addStreamOutput(self, type: .audio, sampleHandlerQueue: queue) }
            list.append(stream)
        }
        queue.sync { streams = list } // kareler hangi ekrandan geldiğini buradan bulur
        for s in list { try await s.startCapture() }
        queue.async { [self] in
            started = true
            if stopping {
                finishOnQueue()
                return
            }
            startTimer()
            updateMic()
        }
    }

    // Kayıt sürerken çağrılır; dosya bitince finished çalışır.
    func stop() {
        let end = clock.now
        queue.async { [self] in
            guard !stopping else { return }
            stopping = true
            stopTime = end
            if started { finishOnQueue() } // akışlar henüz başlamadıysa begin() bitirir
        }
    }

    private func finishOnQueue() {
        timer?.cancel()
        timer = nil
        mic?.stop()
        let list = streams
        streams = []
        Task {
            for s in list { try? await s.stopCapture() }
            queue.async { [self] in finishFile() }
        }
    }

    private func finishFile() {
        let end = stopTime ?? clock.now
        if let w = mp4 {
            flushAudio(upTo: end, final: true)
            if w.frameCount == 0 {
                w.cancel()
                return complete(Mp4Writer.failure("Kayıt boş kaldı: ekrandan görüntü gelmedi."))
            }
            w.finish(endTime: end) { [self] err in queue.async { self.complete(err) } }
        } else if let g = gif {
            gifQueue.async { [self] in
                var err = gifError
                if err == nil && gifFrames == 0 { err = Mp4Writer.failure("Kayıt boş kaldı: ekrandan görüntü gelmedi.") }
                if err == nil { do { try g.finish() } catch { err = error } }
                queue.async { self.complete(err) }
            }
        } else {
            complete(nil)
        }
    }

    private func fail(_ error: Error) {
        stopping = true
        timer?.cancel()
        mic?.stop()
        mp4?.cancel()
        let list = streams
        streams = []
        Task { for s in list { try? await s.stopCapture() } }
        complete(error)
    }

    private var completed = false

    private func complete(_ error: Error?) {
        if completed { return }
        completed = true
        var err = error
        if err == nil {
            do {
                try? FileManager.default.removeItem(at: options.url)
                try FileManager.default.moveItem(at: temp, to: options.url)
            } catch {
                err = error
            }
        }
        if err != nil { try? FileManager.default.removeItem(at: temp) }
        let url = options.url, warn = stateLock.withLock { warning }
        DispatchQueue.main.async { [self] in finished?(url, err, warn) }
    }

    private func addWarning(_ s: String) {
        stateLock.withLock { warning = warning.map { $0 + "\n" + s } ?? s }
    }

    // ---------------------------------------------------------------- ScreenCaptureKit

    func stream(_ stream: SCStream, didStopWithError error: Error) {
        NSLog("Kıskaç: kayıt akışı durdu: \(error)")
        queue.async { [self] in
            guard !stopping, !completed else { return }
            let has = (mp4?.frameCount ?? 0) > 0 || gifLast >= 0
            if !has { return fail(error) }
            // Ekran çıkarıldı ya da sistem kaydı durdurdu: o ana kadarki kayıt saklanır.
            addWarning("Kayıt erken bitti: \(error.localizedDescription)")
            stopping = true
            stopTime = clock.now
            finishOnQueue()
        }
    }

    func stream(_ stream: SCStream, didOutputSampleBuffer sb: CMSampleBuffer, of type: SCStreamOutputType) {
        guard !stopping, sb.isValid else { return }
        switch type {
        case .screen:
            guard let i = streams.firstIndex(where: { $0 === stream }) else { return }
            handleVideo(sb, part: i)
        case .audio:
            handleAudio(sb, source: .system, host: CMSampleBufferGetPresentationTimeStamp(sb).seconds)
        default:
            break
        }
    }

    private func handleVideo(_ sb: CMSampleBuffer, part: Int) {
        guard let att = CMSampleBufferGetSampleAttachmentsArray(sb, createIfNecessary: false) as? [[SCStreamFrameInfo: Any]],
              let raw = att.first?[.status] as? Int, SCFrameStatus(rawValue: raw) == .complete,
              let pb = CMSampleBufferGetImageBuffer(sb) else { return }
        let host = CMSampleBufferGetPresentationTimeStamp(sb).seconds
        clock.start(at: host) // ilk kare kaydın başıdır
        latest[part] = pb
        guard let t = clock.toRecording(host) else {
            if clock.paused && latest.count == 1 { pausedFrame = pb } // en fazla bir kare tutulur
            return
        }
        if latest.count == 1 {
            write(pb, at: t)
        } else if frameIndex(t) > (mp4?.lastIndex ?? gifLast), let frame = composite() {
            write(frame, at: t) // birleştirme yalnızca yazılacak kare için yapılır
        }
    }

    private func frameIndex(_ t: Double) -> Int64 {
        Int64((t * Double(options.fps)).rounded(.down))
    }

    private func resumeFrame() {
        guard !stopping, clock.running else { return }
        let f = latest.count == 1 ? pausedFrame : composite()
        pausedFrame = nil
        if let f { write(f, at: clock.now) }
    }

    private func write(_ frame: CVPixelBuffer, at t: Double) {
        let index = frameIndex(t)
        if let w = mp4 {
            guard index > w.lastIndex else { return }
            let pb = fitted(frame, pool: w.pool) ?? frame
            w.appendVideo(pb, index: index)
            if let e = w.failed { fail(e) }
        } else if let g = gif {
            guard index > gifLast, gifBacklog < 3, let pb = fitted(frame, pool: gifPool()) else { return }
            // Pikseller hemen kopyalanır: ScreenCaptureKit'in tamponu bekletilmez.
            CVPixelBufferLockBaseAddress(pb, .readOnly)
            let bpr = CVPixelBufferGetBytesPerRow(pb)
            let data = CVPixelBufferGetBaseAddress(pb).map { Data(bytes: $0, count: bpr * CVPixelBufferGetHeight(pb)) }
            CVPixelBufferUnlockBaseAddress(pb, .readOnly)
            guard let data else { return }
            gifLast = index
            gifBacklog += 1
            let fps = Double(options.fps)
            gifQueue.async { [self] in
                if gifError == nil {
                    do {
                        try data.withUnsafeBytes {
                            try g.write(bgra: $0.baseAddress!, bytesPerRow: bpr, time: Double(index) / fps, duration: 1 / fps)
                        }
                        gifFrames += 1
                    } catch {
                        gifError = error
                    }
                }
                queue.async { [self] in gifBacklog -= 1 }
            }
        }
    }

    private func gifPool() -> CVPixelBufferPool? {
        if ownPool == nil {
            CVPixelBufferPoolCreate(nil, nil, [
                kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA,
                kCVPixelBufferWidthKey as String: width,
                kCVPixelBufferHeightKey as String: height,
                kCVPixelBufferIOSurfacePropertiesKey as String: [:] as [String: Any],
            ] as CFDictionary, &ownPool)
        }
        return ownPool
    }

    // Kare kayıt boyutunda değilse (nadiren) yeniden ölçeklenir.
    private func fitted(_ pb: CVPixelBuffer, pool: CVPixelBufferPool?) -> CVPixelBuffer? {
        if CVPixelBufferGetWidth(pb) == width && CVPixelBufferGetHeight(pb) == height { return pb }
        guard let pool, let out = newBuffer(pool) else { return nil }
        let img = CIImage(cvPixelBuffer: pb)
        let scaled = img.transformed(by: CGAffineTransform(scaleX: CGFloat(width) / img.extent.width,
                                                           y: CGFloat(height) / img.extent.height))
        ci.render(scaled, to: out, bounds: CGRect(x: 0, y: 0, width: width, height: height),
                  colorSpace: CGColorSpace(name: CGColorSpace.sRGB))
        return out
    }

    private func newBuffer(_ pool: CVPixelBufferPool) -> CVPixelBuffer? {
        var out: CVPixelBuffer?
        CVPixelBufferPoolCreatePixelBuffer(nil, pool, &out)
        return out
    }

    // Tüm ekranlar: her ekranın son karesi tuvaldeki yerine konur (henüz gelmeyen ekran siyah).
    private func composite() -> CVPixelBuffer? {
        guard let pool = mp4?.pool ?? gifPool(), let out = newBuffer(pool) else { return nil }
        let t = options.target
        let sx = CGFloat(width) / t.size.width, sy = CGFloat(height) / t.size.height
        var img = CIImage(color: .black).cropped(to: CGRect(x: 0, y: 0, width: width, height: height))
        for (i, part) in t.parts.enumerated() {
            guard let pb = latest[i] else { continue }
            let p = CIImage(cvPixelBuffer: pb)
            let x = (part.offset.x * sx).rounded()
            let y = CGFloat(height) - (part.offset.y * sy).rounded() - p.extent.height // CoreImage'da orijin sol alttadır
            img = p.transformed(by: CGAffineTransform(translationX: x, y: y)).composited(over: img)
        }
        ci.render(img, to: out, bounds: CGRect(x: 0, y: 0, width: width, height: height),
                  colorSpace: CGColorSpace(name: CGColorSpace.sRGB))
        return out
    }

    // ---------------------------------------------------------------- ses

    private func startTimer() {
        guard mp4 != nil else { return }
        let t = DispatchSource.makeTimerSource(queue: queue)
        t.schedule(deadline: .now() + 0.1, repeating: 0.1)
        // Geç gelen paketlere yer kalsın diye karışım 0,3 sn geriden yazılır.
        t.setEventHandler { [weak self] in
            guard let self, !self.stopping else { return }
            self.flushAudio(upTo: self.clock.now - 0.3, final: false)
        }
        t.resume()
        timer = t
    }

    private func flushAudio(upTo time: Double, final: Bool) {
        guard let w = mp4, clock.running else { return }
        let target = Int64(max(0, time) * AudioMixer.rate)
        guard target > mixer.framesTaken else { return }
        if !final && !w.audioReady { return } // karışımda bekler, sonraki turda yazılır
        if final {
            let deadline = Date().addingTimeInterval(1)
            while !w.audioReady && Date() < deadline { usleep(5000) }
        }
        if let samples = mixer.take(upTo: target) { w.appendAudio(samples) }
    }

    // Mikrofon yalnızca açıkken çalışır (menü çubuğundaki turuncu nokta da ona göre yanar).
    private func updateMic() {
        let want = mixer.mode.contains(.microphone)
        if want && !micRunning {
            if mic == nil {
                mic = MicCapture(queue: queue) { [weak self] sb, host in
                    guard let self, !self.stopping else { return }
                    self.handleAudio(sb, source: .microphone, host: host)
                }
            }
            micRunning = true
            mic?.start { [weak self] ok in
                guard let self else { return }
                self.stateLock.withLock {
                    if ok { self.failedMode.remove(.microphone) } else { self.failedMode.insert(.microphone) }
                }
                if !ok {
                    self.queue.async { self.micRunning = false }
                    self.addWarningOnce("Mikrofon açılamadı. Sistem Ayarları › Gizlilik ve Güvenlik › Mikrofon bölümünden Kıskaç'a izin ver.")
                }
            }
        } else if !want && micRunning {
            micRunning = false
            mic?.stop()
        }
    }

    private var warnedOnce: Set<String> = []

    private func addWarningOnce(_ s: String) {
        let first = stateLock.withLock { warnedOnce.insert(s).inserted }
        if first { addWarning(s) }
    }

    private func handleAudio(_ sb: CMSampleBuffer, source: AudioMode, host: Double) {
        guard let t = clock.toRecording(host), let pcm = pcmBuffer(sb, source: source) else { return }
        mixer.add(pcm, source: source, at: t)
    }

    // Paketi 48 kHz Float32 ayrık kanallara çevirir (gerekirse örnekleme hızı dönüştürülür).
    private func pcmBuffer(_ sb: CMSampleBuffer, source: AudioMode) -> AVAudioPCMBuffer? {
        guard let fd = CMSampleBufferGetFormatDescription(sb),
              let asbd = CMAudioFormatDescriptionGetStreamBasicDescription(fd),
              let fmt = AVAudioFormat(streamDescription: asbd) else { return nil }
        let frames = AVAudioFrameCount(CMSampleBufferGetNumSamples(sb))
        guard frames > 0, let buf = AVAudioPCMBuffer(pcmFormat: fmt, frameCapacity: frames) else { return nil }
        buf.frameLength = frames
        guard CMSampleBufferCopyPCMDataIntoAudioBufferList(sb, at: 0, frameCount: Int32(frames),
                                                          into: buf.mutableAudioBufferList) == noErr else { return nil }
        let channels = min(fmt.channelCount, 2)
        if fmt.commonFormat == .pcmFormatFloat32 && !fmt.isInterleaved && fmt.sampleRate == AudioMixer.rate { return buf }

        let key = source.rawValue
        guard let target = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: AudioMixer.rate,
                                         channels: channels, interleaved: false) else { return nil }
        if converters[key]?.0 != fmt {
            guard let c = AVAudioConverter(from: fmt, to: target) else { return nil }
            c.downmix = fmt.channelCount > 2
            converters[key] = (fmt, c)
        }
        guard let conv = converters[key]?.1,
              let out = AVAudioPCMBuffer(pcmFormat: target,
                                         frameCapacity: AVAudioFrameCount(Double(frames) * AudioMixer.rate / fmt.sampleRate) + 64)
        else { return nil }
        var given = false
        var err: NSError?
        conv.convert(to: out, error: &err) { _, status in
            if given {
                status.pointee = .noDataNow
                return nil
            }
            given = true
            status.pointee = .haveData
            return buf
        }
        return err == nil ? out : nil
    }
}
