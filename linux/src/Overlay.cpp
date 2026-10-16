#include "Overlay.h"

#include "Settings.h"

#include <QClipboard>
#include <QCloseEvent>
#include <QDateTime>
#include <QFileDialog>
#include <QFileInfo>
#include <QGuiApplication>
#include <QImageWriter>
#include <QKeyEvent>
#include <QMessageBox>
#include <QMouseEvent>
#include <QPainter>
#include <QPainterPath>
#include <QScreen>
#include <QTimer>
#include <QWheelEvent>
#include <QWindow>

namespace {

const QColor Palette[] = {QColor(235, 45, 45), QColor(255, 195, 0), QColor(40, 185, 85),
                          QColor(0, 125, 255), QColor(20, 20, 20), QColor(Qt::white)};
const int PaletteSize = 6;
const double Widths[] = {2, 3, 5, 8, 12};
const QColor Accent(53, 132, 228);
const QString HintText = QStringLiteral("Alanı seçmek için sürükleyin   •   Tıklayın: tüm ekran   •   Esc: iptal");

// QRect'in right()/bottom() değerleri bir eksik olduğu için seçim sınırları elle tutulur.
int R(const QRect &r) { return r.x() + r.width(); }
int B(const QRect &r) { return r.y() + r.height(); }

QRect fromLTRB(int l, int t, int r, int b)
{
    return QRect(std::min(l, r), std::min(t, b), std::abs(r - l), std::abs(b - t));
}

} // namespace

// ================================================================ oturum

OverlaySession::OverlaySession(const QList<ScreenShot> &shots, QObject *parent) : QObject(parent)
{
    for (const ScreenShot &s : shots)
        if (s.screen) wins.push_back(new OverlayWindow(this, s));
}

OverlaySession::~OverlaySession()
{
    for (OverlayWindow *w : wins) delete w;
}

void OverlaySession::show()
{
    for (OverlayWindow *w : wins) {
        w->setScreen(w->targetScreen());
        w->setGeometry(w->targetScreen()->geometry());
        w->showFullScreen();
    }
    // Pencereler eşlendikten sonra imlecin bulunduğu ekran odak alır.
    QTimer::singleShot(0, this, [this] { bringToFront(); });
}

void OverlaySession::bringToFront()
{
    if (wins.empty()) return;
    OverlayWindow *key = wins.front();
    QScreen *under = QGuiApplication::screenAt(QCursor::pos());
    for (OverlayWindow *w : wins)
        if (w->targetScreen() == under) key = w;
    for (OverlayWindow *w : wins) w->raise();
    restoreInput(key);
}

void OverlaySession::releaseInput()
{
    for (OverlayWindow *w : wins) w->releaseKeyboard();
}

void OverlaySession::restoreInput(OverlayWindow *w)
{
    w->raise();
    w->activateWindow();
    w->setFocus(Qt::ActiveWindowFocusReason);
    // X11'de pencere yöneticisi odağı geciktirebilir; klavye doğrudan yakalanır.
    if (!Capturer::isWayland()) w->grabKeyboard();
}

void OverlaySession::selectionStarted(OverlayWindow *w)
{
    for (OverlayWindow *o : wins)
        if (o != w) o->reset();
}

void OverlaySession::close()
{
    if (isClosed) return;
    isClosed = true;
    releaseInput();
    for (OverlayWindow *w : wins) w->hide();
    emit closed();
}

// ================================================================ pencere

