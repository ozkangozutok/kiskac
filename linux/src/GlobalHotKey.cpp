#include "GlobalHotKey.h"

#include "Capture.h"
#include "Settings.h"

#include <QDBusArgument>
#include <QDBusConnection>
#include <QDBusInterface>
#include <QDBusMetaType>
#include <QDBusPendingCallWatcher>
#include <QDBusPendingReply>
#include <QDateTime>
#include <QGuiApplication>
#include <QRandomGenerator>

#include <xcb/xcb.h>
#include <xcb/xcb_keysyms.h>

// ---------------------------------------------------------------- portal türleri: a(sa{sv})

struct PortalShortcut {
    QString id;
    QVariantMap props;
};
Q_DECLARE_METATYPE(PortalShortcut)
using PortalShortcutList = QList<PortalShortcut>;
Q_DECLARE_METATYPE(PortalShortcutList)

QDBusArgument &operator<<(QDBusArgument &a, const PortalShortcut &s)
{
    a.beginStructure();
    a << s.id << s.props;
    a.endStructure();
    return a;
}

const QDBusArgument &operator>>(const QDBusArgument &a, PortalShortcut &s)
{
    a.beginStructure();
    a >> s.id >> s.props;
    a.endStructure();
    return a;
}

namespace {

const char *PortalService = "org.freedesktop.portal.Desktop";
const char *PortalPath = "/org/freedesktop/portal/desktop";
const char *ShortcutsIface = "org.freedesktop.portal.GlobalShortcuts";
const char *RequestIface = "org.freedesktop.portal.Request";

xcb_connection_t *x11Connection()
{
    auto *x11 = qGuiApp->nativeInterface<QNativeInterface::QX11Application>();
    return x11 ? x11->connection() : nullptr;
}

// Qt tuş kodu → X keysym
quint32 keysymFor(int key)
{
    if (key >= Qt::Key_F1 && key <= Qt::Key_F35) return 0xffbe + (key - Qt::Key_F1);
    switch (key) {
    case Qt::Key_Print: return 0xff61;
    case Qt::Key_Pause: return 0xff13;
    case Qt::Key_ScrollLock: return 0xff14;
    case Qt::Key_Space: return 0x0020;
    case Qt::Key_Return: return 0xff0d;
    case Qt::Key_Enter: return 0xff8d;
    case Qt::Key_Tab: return 0xff09;
    case Qt::Key_Backspace: return 0xff08;
    case Qt::Key_Delete: return 0xffff;
    case Qt::Key_Insert: return 0xff63;
    case Qt::Key_Home: return 0xff50;
    case Qt::Key_End: return 0xff57;
    case Qt::Key_PageUp: return 0xff55;
    case Qt::Key_PageDown: return 0xff56;
    case Qt::Key_Left: return 0xff51;
    case Qt::Key_Up: return 0xff52;
    case Qt::Key_Right: return 0xff53;
    case Qt::Key_Down: return 0xff54;
    }
    if (key >= 0x20 && key <= 0xff) return QChar(key).toLower().unicode(); // Latin-1 keysym = kod noktası
    if (key > 0xff && key < 0x110000) return 0x01000000 | QChar::toLower(char32_t(key)); // Unicode keysym (ş, ğ…)
    return 0;
}

// xdg "shortcuts" biçimi: CTRL+SHIFT+x, LOGO+Print
QString portalTrigger(const QKeySequence &k)
{
    if (k.isEmpty()) return QString();
    Qt::KeyboardModifiers m = k[0].keyboardModifiers();
    QString s;
    if (m & Qt::ControlModifier) s += "CTRL+";
    if (m & Qt::AltModifier) s += "ALT+";
    if (m & Qt::ShiftModifier) s += "SHIFT+";
    if (m & Qt::MetaModifier) s += "LOGO+";
    int key = k[0].key();
    if (key == Qt::Key_Print) return s + "Print";
    if (key >= Qt::Key_F1 && key <= Qt::Key_F35) return s + QString("F%1").arg(key - Qt::Key_F1 + 1);
    quint32 sym = keysymFor(key);
    if (sym >= 0x20 && sym <= 0x7e) return s + QChar(sym);
    return s + QKeySequence(key).toString(QKeySequence::PortableText);
}

QString newToken()
{
    return QString("kiskac%1").arg(QRandomGenerator::global()->generate());
}

} // namespace

GlobalHotKey::GlobalHotKey(QObject *parent) : QObject(parent)
{
    qDBusRegisterMetaType<PortalShortcut>();
    qDBusRegisterMetaType<PortalShortcutList>();
    if (Capturer::isWayland()) {
        mode = Backend::Portal;
    } else if (x11Connection()) {
        mode = Backend::X11;
        qApp->installNativeEventFilter(this);
    }
}

GlobalHotKey::~GlobalHotKey()
{
    if (mode == Backend::X11) {
        ungrabX11();
        qApp->removeNativeEventFilter(this);
    }
    closePortalSession();
}

