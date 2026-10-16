#include "Updater.h"

#include "Settings.h"

#include <QCoreApplication>
#include <QDateTime>
#include <QCryptographicHash>
#include <QDesktopServices>
#include <QFile>
#include <QFileInfo>
#include <QJsonArray>
#include <QJsonDocument>
#include <QJsonObject>
#include <QMessageBox>
#include <QNetworkAccessManager>
#include <QNetworkReply>
#include <QProcess>
#include <QPushButton>
#include <QSysInfo>

#include <cstdio>

namespace {
const char *Repo = "ozkangozutok/kiskac";
const qint64 CheckIntervalSecs = 24 * 3600;

QString appImagePath()
{
    return QString::fromLocal8Bit(qgetenv("APPIMAGE"));
}

QNetworkRequest request(const QUrl &url)
{
    QNetworkRequest req(url);
    req.setHeader(QNetworkRequest::UserAgentHeader, "Kiskac/" + Updater::current().toString());
    req.setRawHeader("Accept", "application/vnd.github+json");
    req.setAttribute(QNetworkRequest::RedirectPolicyAttribute, QNetworkRequest::NoLessSafeRedirectPolicy);
    req.setTransferTimeout(60000);
    return req;
}
} // namespace

Updater::Updater(QObject *parent) : QObject(parent), net(new QNetworkAccessManager(this)) {}

QVersionNumber Updater::current()
{
    return QVersionNumber::fromString(QCoreApplication::applicationVersion());
}

QString Updater::assetName()
{
    QString arch = QSysInfo::currentCpuArchitecture();
    if (arch == "arm64") arch = "aarch64";
    return QString("Kiskac-%1.AppImage").arg(arch);
}

Updater::Release Updater::parseRelease(const QByteArray &json)
{
    Release r;
    QJsonObject o = QJsonDocument::fromJson(json).object();
    r.tag = o.value("tag_name").toString();
    r.body = o.value("body").toString();
    r.htmlUrl = QUrl(o.value("html_url").toString(QString("https://github.com/%1/releases").arg(Repo)));
    QString v = r.tag.startsWith('v') || r.tag.startsWith('V') ? r.tag.mid(1) : r.tag;
    r.version = QVersionNumber::fromString(v).normalized();
    const QString want = assetName();
    for (const QJsonValue &a : o.value("assets").toArray()) {
        QJsonObject ao = a.toObject();
        if (ao.value("name").toString() == want) {
            r.assetUrl = QUrl(ao.value("browser_download_url").toString());
            r.digest = ao.value("digest").toString();
        }
    }
    return r;
}

QString Updater::verify(const QString &path, const Release &r)
{
    QFile f(path);
    if (!f.open(QIODevice::ReadOnly)) return "İndirilen dosya açılamadı.";
    if (f.peek(4) != QByteArray("\x7f" "ELF", 4)) return "İndirilen dosya bir AppImage değil.";
    QCryptographicHash h(QCryptographicHash::Sha256);
    h.addData(&f);
    f.close();
    if (!r.digest.startsWith("sha256:") || QString::fromLatin1(h.result().toHex()) != r.digest.mid(7).toLower())
        return "İndirilen dosyanın özeti tutmuyor.";

    // Yeni sürümün kendi bildirdiği sürüm etiketle aynı olmalı.
    QFile::setPermissions(path, QFile::permissions(path) | QFile::ExeOwner | QFile::ExeGroup | QFile::ExeOther);
    QProcess p;
    // FUSE olmayan sistemlerde de çalışsın diye AppImage açılıp çalıştırılır.
    QProcessEnvironment env = QProcessEnvironment::systemEnvironment();
    env.insert("APPIMAGE_EXTRACT_AND_RUN", "1");
    p.setProcessEnvironment(env);
    p.start(path, {"--version"});
    if (!p.waitForFinished(15000)) {
        p.kill();
        return "İndirilen sürüm çalıştırılamadı.";
    }
    QString out = QString::fromUtf8(p.readAllStandardOutput()).trimmed();
    QVersionNumber v = QVersionNumber::fromString(out.section(' ', -1)).normalized();
    if (v != r.version) return QString("İndirilen sürüm (%1) beklenenle (%2) aynı değil.").arg(out, r.version.toString());
    return QString();
}

void Updater::startAutomaticChecks()
{
    timer.setInterval(20000);
    connect(&timer, &QTimer::timeout, this, [this] {
        timer.setInterval(3 * 3600 * 1000);
        QDateTime last = Settings::lastUpdateCheck();
        if (Settings::autoUpdate() && canInterrupt()
            && (!last.isValid() || last.secsTo(QDateTime::currentDateTimeUtc()) > CheckIntervalSecs))
            check(false);
    });
    timer.start();
}

