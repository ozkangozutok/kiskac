#pragma once

#include <QColor>
#include <QPainter>
#include <QPointF>
#include <QRectF>
#include <QString>
#include <QVector>

// Tüm şekiller ekranın mantıksal (cihazdan bağımsız) koordinatlarında tutulur.
class Shape {
public:
    Shape(QColor color, double width) : color(color), width(width) {}
    virtual ~Shape() = default;

    virtual void update(QPointF p, bool shift) { Q_UNUSED(p) Q_UNUSED(shift) }
    virtual void draw(QPainter &g) const = 0;
    virtual bool isEmpty() const { return false; }
    virtual QRectF bounds() const = 0;

    QColor color;
    double width;

protected:
    static QPen roundPen(QColor c, double w);
    static QPointF snap45(QPointF a, QPointF b);
};

class FreehandShape : public Shape {
public:
    FreehandShape(QPointF start, QColor c, double w, bool marker);
    void update(QPointF p, bool shift) override;
    void draw(QPainter &g) const override;
    QRectF bounds() const override;

private:
    QVector<QPointF> pts;
    bool marker;
    QRectF box;
};

class LineShape : public Shape {
public:
    LineShape(QPointF start, QColor c, double w, bool arrow);
    void update(QPointF p, bool shift) override;
    void draw(QPainter &g) const override;
    bool isEmpty() const override;
    QRectF bounds() const override;

private:
    QPointF a, b;
    bool arrow;
};

class RectShape : public Shape {
public:
    RectShape(QPointF start, QColor c, double w);
    void update(QPointF p, bool shift) override;
    void draw(QPainter &g) const override;
    bool isEmpty() const override;
    QRectF bounds() const override;

private:
    QPointF a, b;
};

class TextShape : public Shape {
public:
    TextShape(QPointF at, QColor c, double w);
    void draw(QPainter &g) const override { render(g, false); }
    void drawEditing(QPainter &g) const { render(g, true); }
    bool isEmpty() const override { return text.trimmed().isEmpty(); }
    QRectF bounds() const override;

    QString text;

private:
    QFont font() const;
    void render(QPainter &g, bool editing) const;
    QPointF p;
};
