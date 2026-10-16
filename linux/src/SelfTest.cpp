// CI'da (GitHub Actions, Xvfb altında) çalışan iç denetimler: kiskac --selftest
// Seçim/çizim/kırpma zinciri, X11 yakalama ve kısayol, ayar doğrulaması ve güncelleme yanıtı denetlenir.

#include "Capture.h"
#include "GlobalHotKey.h"
#include "Logo.h"
#include "Overlay.h"
#include "Settings.h"
#include "Updater.h"

#include <QApplication>
#include <QCoreApplication>
#include <QEventLoop>
#include <QGuiApplication>
#include <QKeyEvent>
#include <QMouseEvent>
#include <QPainter>
#include <QScreen>
#include <QTimer>

#include <cstdio>

namespace {

int fails = 0;

void check(const char *name, bool ok)
{
    printf("%s  %s\n", ok ? "OK  " : "FAIL", name);
    if (!ok) fails++;
}

void mouse(QWidget *w, QEvent::Type t, QPoint p)
{
    Qt::MouseButtons buttons = t == QEvent::MouseButtonRelease ? Qt::NoButton : Qt::LeftButton;
    QMouseEvent e(t, QPointF(p), QPointF(w->mapToGlobal(p)), Qt::LeftButton, buttons, Qt::NoModifier);
    QCoreApplication::sendEvent(w, &e);
}

void key(QWidget *w, int k, const QString &text)
{
    QKeyEvent e(QEvent::KeyPress, k, Qt::NoModifier, text);
    QCoreApplication::sendEvent(w, &e);
}

bool near(QRgb a, QColor b)
{
    return std::abs(qRed(a) - b.red()) < 40 && std::abs(qGreen(a) - b.green()) < 40 && std::abs(qBlue(a) - b.blue()) < 40;
}

} // namespace

int runSelfTest()
{
    // ---------------------------------------------------------------- kısayol doğrulaması
    check("PrtSc geçerli", Settings::hotKeyProblem(QKeySequence(Qt::Key_Print)).isEmpty());
    check("Ctrl+Shift+X geçerli", Settings::hotKeyProblem(QKeySequence("Ctrl+Shift+X")).isEmpty());
    check("Super+Shift+S geçerli", Settings::hotKeyProblem(QKeySequence("Meta+Shift+S")).isEmpty());
    check("Ctrl+C engelli", !Settings::hotKeyProblem(QKeySequence("Ctrl+C")).isEmpty());
    check("Alt+K engelli", !Settings::hotKeyProblem(QKeySequence("Alt+K")).isEmpty());
    check("K engelli", !Settings::hotKeyProblem(QKeySequence("K")).isEmpty());
    check("Ctrl+Alt+T engelli", !Settings::hotKeyProblem(QKeySequence("Ctrl+Alt+T")).isEmpty());
    check("Görünen ad", Settings::hotKeyText(QKeySequence("Ctrl+Shift+X")) == "Ctrl+Shift+X"
                            && Settings::hotKeyText(QKeySequence(Qt::Key_Print)) == "PrtSc");

    // ---------------------------------------------------------------- güncelleme yanıtı
    QByteArray json = QString(R"({"tag_name":"v9.9.9","html_url":"https://h","body":null,"assets":[
        {"name":"Kiskac.exe","digest":null,"browser_download_url":"https://e/exe"},
        {"name":"%1","digest":"sha256:abc","browser_download_url":"https://e/app"}]})")
                          .arg(Updater::assetName())
                          .toUtf8();
    Updater::Release r = Updater::parseRelease(json);
    check("Sürüm 9.9.9", r.version == QVersionNumber(9, 9, 9));
    check("AppImage adresi ve özeti", r.assetUrl == QUrl("https://e/app") && r.digest == "sha256:abc");
    Updater::Release r2 = Updater::parseRelease(R"({"tag_name":"v1.2","assets":[]})");
    check("AppImage yokken adres boş", r2.assetUrl.isEmpty() && r2.version == QVersionNumber(1, 2));
    check("Sürüm karşılaştırma", QVersionNumber(1, 10, 0) > QVersionNumber(1, 9, 2));

    // ---------------------------------------------------------------- logo
    QImage logo = Logo::image(64);
    check("Logo çizildi", !logo.isNull() && qAlpha(logo.pixel(32, 32)) == 255 && qAlpha(logo.pixel(0, 0)) == 0);

    // ---------------------------------------------------------------- seçim, çizim, kırpma
    QScreen *scr = QGuiApplication::primaryScreen();
    QRect geo = scr->geometry();
    // 2x ölçekli sahte ekran görüntüsü: sol üst 30x20 nokta kırmızı, gerisi gri
    QImage fake(geo.width() * 2, geo.height() * 2, QImage::Format_RGB32);
    fake.fill(QColor(150, 150, 150));
    {
        QPainter p(&fake);
        p.fillRect(QRect(0, 0, 60, 40), Qt::red);
    }
    fake.setDevicePixelRatio(2);
    {
        OverlaySession session({ScreenShot{scr, fake}});
        OverlayWindow *w = session.windows().front();
        w->resize(geo.size());
        mouse(w, QEvent::MouseButtonPress, {0, 0});
        mouse(w, QEvent::MouseMove, {60, 40});
        mouse(w, QEvent::MouseButtonRelease, {60, 40});
        check("Seçim 60x40", w->selection() == QRect(0, 0, 60, 40));
        key(w, Qt::Key_R, "r");
        mouse(w, QEvent::MouseButtonPress, {35, 25});
        mouse(w, QEvent::MouseMove, {55, 35});
        mouse(w, QEvent::MouseButtonRelease, {55, 35});
        QImage out = w->renderResult();
        check("Çıktı 120x80 piksel", out.size() == QSize(120, 80));
        check("Sol üst kırmızı", near(out.pixel(5, 5), Qt::red));
        check("Ortası gri", near(out.pixel(90, 60), QColor(150, 150, 150)));
        check("Dikdörtgen kenarı kırmızı", near(out.pixel(70, 60), QColor(235, 45, 45)));
        QImage view = w->grab().toImage();
        check("Seçim dışı karartılmış", qRed(view.pixel(200, 200)) < 120);
        check("Seçim içi parlak", near(view.pixel(40, 30), QColor(150, 150, 150)));
        out.save("selftest-result.png");
        view.save("selftest-view.png");
    }

    // ---------------------------------------------------------------- X11 yakalama ve kısayol
    if (!Capturer::isWayland()) {
        Capturer cap;
        QList<ScreenShot> shots;
        QEventLoop loop;
        QObject::connect(&cap, &Capturer::finished, &loop, [&](const QList<ScreenShot> &s) {
            shots = s;
            loop.quit();
        });
        QObject::connect(&cap, &Capturer::failed, &loop, [&](const QString &) { loop.quit(); });
        QTimer::singleShot(0, &cap, [&] { cap.capture(); });
        QTimer::singleShot(10000, &loop, &QEventLoop::quit);
        loop.exec();
        check("X11 ekran yakalama", shots.size() == QGuiApplication::screens().size() && !shots.isEmpty()
                                        && shots.front().image.width() >= geo.width());

        GlobalHotKey hk;
        check("X11 kısayol (PrtSc) yakalandı", hk.setKey(QKeySequence(Qt::Key_Print)) && hk.active());
        check("X11 kısayol (Ctrl+Shift+X) yakalandı", hk.setKey(QKeySequence("Ctrl+Shift+X")) && hk.active());
    }

    printf(fails == 0 ? "Tüm denetimler geçti.\n" : "%d denetim başarısız.\n", fails);
    return fails == 0 ? 0 : 1;
}