void Updater::check(bool userInitiated)
{
    if (busy) return;
    busy = true;
    emit busyChanged(true);
    QNetworkReply *reply = net->get(request(QUrl(QString("https://api.github.com/repos/%1/releases/latest").arg(Repo))));
    connect(reply, &QNetworkReply::finished, this, [this, reply, userInitiated] {
        reply->deleteLater();
        busy = false;
        emit busyChanged(false);
        if (reply->error() != QNetworkReply::NoError) {
            if (userInitiated) message("Güncellemeler denetlenemedi: " + reply->errorString(), true);
            return;
        }
        Release r = parseRelease(reply->readAll());
        // İstek sürerken çekim başladıysa sonraki denetime bırakılır.
        if (!userInitiated && !canInterrupt()) return;
        bool newer = !r.version.isNull() && r.version > current();
        bool installable = newer && (!r.assetUrl.isEmpty() || appImagePath().isEmpty());
        // Sürüm yayınlandı ama AppImage henüz eklenmediyse (CI derliyor) 24 saat beklenmez.
        if (!newer || installable) Settings::setLastUpdateCheck(QDateTime::currentDateTimeUtc());
        if (installable && (userInitiated || Settings::skippedVersion() != r.version.toString()))
            offer(r);
        else if (userInitiated)
            message(newer ? QString("Kıskaç %1 yayınlandı ama Linux dosyası henüz hazır değil. Biraz sonra yeniden dene.")
                                .arg(r.version.toString())
                          : QString("Kıskaç güncel. Kullandığın %1 sürümü en son sürüm.").arg(current().toString()));
    });
}

void Updater::offer(const Release &r)
{
    bool canInstall = !appImagePath().isEmpty() && !r.assetUrl.isEmpty();
    QMessageBox box;
    box.setWindowTitle("Kıskaç güncellemesi");
    box.setText(QString("<b>Kıskaç %1 hazır</b>").arg(r.version.toString()));
    box.setInformativeText(canInstall
                               ? QString("Kullandığın sürüm: %1. Güncelleme indirilip kurulduktan sonra Kıskaç yeniden açılır.")
                                     .arg(current().toString())
                               : QString("Kullandığın sürüm: %1. Kıskaç AppImage olarak çalışmadığı için yeni sürümü indirme "
                                         "sayfasından kurman gerekiyor.")
                                     .arg(current().toString()));
    if (!r.body.isEmpty()) box.setDetailedText(r.body);
    QPushButton *now = box.addButton(canInstall ? "Şimdi güncelle" : "İndirme sayfasını aç", QMessageBox::AcceptRole);
    box.addButton("Sonra", QMessageBox::RejectRole);
    QPushButton *skip = box.addButton("Bu sürümü atla", QMessageBox::DestructiveRole);
    box.setDefaultButton(now);
    box.setWindowFlag(Qt::WindowStaysOnTopHint);
    box.exec();
    if (box.clickedButton() == now) {
        if (canInstall)
            install(r);
        else
            QDesktopServices::openUrl(r.htmlUrl);
    } else if (box.clickedButton() == skip) {
        Settings::setSkippedVersion(r.version.toString());
    }
}

void Updater::install(const Release &r)
{
    QString target = appImagePath();
    QString fresh = target + ".new";
    if (!QFileInfo(QFileInfo(target).absolutePath()).isWritable()) {
        message("Kıskaç'ın bulunduğu klasöre yazma iznin yok. Yeni sürümü indirme sayfasından elle kurabilirsin.", true);
        QDesktopServices::openUrl(r.htmlUrl);
        return;
    }
    if (!r.digest.startsWith("sha256:")) {
        message("Güncellemenin özeti GitHub'da yok; güvenlik için elle kurman gerekiyor.", true);
        QDesktopServices::openUrl(r.htmlUrl);
        return;
    }

    busy = true;
    emit busyChanged(true);
    auto *file = new QFile(fresh, this);
    if (!file->open(QIODevice::WriteOnly | QIODevice::Truncate)) {
        busy = false;
        emit busyChanged(false);
        message("Güncelleme dosyası yazılamadı: " + file->errorString(), true);
        file->deleteLater();
        return;
    }
    QNetworkReply *reply = net->get(request(r.assetUrl));
    connect(reply, &QNetworkReply::readyRead, this, [reply, file] { file->write(reply->readAll()); });
    connect(reply, &QNetworkReply::finished, this, [this, reply, file, fresh, r] {
        file->write(reply->readAll());
        file->close();
        file->deleteLater();
        reply->deleteLater();
        busy = false;
        emit busyChanged(false);
        QString problem = reply->error() != QNetworkReply::NoError ? "İndirme başarısız: " + reply->errorString()
                                                                   : verify(fresh, r);
        if (!problem.isEmpty()) {
            QFile::remove(fresh);
            message("Güncelleme kurulamadı: " + problem, true);
            return;
        }
        swapAndRestart(fresh);
    });
}

// Çalışan AppImage'ın üzerine taşımak güvenlidir: süreç eski dosyayı açık tutar.
// Yeni sürüm bu süreç kapandıktan sonra açılır; yoksa tek örnek denetimi onu hemen kapatırdı.
void Updater::swapAndRestart(const QString &fresh)
{
    QString target = appImagePath();
    if (std::rename(QFile::encodeName(fresh).constData(), QFile::encodeName(target).constData()) != 0) {
        QFile::remove(fresh);
        message("Güncelleme kurulamadı: dosya yerine konamadı.", true);
        return;
    }
    QString script = "while kill -0 \"$1\" 2>/dev/null; do sleep 0.2; done; exec \"$2\" --after-update";
    QProcess::startDetached("/bin/sh", {"-c", script, "kiskac-update", QString::number(QCoreApplication::applicationPid()), target});
    QCoreApplication::quit();
}

void Updater::message(const QString &text, bool warning)
{
    QMessageBox box(warning ? QMessageBox::Warning : QMessageBox::Information, "Kıskaç", text);
    box.setWindowFlag(Qt::WindowStaysOnTopHint);
    box.exec();
}