OverlayWindow::OverlayWindow(OverlaySession *session, const ScreenShot &s)
    : QWidget(nullptr, Qt::Window | Qt::FramelessWindowHint | Qt::WindowStaysOnTopHint)
    , session(session)
    , screen_(s.screen)
    , image(s.image)
{
    setAttribute(Qt::WA_OpaquePaintEvent);
    setAttribute(Qt::WA_NoSystemBackground);
    setMouseTracking(true);
    setFocusPolicy(Qt::StrongFocus);
    setCursor(Qt::CrossCursor);
    setWindowTitle("Kıskaç");

    QRect g = screen_->geometry();
    pixelScale = double(image.width()) / std::max(1, g.width());
    resize(g.size());

    // Görüntü ve karartılmış hali bir kez hazırlanır; çizimde yalnızca kopyalanır.
    shot = QPixmap::fromImage(image);
    shot.setDevicePixelRatio(pixelScale);
    QImage dim = image.copy();
    {
        QPainter p(&dim);
        p.fillRect(dim.rect(), QColor(0, 0, 0, Settings::dimPercent() * 255 / 100));
    }
    dimmed = QPixmap::fromImage(dim);
    dimmed.setDevicePixelRatio(pixelScale);

    uiFont = font();
    uiFont.setPixelSize(13);
    letterFont = font();
    letterFont.setPixelSize(17);
    letterFont.setBold(true);

    if (Settings::rememberStyle()) {
        colorIndex = std::clamp(Settings::lastColor(), 0, PaletteSize - 1);
        penWidth = std::clamp(Settings::lastWidth(), 1, 40);
    }
    buildButtons();
}

void OverlayWindow::buildButtons()
{
    auto addTool = [this](const char *id, Tool t, const QString &tip) {
        ToolButton b;
        b.id = id;
        b.tool = int(t);
        b.tip = tip;
        buttons.push_back(b);
    };
    addTool("select", Tool::Select, "Seç / taşı (S)");
    addTool("pen", Tool::Pen, "Kalem (P)");
    addTool("line", Tool::Line, "Çizgi (L)  •  Shift: 45°");
    addTool("arrow", Tool::Arrow, "Ok (A)  •  Shift: 45°");
    addTool("rect", Tool::Rect, "Dikdörtgen (R)  •  Shift: kare");
    addTool("marker", Tool::Marker, "Fosforlu kalem (M)");
    addTool("text", Tool::Text, "Metin (T)  •  Enter: bitir");
    for (int i = 0; i < PaletteSize; i++) {
        ToolButton b;
        b.id = "color";
        b.colorIndex = i;
        b.tip = "Renk";
        b.separatorBefore = i == 0;
        buttons.push_back(b);
    }
    auto addAction = [this](const char *id, const QString &tip, bool sep = false) {
        ToolButton b;
        b.id = id;
        b.tip = tip;
        b.separatorBefore = sep;
        buttons.push_back(b);
    };
    addAction("width", "Kalınlık  •  fare tekerleği");
    addAction("undo", "Geri al (Ctrl+Z)", true);
    addAction("save", "Kaydet (Ctrl+S)");
    addAction("copy", "Kopyala (Ctrl+C / Enter)");
    addAction("close", "Kapat (Esc)");
}

QColor OverlayWindow::currentColor() const
{
    return Palette[colorIndex];
}

void OverlayWindow::reset()
{
    if (!hasSel && !editing) return;
    hasSel = false;
    sel = QRect();
    shapes.clear();
    current.reset();
    editing.reset();
    tool = Tool::Select;
    mode = Mode::None;
    hover = -1;
    update();
    lastVisual = QRect();
}

QPoint OverlayWindow::clamp(QPoint p) const
{
    return QPoint(std::clamp(p.x(), 0, width()), std::clamp(p.y(), 0, height()));
}

// ---------------------------------------------------------------- yerleşim

void OverlayWindow::layoutToolbar()
{
    if (!hasSel) {
        toolbarRect = QRect();
        return;
    }
    const int cell = 32, sep = 10, pad = 5, gap = 8;
    int w = pad * 2;
    for (const ToolButton &b : buttons) w += cell + (b.separatorBefore ? sep : 0);
    int h = cell + pad * 2;

    int x = R(sel) - w;
    int y = B(sel) + gap;
    if (y + h > height() - 2) {
        y = sel.y() - gap - h;
        if (y < 2) y = B(sel) - h - gap;
    }
    x = std::max(2, std::min(x, width() - w - 2));
    y = std::max(2, std::min(y, height() - h - 2));
    toolbarRect = QRect(x, y, w, h);

    int cx = x + pad;
    for (ToolButton &b : buttons) {
        if (b.separatorBefore) cx += sep;
        b.rect = QRect(cx, y + pad, cell, cell);
        cx += cell;
    }
}

