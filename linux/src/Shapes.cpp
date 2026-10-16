#include "Shapes.h"

#include <QFontMetricsF>
#include <QPainterPath>
#include <QtMath>

QPen Shape::roundPen(QColor c, double w)
{
    QPen p(c, w);
    p.setCapStyle(Qt::RoundCap);
    p.setJoinStyle(Qt::RoundJoin);
    return p;
}

QPointF Shape::snap45(QPointF a, QPointF b)
{
    double dx = b.x() - a.x(), dy = b.y() - a.y();
    double len = std::hypot(dx, dy);
    double step = M_PI / 4;
    double ang = std::round(std::atan2(dy, dx) / step) * step;
    return QPointF(std::round(a.x() + std::cos(ang) * len), std::round(a.y() + std::sin(ang) * len));
}

static QRectF box(QPointF a, QPointF b)
{
    return QRectF(a, b).normalized();
}

// ---------------------------------------------------------------- serbest çizim

FreehandShape::FreehandShape(QPointF start, QColor c, double w, bool marker)
    : Shape(marker ? QColor(c.red(), c.green(), c.blue(), 110) : c, marker ? std::max(w * 4, 12.0) : w)
    , marker(marker)
    , box(start, QSizeF(0, 0))
{
    pts.append(start);
}

void FreehandShape::update(QPointF p, bool shift)
{
    if (shift) {
        // Shift: düz çizgi (fosforlu kalemde yatay)
        QPointF a = pts.first();
        pts = {a, marker ? QPointF(p.x(), a.y()) : p};
        box = ::box(pts[0], pts[1]);
        return;
    }
    if (pts.last() != p) {
        pts.append(p);
        box = box.united(QRectF(p, QSizeF(0, 0)));
    }
}

QRectF FreehandShape::bounds() const
{
    return box.adjusted(-width, -width, width, width);
}

void FreehandShape::draw(QPainter &g) const
{
    if (pts.size() == 1) {
        g.setPen(Qt::NoPen);
        g.setBrush(color);
        g.drawEllipse(pts[0], width / 2, width / 2);
        return;
    }
    // Noktalar arası orta noktalardan geçen eğri: fare örneklemesindeki köşeleri yumuşatır.
    QPainterPath path(pts[0]);
    for (int i = 1; i + 1 < pts.size(); i++) {
        QPointF mid = (pts[i] + pts[i + 1]) / 2;
        path.quadTo(pts[i], mid);
    }
    path.lineTo(pts.last());
    g.setPen(roundPen(color, width));
    g.setBrush(Qt::NoBrush);
    g.drawPath(path);
}

// ---------------------------------------------------------------- çizgi ve ok

LineShape::LineShape(QPointF start, QColor c, double w, bool arrow)
    : Shape(c, w), a(start), b(start), arrow(arrow)
{
}

void LineShape::update(QPointF p, bool shift)
{
    b = shift ? snap45(a, p) : p;
}

bool LineShape::isEmpty() const
{
    return std::abs(b.x() - a.x()) < 3 && std::abs(b.y() - a.y()) < 3;
}

QRectF LineShape::bounds() const
{
    double pad = arrow ? std::max(12.0, width * 4) : width;
    return box(a, b).adjusted(-pad, -pad, pad, pad);
}

void LineShape::draw(QPainter &g) const
{
    g.setPen(roundPen(color, width));
    g.setBrush(Qt::NoBrush);
    if (!arrow) {
        g.drawLine(a, b);
        return;
    }

    double dx = b.x() - a.x(), dy = b.y() - a.y();
    double len = std::hypot(dx, dy);
    if (len < 1) return;
    double ux = dx / len, uy = dy / len;
    double head = std::min(std::max(12.0, width * 4), len);
    double half = head * 0.5;
    QPointF base(b.x() - ux * head, b.y() - uy * head);

    // Gövde ok başının içinde biter, yuvarlak uç başın dışına taşmaz.
    g.drawLine(a, QPointF(b.x() - ux * head * 0.6, b.y() - uy * head * 0.6));
    QPolygonF tip;
    tip << b << QPointF(base.x() - uy * half, base.y() + ux * half) << QPointF(base.x() + uy * half, base.y() - ux * half);
    g.setPen(Qt::NoPen);
    g.setBrush(color);
    g.drawPolygon(tip);
}

// ---------------------------------------------------------------- dikdörtgen

RectShape::RectShape(QPointF start, QColor c, double w) : Shape(c, w), a(start), b(start) {}

void RectShape::update(QPointF p, bool shift)
{
    if (shift) {
        double dx = p.x() - a.x(), dy = p.y() - a.y();
        double d = std::max(std::abs(dx), std::abs(dy));
        b = QPointF(a.x() + (dx < 0 ? -d : d), a.y() + (dy < 0 ? -d : d));
    } else {
        b = p;
    }
}

bool RectShape::isEmpty() const
{
    return std::abs(b.x() - a.x()) < 2 && std::abs(b.y() - a.y()) < 2;
}

QRectF RectShape::bounds() const
{
    return box(a, b).adjusted(-width, -width, width, width);
}

void RectShape::draw(QPainter &g) const
{
    QPen pen(color, width);
    pen.setJoinStyle(Qt::MiterJoin);
    g.setPen(pen);
    g.setBrush(Qt::NoBrush);
    g.drawRect(box(a, b));
}

// ---------------------------------------------------------------- metin

TextShape::TextShape(QPointF at, QColor c, double w) : Shape(c, w), p(at) {}

QFont TextShape::font() const
{
    QFont f;
    f.setPixelSize(int(std::round(14 + width * 2.5)));
    f.setBold(true);
    return f;
}

QRectF TextShape::bounds() const
{
    QFontMetricsF fm(font());
    const QStringList lines = text.split('\n');
    double maxW = 0;
    for (const QString &l : lines) maxW = std::max(maxW, fm.horizontalAdvance(l));
    double lh = fm.height();
    return QRectF(p.x() - 8, p.y() - lh / 2 - 6, std::max(maxW, 8.0) + 20, lines.size() * lh + 12);
}

void TextShape::render(QPainter &g, bool editing) const
{
    QFont f = font();
    QFontMetricsF fm(f);
    const QStringList lines = text.split('\n');
    double lh = fm.height();
    double x = p.x(), y = p.y() - lh / 2;
    double maxW = 0;
    g.setFont(f);
    g.setPen(color);
    for (int i = 0; i < lines.size(); i++) {
        g.drawText(QPointF(x, y + i * lh + fm.ascent()), lines[i]);
        maxW = std::max(maxW, fm.horizontalAdvance(lines[i]));
    }
    if (!editing) return;

    double cx = x + fm.horizontalAdvance(lines.last()) + 1;
    double cy = y + (lines.size() - 1) * lh;
    g.setPen(QPen(color, std::max(1.5, (14 + width * 2.5) / 14)));
    g.drawLine(QPointF(cx, cy + 2), QPointF(cx, cy + lh - 2));

    QPen frame(QColor(255, 255, 255, 170), 1, Qt::DashLine);
    g.setPen(frame);
    g.setBrush(Qt::NoBrush);
    g.drawRect(QRectF(x - 5, y - 3, std::max(maxW, 8.0) + 12, lines.size() * lh + 6));
}
