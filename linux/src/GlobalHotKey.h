#pragma once

#include <QAbstractNativeEventFilter>
#include <QDBusObjectPath>
#include <QKeySequence>
#include <QObject>
#include <QVariantMap>
#include <QVector>

// Sistem genelinde kısayol.
//  - X11: kök pencerede XGrabKey (izin istemez).
//  - Wayland: xdg-desktop-portal GlobalShortcuts (KDE, GNOME 48+, Hyprland…); masaüstü bir kez onay ister.
//  - İkisi de yoksa: masaüstü ayarlarından "kiskac --capture" komutuna kısayol atanır.
class GlobalHotKey : public QObject, public QAbstractNativeEventFilter {
    Q_OBJECT
public:
    enum class Backend { None, X11, Portal };

    explicit GlobalHotKey(QObject *parent = nullptr);
    ~GlobalHotKey() override;

    // Kısayolu ayarlar. X11'de sonuç hemen bellidir; portalda statusChanged ile gelir.
    bool setKey(const QKeySequence &key);
    void setPaused(bool paused); // ayarlarda yeni kısayol kaydedilirken

    Backend backend() const { return mode; }
    bool active() const { return ok; }
    QString status() const { return statusText; }

    bool nativeEventFilter(const QByteArray &eventType, void *message, qintptr *result) override;

signals:
    void activated();
    void statusChanged();

private slots:
    void onSessionCreated(uint response, const QVariantMap &results);
    void onBound(uint response, const QVariantMap &results);
    void onActivated(const QDBusObjectPath &session, const QString &id, qulonglong timestamp, const QVariantMap &options);

private:
    bool grabX11();
    void ungrabX11();
    void startPortal();
    void bindPortal();
    void closePortalSession();
    QString portalRequest(const QString &token, const char *slot);
    void setStatus(bool ok, const QString &text);

    Backend mode = Backend::None;
    QKeySequence key;
    bool paused = false;
    bool ok = false;
    QString statusText;

    // X11
    QVector<quint8> keycodes;
    quint16 modMask = 0;
    quint32 rootWindow = 0;
    qint64 lastPress = 0;

    // Portal
    QString sessionHandle;
    QString pendingRequest;
};