void GlobalHotKey::setStatus(bool active, const QString &text)
{
    ok = active;
    statusText = text;
    emit statusChanged();
}

bool GlobalHotKey::setKey(const QKeySequence &k)
{
    key = k;
    if (paused) return true;
    switch (mode) {
    case Backend::X11:
        return grabX11();
    case Backend::Portal:
        startPortal();
        return true;
    case Backend::None:
        setStatus(false, "Bu masaüstünde genel kısayol desteklenmiyor. Masaüstü ayarlarından \"kiskac --capture\" "
                         "komutuna bir kısayol ata.");
        return false;
    }
    return false;
}

void GlobalHotKey::setPaused(bool p)
{
    if (paused == p) return;
    paused = p;
    if (mode == Backend::X11) {
        if (p)
            ungrabX11();
        else
            grabX11();
    }
    // Portalda kısayolu masaüstü yakalar; kayıt sırasında tetiklenirse yok sayılır (onActivated).
}

// ---------------------------------------------------------------- X11

bool GlobalHotKey::grabX11()
{
    ungrabX11();
    xcb_connection_t *c = x11Connection();
    if (!c || key.isEmpty()) return false;

    rootWindow = xcb_setup_roots_iterator(xcb_get_setup(c)).data->root;
    Qt::KeyboardModifiers m = key[0].keyboardModifiers();
    modMask = 0;
    if (m & Qt::ShiftModifier) modMask |= XCB_MOD_MASK_SHIFT;
    if (m & Qt::ControlModifier) modMask |= XCB_MOD_MASK_CONTROL;
    if (m & Qt::AltModifier) modMask |= XCB_MOD_MASK_1;
    if (m & Qt::MetaModifier) modMask |= XCB_MOD_MASK_4;

    xcb_key_symbols_t *syms = xcb_key_symbols_alloc(c);
    xcb_keycode_t *codes = xcb_key_symbols_get_keycode(syms, keysymFor(key[0].key()));
    keycodes.clear();
    for (xcb_keycode_t *p = codes; p && *p != XCB_NO_SYMBOL; p++) keycodes.append(*p);
    free(codes);
    xcb_key_symbols_free(syms);
    if (keycodes.isEmpty()) {
        setStatus(false, Settings::hotKeyText(key) + " bu klavyede bulunamadı.");
        return false;
    }

    // Caps Lock ve Num Lock açıkken de çalışsın: her birleşim ayrıca yakalanır.
    const quint16 extras[] = {0, XCB_MOD_MASK_LOCK, XCB_MOD_MASK_2, XCB_MOD_MASK_LOCK | XCB_MOD_MASK_2};
    QVector<xcb_void_cookie_t> cookies;
    for (quint8 kc : keycodes)
        for (quint16 e : extras)
            cookies.append(xcb_grab_key_checked(c, 1, rootWindow, modMask | e, kc, XCB_GRAB_MODE_ASYNC, XCB_GRAB_MODE_ASYNC));
    bool failed = false;
    for (auto ck : cookies) {
        if (xcb_generic_error_t *err = xcb_request_check(c, ck)) {
            failed = true;
            free(err);
        }
    }
    if (failed) {
        ungrabX11();
        setStatus(false, Settings::hotKeyText(key) + " başka bir uygulama (çoğunlukla masaüstünün kendi ekran görüntüsü aracı) "
                                                     "tarafından kullanılıyor. Başka bir kısayol seç ya da masaüstü "
                                                     "ayarlarından o kısayolu kaldır.");
        return false;
    }
    setStatus(true, "Kısayol etkin (X11).");
    return true;
}

void GlobalHotKey::ungrabX11()
{
    xcb_connection_t *c = x11Connection();
    if (!c || keycodes.isEmpty()) return;
    const quint16 extras[] = {0, XCB_MOD_MASK_LOCK, XCB_MOD_MASK_2, XCB_MOD_MASK_LOCK | XCB_MOD_MASK_2};
    for (quint8 kc : keycodes)
        for (quint16 e : extras) xcb_ungrab_key(c, kc, rootWindow, modMask | e);
    xcb_flush(c);
    keycodes.clear();
}

bool GlobalHotKey::nativeEventFilter(const QByteArray &eventType, void *message, qintptr *)
{
    if (eventType != "xcb_generic_event_t" || keycodes.isEmpty()) return false;
    auto *ev = static_cast<xcb_generic_event_t *>(message);
    if ((ev->response_type & ~0x80) != XCB_KEY_PRESS) return false;
    auto *kp = reinterpret_cast<xcb_key_press_event_t *>(ev);
    if (kp->event != rootWindow || !keycodes.contains(kp->detail)) return false;
    const quint16 relevant = XCB_MOD_MASK_SHIFT | XCB_MOD_MASK_CONTROL | XCB_MOD_MASK_1 | XCB_MOD_MASK_4;
    if ((kp->state & relevant) != modMask) return false;
    // Basılı tutulunca X otomatik tekrar gönderir; kısa aralıklı basışlar yok sayılır.
    qint64 now = QDateTime::currentMSecsSinceEpoch();
    if (now - lastPress > 400) emit activated();
    lastPress = now;
    return true;
}

