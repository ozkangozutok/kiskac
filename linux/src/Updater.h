#pragma once

#include <QObject>
#include <QString>
#include <QTimer>
#include <QUrl>
#include <QVersionNumber>

#include <functional>

class QNetworkAccessManager;

// GitHub Releases üzerinden güncelleme. AppImage olarak çalışıyorsa yeni AppImage indirilir,
// SHA256 özeti ve sürümü doğrulanır, dosya yerinde değiştirilip Kıskaç yeniden açılır.
// Başka bir kurulumda (dağıtım paketi, elle derleme) indirme sayfası açılır.
class Updater : public QObject {
    Q_OBJECT
public:
    struct Release {
        QVersionNumber version;
        QString tag, body;
        QUrl htmlUrl, assetUrl;
        QString digest; // "sha256:…" ya da boş
    };

    explicit Updater(QObject *parent = nullptr);

    static QVersionNumber current();
    static QString assetName(); // Kiskac-x86_64.AppImage
    static Release parseRelease(const QByteArray &json);
    // İndirilen dosyayı doğrular; sorun varsa açıklama döner.
    static QString verify(const QString &path, const Release &r);

    void startAutomaticChecks();
    void check(bool userInitiated);

    std::function<bool()> canInterrupt = [] { return true; };

signals:
    void busyChanged(bool busy);

private:
    void offer(const Release &r);
    void install(const Release &r);
    void swapAndRestart(const QString &fresh);
    void message(const QString &text, bool warning = false);

    QNetworkAccessManager *net;
    QTimer timer;
    bool busy = false;
};
