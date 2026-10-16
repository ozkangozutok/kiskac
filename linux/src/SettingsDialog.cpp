#include "SettingsDialog.h"

#include "GlobalHotKey.h"
#include "Settings.h"
#include "Updater.h"

#include <QCheckBox>
#include <QComboBox>
#include <QCoreApplication>
#include <QDialogButtonBox>
#include <QDir>
#include <QFileDialog>
#include <QFormLayout>
#include <QGroupBox>
#include <QHBoxLayout>
#include <QKeyEvent>
#include <QLabel>
#include <QLineEdit>
#include <QSlider>
#include <QVBoxLayout>

// ---------------------------------------------------------------- kısayol kaydedici

KeyRecorder::KeyRecorder(QWidget *parent) : QPushButton(parent)
{
    setMinimumWidth(170);
    QFont f = font();
    f.setBold(true);
    setFont(f);
    connect(this, &QPushButton::clicked, this, [this] {
        if (recording)
            stop();
        else
            start();
    });
}

void KeyRecorder::setKey(const QKeySequence &k)
{
    key = k;
    if (!recording) setText(Settings::hotKeyText(k));
}

void KeyRecorder::start()
{
    recording = true;
    setText("Kısayola bas…");
    grabKeyboard();
    emit recordingChanged(true);
}

void KeyRecorder::stop()
{
    if (!recording) return;
    recording = false;
    releaseKeyboard();
    setText(Settings::hotKeyText(key));
    emit recordingChanged(false);
}

void KeyRecorder::keyPressEvent(QKeyEvent *e)
{
    if (!recording) {
        QPushButton::keyPressEvent(e);
        return;
    }
    int k = e->key();
    if (k == Qt::Key_Control || k == Qt::Key_Shift || k == Qt::Key_Alt || k == Qt::Key_Meta || k == Qt::Key_Super_L
        || k == Qt::Key_Super_R || k == Qt::Key_AltGr || k == 0 || k == Qt::Key_unknown)
        return;
    if (k == Qt::Key_Escape && e->modifiers() == Qt::NoModifier) {
        stop();
        return;
    }
    // Shift'li harflerde Qt büyük harf kodu verir; kısayol için harfin kendisi kullanılır.
    Qt::KeyboardModifiers m = e->modifiers() & (Qt::ControlModifier | Qt::AltModifier | Qt::ShiftModifier | Qt::MetaModifier);
    emit recorded(QKeySequence(QKeyCombination(m, Qt::Key(k))));
}

void KeyRecorder::focusOutEvent(QFocusEvent *e)
{
    stop(); // başka pencereye geçilirse kayıt biter
    QPushButton::focusOutEvent(e);
}

// ---------------------------------------------------------------- pencere

namespace {

QCheckBox *check(const QString &text, bool value, std::function<void(bool)> set)
{
    auto *c = new QCheckBox(text);
    c->setChecked(value);
    QObject::connect(c, &QCheckBox::toggled, c, [set](bool v) { set(v); });
    return c;
}

QWidget *slider(int lo, int hi, int value, std::function<void(int)> set)
{
    auto *w = new QWidget;
    auto *h = new QHBoxLayout(w);
    h->setContentsMargins(0, 0, 0, 0);
    auto *s = new QSlider(Qt::Horizontal);
    s->setRange(lo, hi);
    s->setValue(value);
    s->setMinimumWidth(180);
    auto *label = new QLabel(QString("%%1").arg(value));
    label->setMinimumWidth(40);
    label->setAlignment(Qt::AlignRight | Qt::AlignVCenter);
    QObject::connect(s, &QSlider::valueChanged, label, [label, set](int v) {
        label->setText(QString("%%1").arg(v));
        set(v);
    });
    h->addWidget(s);
    h->addWidget(label);
    return w;
}

} // namespace

