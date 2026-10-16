#pragma once

#include <QImage>
#include <QList>
#include <QObject>
#include <QPointer>
#include <QScreen>
#include <QVariantMap>

struct ScreenShot {
    QPointer<QScreen> screen;
    QImage image; // cihaz pikselleri; devicePixelRatio ekranın ölçeğine ayarlı
};

// X11'de her ekran doğrudan (XGetImage) yakalanır. Wayland'de uygulamalar ekranı okuyamaz;
// xdg-desktop-portal Screenshot arayüzüyle tüm masaüstü alınır ve ekranlara bölünür.
class Capturer : public QObject {
    Q_OBJECT
public:
    explicit Capturer(QObject *parent = nullptr) : QObject(parent) {}
    void capture();
    static bool isWayland();

signals:
    void finished(const QList<ScreenShot> &shots);
    void failed(const QString &message);

private slots:
    void onPortalResponse(uint response, const QVariantMap &results);

private:
    void captureX11();
    void capturePortal();
    QString requestPath;
};
