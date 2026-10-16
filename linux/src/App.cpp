#include "App.h"

#include "Capture.h"
#include "GlobalHotKey.h"
#include "Logo.h"
#include "Overlay.h"
#include "Settings.h"
#include "SettingsDialog.h"
#include "Updater.h"

#include <QApplication>
#include <QDesktopServices>
#include <QLocalServer>
#include <QLocalSocket>
#include <QMenu>
#include <QMessageBox>
#include <QSystemTrayIcon>
#include <QTimer>
#include <QUrl>

App::App(QLocalServer *server, bool justUpdated)
    : hotKey(new GlobalHotKey(this)), capturer(new Capturer(this)), updater(new Updater(this))
{
    auto *menu = new QMenu;
    captureAction = menu->addAction("Ekran görüntüsü al", this, [this] { startCaptureDelayed(); });
    QFont bold = captureAction->font();
    bold.setBold(true);
    captureAction->setFont(bold);
    menu->addAction("Kayıt klasörünü aç", this, [] { QDesktopServices::openUrl(QUrl::fromLocalFile(Settings::saveDir())); });
    menu->addSeparator();
    menu->addAction("Ayarlar…", this, [this] { showSettings(); });
    updateAction = menu->addAction("Güncellemeleri denetle…", this, [this] { updater->check(true); });
    menu->addSeparator();
    menu->addAction("Çıkış", qApp, &QApplication::quit);
    connect(menu, &QMenu::aboutToShow, this, [this] {
        captureAction->setText("Ekran görüntüsü al\t" + Settings::hotKeyText(Settings::hotKey()));
    });

    tray = new QSystemTrayIcon(Logo::icon(), this);
    tray->setToolTip("Kıskaç — ekran görüntüsü");
    tray->setContextMenu(menu);
    connect(tray, &QSystemTrayIcon::activated, this, [this](QSystemTrayIcon::ActivationReason r) {
        if (r == QSystemTrayIcon::Trigger) startCaptureDelayed();
    });
    tray->show();

    // İkinci örnek (ya da "kiskac --capture") bağlanınca: ekran görüntüsü al
    connect(server, &QLocalServer::newConnection, this, [this, server] {
        while (QLocalSocket *s = server->nextPendingConnection()) {
            connect(s, &QLocalSocket::readyRead, this, [this, s] {
                QByteArray cmd = s->readAll().trimmed();
                if (cmd == "capture") startCapture();
                s->disconnectFromServer();
            });
            connect(s, &QLocalSocket::disconnected, s, &QObject::deleteLater);
        }
    });

    connect(hotKey, &GlobalHotKey::activated, this, &App::startCapture);
    connect(capturer, &Capturer::finished, this, [this](const QList<ScreenShot> &shots) {
        capturing = false;
        auto *s = new OverlaySession(shots, this);
        session = s;
        connect(s, &OverlaySession::closed, s, &QObject::deleteLater);
        s->show();
    });
    connect(capturer, &Capturer::failed, this, [this](const QString &msg) {
        capturing = false;
        notify("Kıskaç", msg);
    });

    updater->canInterrupt = [this] { return !session && !capturing; };
    connect(updater, &Updater::busyChanged, this, [this](bool busy) {
        updateAction->setEnabled(!busy);
        updateAction->setText(busy ? "Güncelleme denetleniyor…" : "Güncellemeleri denetle…");
    });
    updater->startAutomaticChecks();

    hotKey->setKey(Settings::hotKey());
    connect(hotKey, &GlobalHotKey::statusChanged, this, [this] {
        tray->setToolTip(hotKey->active() ? "Kıskaç — " + Settings::hotKeyText(Settings::hotKey()) + " ile ekran görüntüsü"
                                          : "Kıskaç — ekran görüntüsü");
    });

    if (!Settings::initialized()) {
        Settings::setInitialized();
        Settings::setAutostart(true);
        notify("Kıskaç çalışıyor", Settings::hotKeyText(Settings::hotKey())
                                       + " tuşuna bas ve alanı seç. Ctrl+C kopyalar, Ctrl+S kaydeder. Kısayolu ve diğer "
                                         "ayarları tepsi menüsünden değiştirebilirsin.");
        // Kısayol kurulamadıysa ayarlar açılır; orada neden ve çözüm yazar.
        QTimer::singleShot(1500, this, [this] {
            if (!hotKey->active()) showSettings();
        });
    } else if (justUpdated) {
        notify("Kıskaç güncellendi", "Kıskaç " + Updater::current().toString() + " sürümüne güncellendi.");
    }
    if (Settings::autostart()) Settings::setAutostart(true); // AppImage taşındıysa yolu güncelle
}

void App::notify(const QString &title, const QString &text)
{
    if (QSystemTrayIcon::supportsMessages())
        tray->showMessage(title, text, Logo::icon(), 6000);
}

// Menü kapanma animasyonu görüntüye girmesin.
void App::startCaptureDelayed()
{
    QTimer::singleShot(250, this, &App::startCapture);
}

void App::startCapture()
{
    if (session) {
        session->bringToFront();
        return;
    }
    // Açık bir uyarı penceresi varken seçim ekranı onun üstünde açılıp girdiyi kilitlerdi.
    if (QWidget *modal = QApplication::activeModalWidget()) {
        modal->raise();
        modal->activateWindow();
        return;
    }
    if (capturing) return;
    capturing = true;
    capturer->capture();
}

void App::showSettings()
{
    if (!settings) {
        settings = new SettingsDialog(hotKey, [this] { updater->check(true); });
        settings->setWindowIcon(Logo::icon());
    }
    settings->show();
    settings->raise();
    settings->activateWindow();
}