QRect OverlayWindow::tooltipRect() const
{
    if (hover < 0 || !toolbarVisible()) return QRect();
    QFontMetrics fm(uiFont);
    const ToolButton &b = buttons[hover];
    int w = fm.horizontalAdvance(b.tip) + 16, h = fm.height() + 8;
    int x = b.rect.center().x() - w / 2;
    int y = toolbarRect.y() - h - 6;
    if (y < 0) y = B(toolbarRect) + 6;
    x = std::max(2, std::min(x, width() - w - 2));
    return QRect(x, y, w, h);
}

QString OverlayWindow::sizeText() const
{
    return QString("%1 × %2").arg(qRound(sel.width() * pixelScale)).arg(qRound(sel.height() * pixelScale));
}

QRect OverlayWindow::sizeLabelRect() const
{
    QFontMetrics fm(uiFont);
    int w = fm.horizontalAdvance(sizeText()) + 12, h = fm.height() + 4;
    int x = sel.x(), y = sel.y() - h - 4;
    if (y < 0) {
        x = sel.x() + 4;
        y = sel.y() + 4;
    }
    return QRect(x, y, w, h);
}

QRect OverlayWindow::hintRect() const
{
    QFontMetrics fm(uiFont);
    int w = fm.horizontalAdvance(HintText) + 28, h = fm.height() + 16;
    return QRect((width() - w) / 2, 40, w, h);
}

QRect OverlayWindow::visualBounds() const
{
    if (!hasSel) return hintRect();
    QRect r = sel.adjusted(-12, -12, 12, 12);
    r = r.united(sizeLabelRect().adjusted(-2, -2, 2, 2));
    if (toolbarVisible()) {
        r = r.united(toolbarRect.adjusted(-2, -2, 2, 2));
        QRect t = tooltipRect();
        if (!t.isNull()) r = r.united(t.adjusted(-2, -2, 2, 2));
    }
    return r;
}

// Yalnızca değişen bölgeyi yeniden çizer; 4K ekranda bile akıcı kalsın diye.
void OverlayWindow::updateView()
{
    layoutToolbar();
    QRect nb = visualBounds();
    update(lastVisual.isNull() ? nb : lastVisual.united(nb));
    lastVisual = nb;
}

// Çizim sırasında yalnızca çizilen şeklin alanı yenilenir.
void OverlayWindow::updateCurrentShape()
{
    if (!current) return;
    QRect nb = current->bounds().toAlignedRect().intersected(sel.adjusted(-1, -1, 1, 1));
    update(currentBounds.isNull() ? nb : currentBounds.united(nb));
    currentBounds = nb;
}

QPoint OverlayWindow::handlePoint(int i) const
{
    int l = sel.x(), t = sel.y(), r = R(sel), b = B(sel);
    int mx = l + sel.width() / 2, my = t + sel.height() / 2;
    switch (i) {
    case 0: return {l, t};
    case 1: return {mx, t};
    case 2: return {r, t};
    case 3: return {r, my};
    case 4: return {r, b};
    case 5: return {mx, b};
    case 6: return {l, b};
    default: return {l, my};
    }
}

int OverlayWindow::hitHandle(QPoint p) const
{
    const int tol = 7;
    for (int i = 0; i < 8; i++) {
        QPoint h = handlePoint(i);
        if (std::abs(p.x() - h.x()) <= tol && std::abs(p.y() - h.y()) <= tol) return i;
    }
    return -1;
}

ToolButton *OverlayWindow::hitButton(QPoint p)
{
    for (ToolButton &b : buttons)
        if (b.rect.contains(p)) return &b;
    return nullptr;
}

Qt::CursorShape OverlayWindow::cursorFor(QPoint p) const
{
    if (!hasSel) return Qt::CrossCursor;
    if (toolbarRect.contains(p)) return Qt::ArrowCursor;
    if (tool == Tool::Select) {
        switch (hitHandle(p)) {
        case 0: case 4: return Qt::SizeFDiagCursor;
        case 2: case 6: return Qt::SizeBDiagCursor;
        case 1: case 5: return Qt::SizeVerCursor;
        case 3: case 7: return Qt::SizeHorCursor;
        }
        return sel.contains(p) ? Qt::SizeAllCursor : Qt::CrossCursor;
    }
    if (sel.contains(p)) return tool == Tool::Text ? Qt::IBeamCursor : Qt::CrossCursor;
    return Qt::ArrowCursor;
}

// ---------------------------------------------------------------- fare