// ---------------------------------------------------------------- Wayland: GlobalShortcuts portalı

QString GlobalHotKey::portalRequest(const QString &token, const char *slot)
{
    QDBusConnection bus = QDBusConnection::sessionBus();
    QString sender = bus.baseService().mid(1).replace('.', '_');
    QString path = QString("/org/freedesktop/portal/desktop/request/%1/%2").arg(sender, token);
    bus.connect(PortalService, path, RequestIface, "Response", this, slot);
    return path;
}

void GlobalHotKey::startPortal()
{
    closePortalSession();
    QDBusInterface portal(PortalService, PortalPath, ShortcutsIface, QDBusConnection::sessionBus());
    if (!portal.isValid()) {
        setStatus(false, "Masaüstün genel kısayol portalını desteklemiyor. Masaüstü ayarlarından \"kiskac --capture\" "
                         "komutuna bir kısayol ata.");
        return;
    }
    QString token = newToken();
    pendingRequest = portalRequest(token, SLOT(onSessionCreated(uint, QVariantMap)));
    QVariantMap opts{{"handle_token", token}, {"session_handle_token", newToken()}};
    auto *w = new QDBusPendingCallWatcher(portal.asyncCall("CreateSession", opts), this);
    connect(w, &QDBusPendingCallWatcher::finished, this, [this](QDBusPendingCallWatcher *w) {
        w->deleteLater();
        QDBusPendingReply<QDBusObjectPath> r = *w;
        if (r.isError())
            setStatus(false, "Genel kısayol portalı kullanılamıyor (" + r.error().message()
                                 + "). Masaüstü ayarlarından \"kiskac --capture\" komutuna bir kısayol ata.");
    });
}

void GlobalHotKey::onSessionCreated(uint response, const QVariantMap &results)
{
    QDBusConnection::sessionBus().disconnect(PortalService, pendingRequest, RequestIface, "Response", this,
                                             SLOT(onSessionCreated(uint, QVariantMap)));
    if (response != 0) {
        setStatus(false, "Genel kısayol oturumu açılamadı.");
        return;
    }
    sessionHandle = results.value("session_handle").toString();
    QDBusConnection::sessionBus().connect(PortalService, PortalPath, ShortcutsIface, "Activated", this,
                                          SLOT(onActivated(QDBusObjectPath, QString, qulonglong, QVariantMap)));
    bindPortal();
}

void GlobalHotKey::bindPortal()
{
    PortalShortcut s;
    s.id = "capture";
    s.props = {{"description", "Ekran görüntüsü al"}, {"preferred_trigger", portalTrigger(key)}};
    PortalShortcutList list{s};

    QString token = newToken();
    pendingRequest = portalRequest(token, SLOT(onBound(uint, QVariantMap)));
    QDBusInterface portal(PortalService, PortalPath, ShortcutsIface, QDBusConnection::sessionBus());
    QVariantMap opts{{"handle_token", token}};
    portal.asyncCall("BindShortcuts", QVariant::fromValue(QDBusObjectPath(sessionHandle)), QVariant::fromValue(list),
                     QString(), opts);
}

void GlobalHotKey::onBound(uint response, const QVariantMap &results)
{
    QDBusConnection::sessionBus().disconnect(PortalService, pendingRequest, RequestIface, "Response", this,
                                             SLOT(onBound(uint, QVariantMap)));
    if (response != 0) {
        setStatus(false, "Kısayol onaylanmadı. Ayarlar'dan yeniden deneyebilirsin.");
        return;
    }
    // Masaüstü farklı bir tuş atamış olabilir; gerçekte atanan gösterilir.
    QString trigger;
    const QDBusArgument arg = results.value("shortcuts").value<QDBusArgument>();
    PortalShortcutList bound;
    if (arg.currentType() != QDBusArgument::UnknownType) arg >> bound;
    for (const PortalShortcut &s : bound)
        if (s.id == "capture") trigger = s.props.value("trigger_description").toString();
    setStatus(true, trigger.isEmpty() ? "Kısayol masaüstüne kaydedildi (Wayland portalı)."
                                      : "Kısayol etkin: " + trigger + " (Wayland portalı).");
}

void GlobalHotKey::onActivated(const QDBusObjectPath &session, const QString &id, qulonglong, const QVariantMap &)
{
    if (session.path() == sessionHandle && id == "capture" && !paused) emit activated();
}

void GlobalHotKey::closePortalSession()
{
    if (sessionHandle.isEmpty()) return;
    QDBusConnection::sessionBus().disconnect(PortalService, PortalPath, ShortcutsIface, "Activated", this,
                                             SLOT(onActivated(QDBusObjectPath, QString, qulonglong, QVariantMap)));
    QDBusInterface session(PortalService, sessionHandle, "org.freedesktop.portal.Session", QDBusConnection::sessionBus());
    session.asyncCall("Close");
    sessionHandle.clear();
}