SettingsDialog::SettingsDialog(GlobalHotKey *hk, std::function<void()> checkForUpdates, QWidget *parent)
    : QDialog(parent), hotKey(hk)
{
    setWindowTitle("Kıskaç Ayarları");
    setAttribute(Qt::WA_DeleteOnClose);
    auto *root = new QVBoxLayout(this);

    // ---------------------------------------------------------------- kısayol
    auto *g = new QGroupBox("Kısayol");
    auto *form = new QFormLayout(g);
    recorder = new KeyRecorder;
    recorder->setKey(Settings::hotKey());
    resetButton = new QPushButton("Varsayılan");
    resetButton->setFlat(true);
    connect(resetButton, &QPushButton::clicked, this, [this] { apply(Settings::defaultHotKey()); });
    auto *row = new QHBoxLayout;
    row->addWidget(recorder);
    row->addWidget(resetButton);
    row->addStretch();
    form->addRow("Ekran görüntüsü al", row);
    message = new QLabel;
    message->setWordWrap(true);
    message->setStyleSheet("color: #c0392b");
    message->hide();
    form->addRow(message);
    status = new QLabel;
    status->setWordWrap(true);
    status->setTextInteractionFlags(Qt::TextSelectableByMouse);
    form->addRow(status);
    root->addWidget(g);

    connect(recorder, &KeyRecorder::recordingChanged, this, [this](bool rec) {
        hotKey->setPaused(rec);
        resetButton->setVisible(!rec && Settings::hotKey() != Settings::defaultHotKey());
        if (rec) {
            message->setStyleSheet("color: #888");
            message->setText("Yeni kısayola bas. Vazgeçmek için Esc.");
            message->show();
        } else if (message->styleSheet().contains("#888")) {
            message->hide();
        }
    });
    connect(recorder, &KeyRecorder::recorded, this, [this](const QKeySequence &k) {
        QString problem = Settings::hotKeyProblem(k);
        if (!problem.isEmpty()) {
            message->setStyleSheet("color: #c0392b");
            message->setText(problem);
            message->show();
            return;
        }
        apply(k);
    });
    connect(hotKey, &GlobalHotKey::statusChanged, this, &SettingsDialog::showStatus);
    showStatus();

    // ---------------------------------------------------------------- genel
    g = new QGroupBox("Genel");
    form = new QFormLayout(g);
    form->addRow(check("Oturum açılınca başlat", Settings::autostart(), [](bool v) { Settings::setAutostart(v); }));
    root->addWidget(g);

    // ---------------------------------------------------------------- seçim ekranı
    g = new QGroupBox("Seçim ekranı");
    form = new QFormLayout(g);
    form->addRow("Karartma", slider(15, 75, Settings::dimPercent(), [](int v) { Settings::setDimPercent(v); }));
    form->addRow(check("Başta ipucu metnini göster", Settings::showHint(), [](bool v) { Settings::setShowHint(v); }));
    form->addRow(check("Son kullanılan renk ve kalınlığı hatırla", Settings::rememberStyle(),
                       [](bool v) { Settings::setRememberStyle(v); }));
    root->addWidget(g);

    // ---------------------------------------------------------------- kaydetme
    g = new QGroupBox("Kaydetme ve kopyalama");
    form = new QFormLayout(g);
    auto *dir = new QLineEdit(QDir::toNativeSeparators(Settings::saveDir()).replace(QDir::homePath(), "~"));
    dir->setReadOnly(true);
    dir->setMinimumWidth(220);
    auto *browse = new QPushButton("Değiştir…");
    connect(browse, &QPushButton::clicked, this, [this, dir] {
        QString d = QFileDialog::getExistingDirectory(this, "Kayıt klasörünü seç", Settings::saveDir());
        if (d.isEmpty()) return;
        Settings::setSaveDir(d);
        dir->setText(QString(d).replace(QDir::homePath(), "~"));
    });
    row = new QHBoxLayout;
    row->addWidget(dir);
    row->addWidget(browse);
    form->addRow("Kayıt klasörü", row);

    auto *format = new QComboBox;
    format->addItems({"PNG", "JPEG"});
    format->setCurrentIndex(Settings::format());
    form->addRow("Biçim", format);
    QWidget *quality = slider(50, 100, Settings::jpegQuality(), [](int v) { Settings::setJpegQuality(v); });
    form->addRow("JPEG kalitesi", quality);
    quality->setEnabled(Settings::format() == 1);
    connect(format, &QComboBox::currentIndexChanged, this, [quality](int i) {
        Settings::setFormat(i);
        quality->setEnabled(i == 1);
    });
    form->addRow(check("Ctrl+S sormadan doğrudan kayıt klasörüne kaydetsin", Settings::quickSave(),
                       [](bool v) { Settings::setQuickSave(v); }));
    form->addRow(check("Kaydettikten sonra panoya da kopyala", Settings::copyAfterSave(),
                       [](bool v) { Settings::setCopyAfterSave(v); }));
    root->addWidget(g);

    // ---------------------------------------------------------------- güncelleme
    g = new QGroupBox("Güncelleme");
    form = new QFormLayout(g);
    auto *checkNow = new QPushButton("Şimdi denetle");
    connect(checkNow, &QPushButton::clicked, this, [checkForUpdates] { checkForUpdates(); });
    row = new QHBoxLayout;
    auto *ver = new QLabel(Updater::current().toString());
    ver->setStyleSheet("color: #888");
    row->addWidget(ver);
    row->addWidget(checkNow);
    row->addStretch();
    form->addRow("Sürüm", row);
    form->addRow(check("Güncellemeleri otomatik denetle", Settings::autoUpdate(), [](bool v) { Settings::setAutoUpdate(v); }));
    root->addWidget(g);

    auto *buttons = new QDialogButtonBox(QDialogButtonBox::Close);
    connect(buttons, &QDialogButtonBox::rejected, this, &QDialog::close);
    root->addWidget(buttons);

    resetButton->setVisible(Settings::hotKey() != Settings::defaultHotKey());
}

void SettingsDialog::apply(const QKeySequence &k)
{
    Settings::setHotKey(k);
    recorder->setKey(k);
    recorder->stop();
    hotKey->setKey(k);
    message->hide();
    resetButton->setVisible(k != Settings::defaultHotKey());
}

void SettingsDialog::showStatus()
{
    QString exe = qEnvironmentVariableIsSet("APPIMAGE") ? qEnvironmentVariable("APPIMAGE")
                                                        : QCoreApplication::applicationFilePath();
    QString text = hotKey->status();
    if (!hotKey->active())
        text += QString("<br><br>Masaüstü ayarlarında (Klavye › Özel kısayollar) şu komuta bir kısayol atayabilirsin:"
                        "<br><code>\"%1\" --capture</code>")
                    .arg(exe.toHtmlEscaped());
    status->setText(text);
    status->setStyleSheet(hotKey->active() ? "color: #888" : "");
}