void OverlayWindow::mousePressEvent(QMouseEvent *e)
{
    if (e->button() == Qt::RightButton) {
        if (editing)
            cancelText();
        else
            session->close();
        return;
    }
    if (e->button() != Qt::LeftButton) return;

    QPoint p = e->position().toPoint();
    lastPoint = e->position();
    if (toolbarVisible() && toolbarRect.contains(p)) {
        if (ToolButton *b = hitButton(p)) press(*b);
        return;
    }

    if (editing) commitText();

    if (!hasSel || tool == Tool::Select) {
        int h = hasSel ? hitHandle(p) : -1;
        if (h >= 0) {
            mode = Mode::Resizing;
            resizeHandle = h;
        } else if (hasSel && sel.contains(p)) {
            mode = Mode::Moving;
        } else {
            session->selectionStarted(this);
            mode = Mode::Selecting;
            hasSel = true;
            sel = QRect(p, QSize(0, 0));
            shapes.clear();
        }
        dragStart = p;
        selStart = sel;
    } else {
        if (!sel.contains(p)) return;
        QColor c = currentColor();
        QPointF fp = e->position();
        switch (tool) {
        case Tool::Text:
            editing = std::make_unique<TextShape>(fp, c, penWidth);
            updateView();
            return;
        case Tool::Pen: current = std::make_unique<FreehandShape>(fp, c, penWidth, false); break;
        case Tool::Marker: current = std::make_unique<FreehandShape>(fp, c, penWidth, true); break;
        case Tool::Line: current = std::make_unique<LineShape>(fp, c, penWidth, false); break;
        case Tool::Arrow: current = std::make_unique<LineShape>(fp, c, penWidth, true); break;
        case Tool::Rect: current = std::make_unique<RectShape>(fp, c, penWidth); break;
        case Tool::Select: break;
        }
        mode = Mode::Drawing;
        currentBounds = QRect();
        updateCurrentShape();
        return;
    }
    updateView();
}

void OverlayWindow::mouseMoveEvent(QMouseEvent *e)
{
    QPoint p = e->position().toPoint();
    lastPoint = e->position();
    switch (mode) {
    case Mode::Selecting:
        sel = fromLTRB(dragStart.x(), dragStart.y(), clamp(p).x(), clamp(p).y());
        updateView();
        break;
    case Mode::Moving: {
        QRect r = selStart.translated(p - dragStart);
        r.moveLeft(std::max(0, std::min(r.x(), width() - r.width())));
        r.moveTop(std::max(0, std::min(r.y(), height() - r.height())));
        sel = r;
        updateView();
        break;
    }
    case Mode::Resizing:
        sel = resizeRect(selStart, resizeHandle, p.x() - dragStart.x(), p.y() - dragStart.y());
        updateView();
        break;
    case Mode::Drawing:
        current->update(e->position(), e->modifiers() & Qt::ShiftModifier);
        updateCurrentShape();
        break;
    case Mode::None: {
        ToolButton *b = toolbarVisible() && toolbarRect.contains(p) ? hitButton(p) : nullptr;
        int idx = b ? int(b - buttons.data()) : -1;
        if (idx != hover) {
            hover = idx;
            updateView();
        }
        Qt::CursorShape c = cursorFor(p);
        if (cursor().shape() != c) setCursor(c);
        break;
    }
    }
}

QRect OverlayWindow::resizeRect(QRect r, int handle, int dx, int dy) const
{
    int l = r.x(), t = r.y(), rt = R(r), b = B(r);
    if (handle == 0 || handle == 6 || handle == 7) l += dx;
    if (handle == 2 || handle == 3 || handle == 4) rt += dx;
    if (handle == 0 || handle == 1 || handle == 2) t += dy;
    if (handle == 4 || handle == 5 || handle == 6) b += dy;
    QPoint a = clamp(QPoint(l, t)), c = clamp(QPoint(rt, b));
    return fromLTRB(a.x(), a.y(), c.x(), c.y());
}

