#pragma once

#include <QObject>
#include <QPointer>

class Capturer;
class GlobalHotKey;
class OverlaySession;
class QAction;
class QLocalServer;
class QSystemTrayIcon;
class SettingsDialog;
class Updater;

// Uygulamanın gövdesi: tepsi simgesi, kısayol, tek örnek sunucusu ve çekim akışı.
// Kıskaç arka planda çalışmaya devam eder: Wayland'de pano, sahibi olan süreç kapanınca silinir.
class App : public QObject {
    Q_OBJECT
public:
    App(QLocalServer *server, bool justUpdated);
    void startCapture();

private:
    void startCaptureDelayed();
    void onShots();
    void showSettings();
    void notify(const QString &title, const QString &text);

    QSystemTrayIcon *tray;
    QAction *captureAction;
    QAction *updateAction;
    GlobalHotKey *hotKey;
    Capturer *capturer;
    Updater *updater;
    QPointer<OverlaySession> session;
    QPointer<SettingsDialog> settings;
    bool capturing = false;
};
