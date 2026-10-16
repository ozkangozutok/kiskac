#pragma once

#include "Capture.h"
#include "Shapes.h"

#include <QFont>
#include <QPixmap>
#include <QPointer>
#include <QWidget>

#include <memory>
#include <vector>

class OverlayWindow;

// Bir çekim oturumu: ekran başına bir pencere. Bir ekranda seçim başlayınca diğerleri temizlenir.
class OverlaySession : public QObject {
    Q_OBJECT
public:
    explicit OverlaySession(const QList<ScreenShot> &shots, QObject *parent = nullptr);
    ~OverlaySession() override;

    void show();
    void bringToFront();
    void close();
    void selectionStarted(OverlayWindow *w);
    // Dosya penceresi açılırken klavye yakalaması bırakılır (X11).
    void releaseInput();
    void restoreInput(OverlayWindow *w);

    const std::vector<OverlayWindow *> &windows() const { return wins; }

signals:
    void closed();

private:
    std::vector<OverlayWindow *> wins;
    bool isClosed = false;
};

enum class Tool { Select, Pen, Line, Arrow, Rect, Marker, Text };

struct ToolButton {
    QString id;
    QString tip;
    int tool = -1; // Tool ya da -1
    int colorIndex = -1;
    bool separatorBefore = false;
    QRect rect;
};

class OverlayWindow : public QWidget {
    Q_OBJECT
public:
    OverlayWindow(OverlaySession *session, const ScreenShot &shot);

    void reset();
    QImage renderResult() const; // seçili alan, çizimlerle birlikte tam piksel çözünürlüğünde
    QScreen *targetScreen() const { return screen_; }

    // İç denetimler için
    QRect selection() const { return sel; }

protected:
    void paintEvent(QPaintEvent *e) override;
    void mousePressEvent(QMouseEvent *e) override;
    void mouseMoveEvent(QMouseEvent *e) override;
    void mouseReleaseEvent(QMouseEvent *e) override;
    void mouseDoubleClickEvent(QMouseEvent *e) override;
    void wheelEvent(QWheelEvent *e) override;
    void keyPressEvent(QKeyEvent *e) override;
    void keyReleaseEvent(QKeyEvent *e) override;
    void closeEvent(QCloseEvent *e) override;

private:
    enum class Mode { None, Selecting, Moving, Resizing, Drawing };

    void buildButtons();
    void layoutToolbar();
    QRect tooltipRect() const;
    QString sizeText() const;
    QRect sizeLabelRect() const;
    QRect hintRect() const;
    QRect visualBounds() const;
    void updateView();
    void updateCurrentShape();
    QPoint handlePoint(int i) const;
    int hitHandle(QPoint p) const;
    ToolButton *hitButton(QPoint p);
    Qt::CursorShape cursorFor(QPoint p) const;
    QRect resizeRect(QRect r, int handle, int dx, int dy) const;
    QPoint clamp(QPoint p) const;
    bool toolbarVisible() const { return hasSel && mode != Mode::Selecting; }
    QColor currentColor() const;

    void press(ToolButton &b);
    void setTool(Tool t);
    void undo();
    void commitText();
    void cancelText();
    void copyAndClose();
    void saveAs();

    void drawFrame(QPainter &g);
    void drawPill(QPainter &g, const QRect &r, int alpha, const QString &text);
    void drawToolbar(QPainter &g);
    void drawIcon(QPainter &g, const ToolButton &b);

    OverlaySession *session;
    QPointer<QScreen> screen_;
    QImage image;
    QPixmap shot, dimmed;
    double pixelScale = 1;
    QFont uiFont, letterFont;

    QRect sel;
    bool hasSel = false;
    Mode mode = Mode::None;
    QPoint dragStart;
    QRect selStart;
    int resizeHandle = -1;
    QPointF lastPoint;

    Tool tool = Tool::Select;
    int colorIndex = 0;
    double penWidth = 3;
    std::vector<std::unique_ptr<Shape>> shapes;
    std::unique_ptr<Shape> current;
    QRect currentBounds;
    std::unique_ptr<TextShape> editing;

    std::vector<ToolButton> buttons;
    QRect toolbarRect;
    int hover = -1;
    QRect lastVisual;
    int wheelAccum = 0;
};