void OverlayWindow::mouseReleaseEvent(QMouseEvent *e)
{
    if (e->button() != Qt::LeftButton) return;
    Mode m = mode;
    mode = Mode::None;
    if (m == Mode::Selecting && (sel.width() < 4 || sel.height() < 4)) {
        sel = rect(); // tek tıklama: o ekranın tamamı
    } else if (m == Mode::Drawing && current) {
        if (!current->isEmpty()) shapes.push_back(std::move(current));
        current.reset();
        update(currentBounds);
        currentBounds = QRect();
    }
    updateView();
    setCursor(cursorFor(e->position().toPoint()));
}

void OverlayWindow::mouseDoubleClickEvent(QMouseEvent *e)
{
    QPoint p = e->position().toPoint();
    if (e->button() == Qt::LeftButton && hasSel && tool == Tool::Select && sel.contains(p) && !toolbarRect.contains(p))
        copyAndClose();
    else
        mousePressEvent(e);
}

void OverlayWindow::wheelEvent(QWheelEvent *e)
{
    // Dokunmatik yüzey çok sayıda küçük adım gönderir; biriktirip tek adıma çevrilir.
    wheelAccum += e->angleDelta().y();
    if (std::abs(wheelAccum) < 120) return;
    int dir = wheelAccum > 0 ? 1 : -1;
    wheelAccum = 0;
    penWidth = std::clamp(penWidth + dir, 1.0, 40.0);
    if (editing) editing->width = penWidth;
    updateView();
}

// ---------------------------------------------------------------- klavye

void OverlayWindow::keyPressEvent(QKeyEvent *e)
{
    const bool ctrl = e->modifiers() & Qt::ControlModifier;
    const int key = e->key();
    const bool isReturn = key == Qt::Key_Return || key == Qt::Key_Enter;

    if (editing) {
        if (key == Qt::Key_Escape) {
            cancelText();
        } else if (isReturn) {
            if (e->modifiers() & Qt::ShiftModifier) {
                editing->text += '\n';
                updateView();
            } else {
                commitText();
            }
        } else if (key == Qt::Key_Backspace) {
            if (!editing->text.isEmpty()) editing->text.chop(1);
            updateView();
        } else if (ctrl && key == Qt::Key_V) {
            editing->text += QGuiApplication::clipboard()->text().replace("\r\n", "\n");
            updateView();
        } else if (ctrl && key == Qt::Key_C) {
            commitText();
            copyAndClose();
        } else if (ctrl && key == Qt::Key_S) {
            commitText();
            saveAs();
        } else if (ctrl && key == Qt::Key_Z) {
            cancelText();
        } else if (!ctrl && !e->text().isEmpty() && e->text().at(0).isPrint()) {
            // Türkçe karakterler ve AltGr ile yazılanlar (ör. @) doğrudan gelir.
            editing->text += e->text();
            updateView();
        }
        return;
    }

    if (key == Qt::Key_Escape) {
        session->close();
        return;
    }
    if (isReturn || (ctrl && key == Qt::Key_C)) {
        if (hasSel) copyAndClose();
        return;
    }
    if (ctrl) {
        if (key == Qt::Key_S && hasSel) saveAs();
        if (key == Qt::Key_Z) undo();
        return;
    }
    switch (key) {
    case Qt::Key_S: setTool(Tool::Select); break;
    case Qt::Key_P: setTool(Tool::Pen); break;
    case Qt::Key_L: setTool(Tool::Line); break;
    case Qt::Key_A: setTool(Tool::Arrow); break;
    case Qt::Key_R: setTool(Tool::Rect); break;
    case Qt::Key_M: setTool(Tool::Marker); break;
    case Qt::Key_T: setTool(Tool::Text); break;
    case Qt::Key_Shift:
        // Shift fare kıpırdamadan basılsa da çizgi/kare hemen güncellensin.
        if (mode == Mode::Drawing && current) {
            current->update(lastPoint, true);
            updateCurrentShape();
        }
        break;
    default: break;
    }
}

void OverlayWindow::keyReleaseEvent(QKeyEvent *e)
{
    if (e->key() == Qt::Key_Shift && mode == Mode::Drawing && current) {
        current->update(lastPoint, false);
        updateCurrentShape();
    }
}

void OverlayWindow::closeEvent(QCloseEvent *e)
{
    // Alt+F4 vb. ile kapatılırsa tüm oturum kapanır.
    e->ignore();
    session->close();
}

// ---------------------------------------------------------------- eylemler

