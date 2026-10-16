#include "Capture.h"

#include <QDBusConnection>
#include <QDBusInterface>
#include <QDBusPendingCallWatcher>
#include <QDBusPendingReply>
#include <QFile>
#include <QGuiApplication>
#include <QPixmap>
#include <QRandomGenerator>
#include <QUrl>

bool Capturer::isWayland()
{
    return QGuiApplication::platformName().startsWith("wayland");
}

void Capturer::capture()
{
    if (isWayland())
        capturePortal();
    else
        captureX11();
}

void Capturer::captureX11()
{
    QList<ScreenShot> shots;
    for (QScreen *s : QGuiApplication::screens()) {
        // grabWindow(0): o ekranın kök pencere alanı, cihaz pikselleriyle
        QPixmap pm = s->grabWindow(0);
        if (pm.isNull()) continue;
        QImage img = pm.toImage().convertToFormat(QImage::Format_RGB32);
        img.setDevicePixelRatio(double(img.width()) / s->geometry().width());
        shots.append({s, img});
    }
    if (shots.isEmpty())
        emit failed("Ekran yakalanamadı.");
    else
        emit finished(shots);
}

// ---------------------------------------------------------------- Wayland portalı

void Capturer::capturePortal()
{
    QDBusConnection bus = QDBusConnection::sessionBus();
    QString token = QString("kiskac%1").arg(QRandomGenerator::global()->generate());
    // İstek nesnesinin yolu önceden bilinir; yanıt kaçmasın diye çağrıdan önce dinlenir.
    QString sender = bus.baseService().mid(1).replace('.', '_');
    requestPath = QString("/org/freedesktop/portal/desktop/request/%1/%2").arg(sender, token);
    bus.connect("org.freedesktop.portal.Desktop", requestPath, "org.freedesktop.portal.Request", "Response", this,
                SLOT(onPortalResponse(uint, QVariantMap)));

    QDBusInterface portal("org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop",
                          "org.freedesktop.portal.Screenshot", bus);
    QVariantMap options{{"handle_token", token}, {"interactive", false}, {"modal", false}};
    auto *w = new QDBusPendingCallWatcher(portal.asyncCall("Screenshot", QString(), options), this);
    connect(w, &QDBusPendingCallWatcher::finished, this, [this](QDBusPendingCallWatcher *w) {
        w->deleteLater();
        QDBusPendingReply<QDBusObjectPath> r = *w;
        if (r.isError()) {
            QDBusConnection::sessionBus().disconnect("org.freedesktop.portal.Desktop", requestPath,
                                                     "org.freedesktop.portal.Request", "Response", this,
                                                     SLOT(onPortalResponse(uint, QVariantMap)));
            emit failed("Ekran görüntüsü portalı kullanılamıyor: " + r.error().message());
        }
    });
}

void Capturer::onPortalResponse(uint response, const QVariantMap &results)
{
    QDBusConnection::sessionBus().disconnect("org.freedesktop.portal.Desktop", requestPath,
                                             "org.freedesktop.portal.Request", "Response", this,
                                             SLOT(onPortalResponse(uint, QVariantMap)));
    if (response != 0) {
        emit failed(response == 1 ? "Ekran görüntüsü iptal edildi." : "Ekran görüntüsü portalı hata verdi.");
        return;
    }
    QString path = QUrl(results.value("uri").toString()).toLocalFile();
    QImage full(path);
    QFile::remove(path); // portal dosyayı Resimler klasörüne yazar; yalnızca bize lazım
    if (full.isNull()) {
        emit failed("Portalın verdiği görüntü okunamadı.");
        return;
    }
    full = full.convertToFormat(QImage::Format_RGB32);

    // Görüntü tüm masaüstünü kapsar; ekranlara mantıksal geometri × ölçekle bölünür.
    QRect virt;
    for (QScreen *s : QGuiApplication::screens()) virt = virt.united(s->geometry());
    double scale = double(full.width()) / std::max(1, virt.width());
    QList<ScreenShot> shots;
    for (QScreen *s : QGuiApplication::screens()) {
        QRect g = s->geometry().translated(-virt.topLeft());
        QRect src(qRound(g.x() * scale), qRound(g.y() * scale), qRound(g.width() * scale), qRound(g.height() * scale));
        QImage img = full.copy(src.intersected(full.rect()));
        if (img.isNull()) continue;
        img.setDevicePixelRatio(double(img.width()) / g.width());
        shots.append({s, img});
    }
    if (shots.isEmpty())
        emit failed("Ekran yakalanamadı.");
    else
        emit finished(shots);
}
