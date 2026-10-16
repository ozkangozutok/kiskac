#include "App.h"
#include "Logo.h"

#include <QApplication>
#include <QDir>
#include <QLocalServer>
#include <QLocalSocket>
#include <QThread>

#include <cstdio>
#include <unistd.h>

#ifndef KISKAC_VERSION
#define KISKAC_VERSION "0.0.0"
#endif

int runSelfTest(); // SelfTest.cpp

namespace {

QString serverName()
{
    return QString("kiskac-%1").arg(getuid());
}

// Çalışan örneğe "capture" gönderir; örnek yoksa false.
bool sendToRunning()
{
    QLocalSocket s;
    s.connectToServer(serverName());
    if (!s.waitForConnected(500)) return false;
    s.write("capture\n");
    s.waitForBytesWritten(500);
    s.waitForDisconnected(1000);
    return true;
}

} // namespace

int main(int argc, char **argv)
{
    // QApplication gerektirmeyen modlar
    for (int i = 1; i < argc; i++) {
        if (!strcmp(argv[i], "--version")) {
            printf("Kiskac %s\n", KISKAC_VERSION);
            return 0;
        }
        if (!strcmp(argv[i], "--help") || !strcmp(argv[i], "-h")) {
            printf("Kullanım: kiskac [--capture]\n"
                   "  --capture   Ekran görüntüsü al (Kıskaç çalışmıyorsa başlatır)\n"
                   "  --version   Sürümü yaz\n");
            return 0;
        }
    }

    QApplication::setApplicationName("Kiskac");
    QApplication::setApplicationDisplayName("Kıskaç");
    QApplication::setApplicationVersion(KISKAC_VERSION);
    QApplication::setDesktopFileName("kiskac");
    QApplication::setQuitOnLastWindowClosed(false);
    QApplication app(argc, argv);
    app.setWindowIcon(Logo::icon());

    QStringList args = app.arguments();
    if (args.size() == 3 && args[1] == "--export-icon") return Logo::image(256).save(args[2]) ? 0 : 1;
    if (args.contains("--selftest")) return runSelfTest();

    bool capture = args.contains("--capture");
    bool justUpdated = args.contains("--after-update");

    // Tek örnek: zaten çalışıyorsa yeniden açmak = ekran görüntüsü al
    if (sendToRunning()) return 0;
    auto *server = new QLocalServer(&app);
    server->setSocketOptions(QLocalServer::UserAccessOption);
    if (!server->listen(serverName())) {
        QLocalServer::removeServer(serverName()); // önceki çöküşten kalan soket
        if (!server->listen(serverName())) {
            fprintf(stderr, "Kıskaç: tek örnek soketi açılamadı: %s\n", qPrintable(server->errorString()));
            return 1;
        }
    }

    App kiskac(server, justUpdated);
    if (capture) QMetaObject::invokeMethod(&kiskac, [&kiskac] { kiskac.startCapture(); }, Qt::QueuedConnection);
    return app.exec();
}