void OverlayWindow::setTool(Tool t)
{
    if (!hasSel) return;
    tool = t;
    updateView();
}

void OverlayWindow::press(ToolButton &b)
{
    if (b.tool >= 0) {
        if (editing) commitText();
        tool = Tool(b.tool);
    } else if (b.colorIndex >= 0) {
        colorIndex = b.colorIndex;
        if (editing) editing->color = currentColor();
    } else if (b.id == "width") {
        double next = Widths[0];
        for (double w : Widths)
            if (w > penWidth) {
                next = w;
                break;
            }
        penWidth = next;
        if (editing) editing->width = penWidth;
    } else if (b.id == "undo") {
        undo();
    } else if (b.id == "save") {
        saveAs();
        return;
    } else if (b.id == "copy") {
        copyAndClose();
        return;
    } else if (b.id == "close") {
        session->close();
        return;
    }
    if (Settings::rememberStyle()) {
        Settings::setLastColor(colorIndex);
        Settings::setLastWidth(int(penWidth));
    }
    updateView();
}

void OverlayWindow::undo()
{
    if (editing)
        cancelText();
    else if (!shapes.empty())
        shapes.pop_back();
    update(sel.adjusted(-2, -2, 2, 2));
    updateView();
}

void OverlayWindow::commitText()
{
    if (!editing) return;
    if (!editing->isEmpty()) shapes.push_back(std::move(editing));
    editing.reset();
    updateView();
}

void OverlayWindow::cancelText()
{
    editing.reset();
    updateView();
}

QImage OverlayWindow::renderResult() const
{
    QRect px(qRound(sel.x() * pixelScale), qRound(sel.y() * pixelScale), qRound(sel.width() * pixelScale),
             qRound(sel.height() * pixelScale));
    px = px.intersected(image.rect());
    if (px.isEmpty()) return QImage();
    QImage out = image.copy(px);
    out.setDevicePixelRatio(1);
    if (shapes.empty()) return out; // çizim yoksa pikseller olduğu gibi

    QPainter g(&out);
    g.setRenderHint(QPainter::Antialiasing);
    g.setRenderHint(QPainter::TextAntialiasing);
    g.scale(pixelScale, pixelScale);
    g.translate(-sel.topLeft());
    for (const auto &s : shapes) s->draw(g);
    return out;
}

void OverlayWindow::copyAndClose()
{
    if (!hasSel || sel.width() < 1 || sel.height() < 1) return;
    commitText();
    QImage img = renderResult();
    if (img.isNull()) return;
    // Wayland'de pano yalnızca odaktaki pencereden yazılabilir: kapatmadan önce yazılır.
    QGuiApplication::clipboard()->setImage(img);
    if (Settings::rememberStyle()) {
        Settings::setLastColor(colorIndex);
        Settings::setLastWidth(int(penWidth));
    }
    session->close();
}

void OverlayWindow::saveAs()
{
    if (!hasSel || sel.width() < 1 || sel.height() < 1) return;
    commitText();
    QImage img = renderResult();
    if (img.isNull()) return;

    QString base = "Kıskaç_" + QDateTime::currentDateTime().toString("yyyy-MM-dd_HH-mm-ss");
    QString path;
    if (Settings::quickSave()) {
        // Sormadan kayıt klasörüne; aynı adlı dosya varsa numara eklenir.
        QString dir = Settings::saveDir(), ext = Settings::formatExtension();
        path = dir + "/" + base + ext;
        for (int n = 2; QFileInfo::exists(path); n++) path = QString("%1/%2 (%3)%4").arg(dir, base).arg(n).arg(ext);
    } else {
        session->releaseInput();
        QString filter = Settings::format() == 1 ? "JPEG resmi (*.jpg *.jpeg);;PNG resmi (*.png)"
                                                 : "PNG resmi (*.png);;JPEG resmi (*.jpg *.jpeg)";
        path = QFileDialog::getSaveFileName(this, "Ekran görüntüsünü kaydet",
                                            Settings::saveDir() + "/" + base + Settings::formatExtension(), filter);
        if (path.isEmpty()) {
            session->restoreInput(this);
            return;
        }
        if (QFileInfo(path).suffix().isEmpty()) path += Settings::formatExtension();
    }

    QString suffix = QFileInfo(path).suffix().toLower();
    QImageWriter w(path, suffix == "jpg" || suffix == "jpeg" ? "jpeg" : "png");
    if (suffix == "jpg" || suffix == "jpeg") w.setQuality(Settings::jpegQuality());
    if (!w.write(img)) {
        session->releaseInput();
        QMessageBox::warning(this, "Kıskaç", "Kaydedilemedi: " + w.errorString());
        session->restoreInput(this);
        return;
    }
    if (!Settings::quickSave()) Settings::setSaveDir(QFileInfo(path).absolutePath());
    if (Settings::copyAfterSave()) QGuiApplication::clipboard()->setImage(img);
    session->close();
}

