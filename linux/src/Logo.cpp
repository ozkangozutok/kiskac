#include "Logo.h"

#include <QLinearGradient>
#include <QPainter>
#include <QPainterPath>
#include <QPainterPathStroker>
#include <QPixmap>
#include <QtMath>

namespace {

const QColor BgTop(255, 122, 69);
const QColor BgBottom(226, 36, 90);
const QColor Piece(255, 214, 72);

// 32 birimlik ızgara. Isırık çemberinin merkezi gövdenin içinde olduğu için
// ağız kenarda daralır ve çeneler içe doğru kıvrılır.
const QPointF BodyCenter(14.5, 17.5);
const double BodyRadius = 10;
const QPointF BiteCenter(19.2, 12.3);
const double BiteRadius = 5.6;
const QPointF PieceCenter(19.6, 11.9);

double deg(double r) { return r * 180 / M_PI; }
double mod360(double v)
{
    v = std::fmod(v, 360);
    return v < 0 ? v + 360 : v;
}
double angleOf(QPointF c, QPointF p) { return deg(std::atan2(p.y() - c.y(), p.x() - c.x())); }
bool inSweep(double start, double sweep, double a) { return mod360(a - start) <= sweep; }

// Qt'nin arcTo açıları saat yönünün tersine ve y yukarı varsayar; ızgara y aşağı olduğu için işaret çevrilir.
void addArc(QPainterPath &p, QPointF c, double r, double start, double sweep)
{
    p.arcTo(QRectF(c.x() - r, c.y() - r, 2 * r, 2 * r), -start, -sweep);
}

// Büyük bir daireden ikinci bir daireyi "ısırarak" iki sivri çeneli kıskaç oluşturur.
QPainterPath clawPath()
{
    QPointF c1 = BodyCenter, c2 = BiteCenter;
    double r1 = BodyRadius, r2 = BiteRadius;
    double dx = c2.x() - c1.x(), dy = c2.y() - c1.y();
    double d = std::hypot(dx, dy);
    double a = (r1 * r1 - r2 * r2 + d * d) / (2 * d);
    double h = std::sqrt(r1 * r1 - a * a);
    double ux = dx / d, uy = dy / d;
    QPointF pA(c1.x() + a * ux - h * uy, c1.y() + a * uy + h * ux);
    QPointF pB(c1.x() + a * ux + h * uy, c1.y() + a * uy - h * ux);

    double toBite = deg(std::atan2(dy, dx));
    double toBody = deg(std::atan2(-dy, -dx));

    double aA = angleOf(c1, pA), aB = angleOf(c1, pB);
    double start = aA, sweep = mod360(aB - aA);
    if (inSweep(start, sweep, toBite)) {
        start = aB;
        sweep = mod360(aA - aB);
    }
    QPointF end = start == aA ? pB : pA;
    QPointF begin = start == aA ? pA : pB;

    double e2 = angleOf(c2, end), b2 = angleOf(c2, begin);
    double cw = mod360(b2 - e2);
    double sweep2 = inSweep(e2, cw, toBody) ? cw : -(360 - cw);

    QPainterPath p;
    p.moveTo(start == aA ? pA : pB);
    addArc(p, c1, r1, start, sweep);
    addArc(p, c2, r2, e2, sweep2);
    p.closeSubpath();
    return p;
}

QPainterPath clawWithArm()
{
    QPainterPath arm(BodyCenter);
    arm.lineTo(7, 25);
    QPainterPathStroker st;
    st.setWidth(6.5);
    st.setCapStyle(Qt::RoundCap);
    QPainterPath p = clawPath().united(st.createStroke(arm));
    return p;
}

QPainterPath roundRect(QRectF r, double radius)
{
    QPainterPath p;
    p.addRoundedRect(r, radius, radius);
    return p;
}

} // namespace

QImage Logo::image(int size)
{
    QImage img(size, size, QImage::Format_ARGB32_Premultiplied);
    img.fill(Qt::transparent);
    QPainter g(&img);
    g.setRenderHint(QPainter::Antialiasing);
    double s = size / 32.0;
    g.scale(s, s);

    // Zemin ve üstte hafif parlama
    QPainterPath bg = roundRect(QRectF(1, 1, 30, 30), 7.5);
    QLinearGradient grad(QPointF(1, 1), QPointF(31, 31));
    grad.setColorAt(0, BgTop);
    grad.setColorAt(1, BgBottom);
    g.fillPath(bg, grad);
    g.save();
    g.setClipRect(QRectF(0, 0, 32, 15.9));
    QLinearGradient gloss(QPointF(0, 0), QPointF(0, 16));
    gloss.setColorAt(0, QColor(255, 255, 255, 50));
    gloss.setColorAt(1, QColor(255, 255, 255, 0));
    g.fillPath(bg, gloss);
    g.restore();

    QPainterPath claw = clawWithArm();
    if (size >= 24)
        g.fillPath(claw.translated(0, 1.1), QColor(90, 0, 30, 55));
    g.fillPath(claw, Qt::white);

    // Kıskacın ağzındaki ekran parçası (eğik)
    g.save();
    g.translate(PieceCenter);
    g.rotate(-12);
    double ps = size <= 20 ? 6.4 : 5.6;
    QPainterPath piece = roundRect(QRectF(-ps / 2, -ps / 2, ps, ps), ps * 0.22);
    g.fillPath(piece, Piece);
    if (size >= 32) g.strokePath(piece, QPen(QColor(150, 60, 0, 120), 0.5));
    g.restore();
    return img;
}

QIcon Logo::icon()
{
    QIcon icon;
    for (int s : {16, 22, 24, 32, 48, 64, 128, 256}) icon.addPixmap(QPixmap::fromImage(image(s)));
    return icon;
}
