#pragma once

#include <QIcon>
#include <QImage>

// Kıskaç logosu: mercan zemin, beyaz yengeç kıskacı, ağzında sarı bir ekran parçası.
// Windows (Logo.cs) ve macOS (Logo.swift) ile aynı geometri.
namespace Logo {
QImage image(int size);
QIcon icon();
}
