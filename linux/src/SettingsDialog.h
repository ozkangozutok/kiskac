#pragma once

#include <QDialog>
#include <QKeySequence>
#include <QPushButton>

#include <functional>

class GlobalHotKey;
class QLabel;

// Tıklanınca bir sonraki tuş birleşimini kaydeden düğme. Klavyeyi yakalar; PrtSc gibi tuşlar da gelir.
class KeyRecorder : public QPushButton {
    Q_OBJECT
public:
    explicit KeyRecorder(QWidget *parent = nullptr);
    void setKey(const QKeySequence &k);
    bool isRecording() const { return recording; }
    void stop();

signals:
    void recorded(const QKeySequence &k);
    void recordingChanged(bool recording);

protected:
    void keyPressEvent(QKeyEvent *e) override;
    void focusOutEvent(QFocusEvent *e) override;

private:
    void start();
    QKeySequence key;
    bool recording = false;
};

class SettingsDialog : public QDialog {
    Q_OBJECT
public:
    SettingsDialog(GlobalHotKey *hotKey, std::function<void()> checkForUpdates, QWidget *parent = nullptr);

private:
    void apply(const QKeySequence &k);
    void showStatus();

    GlobalHotKey *hotKey;
    KeyRecorder *recorder;
    QPushButton *resetButton;
    QLabel *message;
    QLabel *status;
};
