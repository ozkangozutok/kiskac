#pragma once

#include <QDateTime>
#include <QKeySequence>
#include <QString>

// Tüm ayarlar ~/.config/Kiskac/Kiskac.conf dosyasında tutulur.
namespace Settings {

QString saveDir();
void setSaveDir(const QString &dir);

bool initialized();
void setInitialized();

QKeySequence hotKey();
void setHotKey(const QKeySequence &k);
QKeySequence defaultHotKey();

int dimPercent(); // 15–75
void setDimPercent(int v);
bool showHint();
void setShowHint(bool v);
bool rememberStyle();
void setRememberStyle(bool v);
int lastColor();
void setLastColor(int v);
int lastWidth();
void setLastWidth(int v);

int format(); // 0: PNG, 1: JPEG
void setFormat(int v);
QString formatExtension();
int jpegQuality(); // 50–100
void setJpegQuality(int v);
bool quickSave();
void setQuickSave(bool v);
bool copyAfterSave();
void setCopyAfterSave(bool v);

bool autoUpdate();
void setAutoUpdate(bool v);
QDateTime lastUpdateCheck();
void setLastUpdateCheck(const QDateTime &t);
QString skippedVersion();
void setSkippedVersion(const QString &v);

// ~/.config/autostart/kiskac.desktop
bool autostart();
void setAutostart(bool on);

// Kısayol kullanılamıyorsa nedeni, kullanılabiliyorsa boş dize.
QString hotKeyProblem(const QKeySequence &k);
// Görünen ad: "Ctrl+Shift+X", "PrtSc"
QString hotKeyText(const QKeySequence &k);

} // namespace Settings