// ---------------------------------------------------------------- çizim

void OverlayWindow::paintEvent(QPaintEvent *e)
{
    QPainter g(this);
    const QRect clip = e->rect();
    auto src = [this](const QRect &r) {
        return QRectF(r.x() * pixelScale, r.y() * pixelScale, r.width() * pixelScale, r.height() * pixelScale);
    };
    g.drawPixmap(QRectF(clip), dimmed, src(clip));
    if (hasSel) {
        QRect r = sel.intersected(clip);
        if (!r.isEmpty()) g.drawPixmap(QRectF(r), shot, src(r));
    }

    if (!hasSel) {
        if (Settings::showHint()) drawPill(g, hintRect(), 225, HintText);
        return;
    }

    g.setRenderHint(QPainter::Antialiasing);
    g.setRenderHint(QPainter::TextAntialiasing);
    g.save();
    g.setClipRect(sel);
    for (const auto &s : shapes) s->draw(g);
    if (current) current->draw(g);
    if (editing) editing->drawEditing(g);
    g.restore();

    drawFrame(g);
    drawPill(g, sizeLabelRect(), 210, sizeText());
    if (toolbarVisible()) drawToolbar(g);
}

void OverlayWindow::drawFrame(QPainter &g)
{
    g.setRenderHint(QPainter::Antialiasing, false);
    QRect r(sel.x() - 1, sel.y() - 1, sel.width() + 1, sel.height() + 1);
    g.setBrush(Qt::NoBrush);
    g.setPen(QPen(Qt::white, 1));
    g.drawRect(r);
    g.setPen(QPen(Accent, 1, Qt::DashLine));
    g.drawRect(r);

    if (tool != Tool::Select || mode == Mode::Selecting) return;
    const int hs = 8;
    for (int i = 0; i < 8; i++) {
        QPoint p = handlePoint(i);
        QRect hr(p.x() - hs / 2, p.y() - hs / 2, hs, hs);
        g.fillRect(hr, Qt::white);
        g.setPen(QPen(Accent, 1));
        g.drawRect(hr.adjusted(0, 0, -1, -1));
    }
}

void OverlayWindow::drawPill(QPainter &g, const QRect &r, int alpha, const QString &text)
{
    g.setRenderHint(QPainter::Antialiasing);
    QPainterPath path;
    path.addRoundedRect(QRectF(r), 6, 6);
    g.fillPath(path, QColor(25, 25, 28, alpha));
    g.setFont(uiFont);
    g.setPen(Qt::white);
    g.drawText(r, Qt::AlignCenter, text);
}

void OverlayWindow::drawToolbar(QPainter &g)
{
    g.setRenderHint(QPainter::Antialiasing);
    QPainterPath bg;
    bg.addRoundedRect(QRectF(toolbarRect).adjusted(0.5, 0.5, -0.5, -0.5), 8, 8);
    g.fillPath(bg, QColor(32, 32, 36, 245));
    g.strokePath(bg, QPen(QColor(255, 255, 255, 70), 1));

    for (int i = 0; i < int(buttons.size()); i++) {
        const ToolButton &b = buttons[i];
        if (b.separatorBefore)
            g.fillRect(QRectF(b.rect.x() - 5.5, b.rect.y() + 6, 1, b.rect.height() - 12), QColor(255, 255, 255, 60));
        bool active = b.tool >= 0 && Tool(b.tool) == tool;
        if (active || i == hover) {
            QPainterPath hp;
            hp.addRoundedRect(QRectF(b.rect).adjusted(2, 2, -2, -2), 5, 5);
            g.fillPath(hp, active ? Accent : QColor(255, 255, 255, 60));
        }
        drawIcon(g, b);
    }

    QRect tip = tooltipRect();
    if (!tip.isNull()) drawPill(g, tip, 240, buttons[hover].tip);
}

