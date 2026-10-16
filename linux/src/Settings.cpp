#include "Settings.h"

#include <QCoreApplication>
#include <QDir>
#include <QFile>
#include <QSettings>
#include <QStandardPaths>
#include <QTextStream>

namespace {

QSettings &store()
{
    static QSettings s(QSettings::IniFormat, QSettings::UserScope, "Kiskac", "Kiskac");
    return s;
}

QVariant get(const char *key, const QVariant &def) { return store().value(key, def); }
void set(const char *key, const QVariant &v) { store().setValue(key, v); }

int clampInt(int v, int lo, int hi) { return std::max(lo, std::min(hi, v)); }

QString autostartFile()
{
    return QStandardPaths::writableLocation(QStandardPaths::ConfigLocation) + "/autostart/kiskac.desktop";
}

// AppImage içinden çalışıyorsa AppImage dosyası, değilse ikili dosyanın kendisi başlatılır.
QString executable()
{
    QByteArray appImage = qgetenv("APPIMAGE");
    return appImage.isEmpty() ? QCoreApplication::applicationFilePath() : QString::fromLocal8Bit(appImage);
}

} // namespace

namespace Settings {

QString saveDir()
{
    QString v = get("SaveDir", QString()).toString();
    if (v.isEmpty() || !QDir(v).exists()) {
        QString pics = QStandardPaths::writableLocation(QStandardPaths::PicturesLocation);
        if (pics.isEmpty()) pics = QDir::homePath() + "/Pictures";
        v = pics + "/Kıskaç";
        QDir().mkpath(v);
    }
    return v;
}
void setSaveDir(const QString &dir) { set("SaveDir", dir); }

bool initialized() { return get("Initialized", false).toBool(); }
void setInitialized() { set("Initialized", true); }

QKeySequence defaultHotKey() { return QKeySequence(Qt::Key_Print); }
QKeySequence hotKey()
{
    QKeySequence k = QKeySequence::fromString(get("HotKey", QString()).toString(), QKeySequence::PortableText);
    return k.isEmpty() ? defaultHotKey() : QKeySequence(k[0]);
}
void setHotKey(const QKeySequence &k) { set("HotKey", k.toString(QKeySequence::PortableText)); }

int dimPercent() { return clampInt(get("DimPercent", 45).toInt(), 15, 75); }
void setDimPercent(int v) { set("DimPercent", v); }
bool showHint() { return get("ShowHint", true).toBool(); }
void setShowHint(bool v) { set("ShowHint", v); }
bool rememberStyle() { return get("RememberStyle", true).toBool(); }
void setRememberStyle(bool v) { set("RememberStyle", v); }
int lastColor() { return get("LastColor", 0).toInt(); }
void setLastColor(int v) { set("LastColor", v); }
int lastWidth() { return get("LastWidth", 3).toInt(); }
void setLastWidth(int v) { set("LastWidth", v); }

int format() { return clampInt(get("Format", 0).toInt(), 0, 1); }
void setFormat(int v) { set("Format", v); }
QString formatExtension() { return format() == 1 ? ".jpg" : ".png"; }
int jpegQuality() { return clampInt(get("JpegQuality", 92).toInt(), 50, 100); }
void setJpegQuality(int v) { set("JpegQuality", v); }
bool quickSave() { return get("QuickSave", false).toBool(); }
void setQuickSave(bool v) { set("QuickSave", v); }
bool copyAfterSave() { return get("CopyAfterSave", false).toBool(); }
void setCopyAfterSave(bool v) { set("CopyAfterSave", v); }

bool autoUpdate() { return get("AutoUpdate", true).toBool(); }
void setAutoUpdate(bool v) { set("AutoUpdate", v); }
QDateTime lastUpdateCheck() { return get("LastUpdateCheck", QDateTime()).toDateTime(); }
void setLastUpdateCheck(const QDateTime &t) { set("LastUpdateCheck", t); }
QString skippedVersion() { return get("SkippedVersion", QString()).toString(); }
void setSkippedVersion(const QString &v) { set("SkippedVersion", v); }

bool autostart() { return QFile::exists(autostartFile()); }

void setAutostart(bool on)
{
    QString path = autostartFile();
    if (!on) {
        QFile::remove(path);
        return;
    }
    QDir().mkpath(QFileInfo(path).absolutePath());
    QFile f(path);
    if (!f.open(QIODevice::WriteOnly | QIODevice::Truncate)) return;
    QTextStream o(&f);
    o << "[Desktop Entry]\n"
      << "Type=Application\n"
      << "Name=Kıskaç\n"
      << "Comment=Ekran görüntüsü aracı\n"
      << "Exec=\"" << executable() << "\"\n"
      << "Icon=kiskac\n"
      << "Terminal=false\n"
      << "X-GNOME-Autostart-enabled=true\n";
}

// ---------------------------------------------------------------- kısayol

QString hotKeyText(const QKeySequence &k)
{
    if (k.isEmpty()) return QString();
    int key = k[0].key();
    Qt::KeyboardModifiers m = k[0].keyboardModifiers();
    QString s;
    if (m & Qt::ControlModifier) s += "Ctrl+";
    if (m & Qt::AltModifier) s += "Alt+";
    if (m & Qt::ShiftModifier) s += "Shift+";
    if (m & Qt::MetaModifier) s += "Super+";
    switch (key) {
    case Qt::Key_Print: return s + "PrtSc";
    case Qt::Key_Space: return s + "Boşluk";
    case Qt::Key_ScrollLock: return s + "ScrLk";
    default: return s + QKeySequence(key).toString(QKeySequence::NativeText);
    }
}

QString hotKeyProblem(const QKeySequence &k)
{
    if (k.isEmpty()) return "Bir tuşa bas.";
    int key = k[0].key();
    Qt::KeyboardModifiers m = k[0].keyboardModifiers() & (Qt::ControlModifier | Qt::AltModifier | Qt::ShiftModifier | Qt::MetaModifier);
    QString name = hotKeyText(k);

    if (key == Qt::Key_Escape) return "Esc kısayol olamaz.";
    // PrtSc, F tuşları, Pause ve Scroll Lock tek başına olabilir; yazmayı bozmazlar.
    bool standalone = key == Qt::Key_Print || (key >= Qt::Key_F1 && key <= Qt::Key_F35) || key == Qt::Key_Pause
        || key == Qt::Key_ScrollLock;
    if (standalone) return QString();
    if (!(m & (Qt::ControlModifier | Qt::MetaModifier)))
        return "Ctrl veya Super ile birlikte bir tuşa bas. PrtSc ve F tuşları tek başına da olur.";

    static const QList<int> ctrlKeys = {Qt::Key_A, Qt::Key_C, Qt::Key_V, Qt::Key_X, Qt::Key_Z, Qt::Key_Y, Qt::Key_S,
                                        Qt::Key_F, Qt::Key_N, Qt::Key_O, Qt::Key_P, Qt::Key_W, Qt::Key_T, Qt::Key_Q,
                                        Qt::Key_R, Qt::Key_Tab, Qt::Key_Space};
    static const QList<int> ctrlShiftKeys = {Qt::Key_Z, Qt::Key_S, Qt::Key_N, Qt::Key_T, Qt::Key_P, Qt::Key_W,
                                             Qt::Key_Tab, Qt::Key_C, Qt::Key_V};
    if ((m == Qt::ControlModifier && ctrlKeys.contains(key))
        || (m == (Qt::ControlModifier | Qt::ShiftModifier) && ctrlShiftKeys.contains(key)))
        return name + " uygulamalarda standart bir kısayol, başka bir tane seç.";
    // Ctrl+Alt+T terminal, Ctrl+Alt+Del oturum, Ctrl+Alt+oklar çalışma alanı değiştirir.
    if (m == (Qt::ControlModifier | Qt::AltModifier)
        && (key == Qt::Key_T || key == Qt::Key_Delete || key == Qt::Key_Left || key == Qt::Key_Right
            || key == Qt::Key_Up || key == Qt::Key_Down || key == Qt::Key_L))
        return name + " masaüstü ortamına ayrılmıştır, başka bir tane seç.";
    return QString();
}

} // namespace Settings