void OverlayWindow::drawIcon(QPainter &g, const ToolButton &b)
{
    QPointF c = QRectF(b.rect).center();
    auto P = [c](double x, double y) { return QPointF(c.x() + x, c.y() + y); };
    QPen pen(Qt::white, 2, Qt::SolidLine, Qt::RoundCap, Qt::RoundJoin);
    g.setRenderHint(QPainter::Antialiasing);
    g.setPen(pen);
    g.setBrush(Qt::NoBrush);

    if (b.id == "select") {
        QPolygonF arrow;
        arrow << P(-5, -9) << P(-5, 6) << P(-1.5, 2.5) << P(1.5, 9) << P(4, 8) << P(1, 1.5) << P(6, 1.5);
        g.setPen(Qt::NoPen);
        g.setBrush(Qt::white);
        g.drawPolygon(arrow);
    } else if (b.id == "pen") {
        QPainterPath p(P(-8, 5));
        p.cubicTo(P(-3, -9), P(2, 11), P(8, -5));
        g.drawPath(p);
    } else if (b.id == "line") {
        g.drawLine(P(-7, 7), P(7, -7));
    } else if (b.id == "arrow") {
        g.drawLine(P(-7, 7), P(3, -3));
        g.setPen(Qt::NoPen);
        g.setBrush(Qt::white);
        g.drawPolygon(QPolygonF() << P(8, -8) << P(-1, -6) << P(6, 1));
    } else if (b.id == "rect") {
        g.setPen(QPen(Qt::white, 2, Qt::SolidLine, Qt::SquareCap, Qt::MiterJoin));
        g.drawRect(QRectF(c.x() - 8, c.y() - 6, 16, 12));
    } else if (b.id == "marker") {
        g.fillRect(QRectF(c.x() - 9, c.y() - 3.5, 18, 7), QColor(255, 215, 0, 200));
    } else if (b.id == "text") {
        g.setFont(letterFont);
        g.drawText(b.rect, Qt::AlignCenter, "T");
    } else if (b.id == "color") {
        const double d = 15;
        if (b.colorIndex == colorIndex) g.drawEllipse(c, 11, 11);
        g.setPen(QPen(QColor(255, 255, 255, 90), 1));
        g.setBrush(Palette[b.colorIndex]);
        g.drawEllipse(c, d / 2, d / 2);
    } else if (b.id == "width") {
        double d = std::min(4 + penWidth * 1.1, 20.0);
        g.setPen(QPen(QColor(255, 255, 255, 120), 1));
        g.setBrush(currentColor());
        g.drawEllipse(c, d / 2, d / 2);
    } else if (b.id == "undo") {
        QPainterPath p;
        p.moveTo(P(-6, -2));
        p.lineTo(P(3, -2));
        p.cubicTo(P(9, -2), P(9, 8), P(3, 8));
        p.lineTo(P(-2, 8));
        g.drawPath(p);
        g.drawPolyline(QPolygonF() << P(-2, -6) << P(-6, -2) << P(-2, 2));
    } else if (b.id == "save") {
        g.drawLine(P(0, -8), P(0, 3));
        g.drawPolyline(QPolygonF() << P(-4, -1) << P(0, 3) << P(4, -1));
        g.drawPolyline(QPolygonF() << P(-8, 2) << P(-8, 8) << P(8, 8) << P(8, 2));
    } else if (b.id == "copy") {
        QPainterPath back;
        back.addRoundedRect(QRectF(c.x() - 7, c.y() - 8, 10, 12), 2, 2);
        g.drawPath(back);
        QPainterPath front;
        front.addRoundedRect(QRectF(c.x() - 3, c.y() - 4, 10, 12), 2, 2);
        g.fillPath(front, QColor(32, 32, 36));
        g.drawPath(front);
    } else if (b.id == "close") {
        g.drawLine(P(-6, -6), P(6, 6));
        g.drawLine(P(-6, 6), P(6, -6));
    }
}
