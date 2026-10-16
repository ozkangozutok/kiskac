cmake_minimum_required(VERSION 3.16)

# Sürüm kökteki VERSION dosyasından gelir (Windows ve macOS ile ortak).
file(READ "${CMAKE_CURRENT_SOURCE_DIR}/../VERSION" KISKAC_VERSION)
string(STRIP "${KISKAC_VERSION}" KISKAC_VERSION)

project(kiskac VERSION ${KISKAC_VERSION} LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_AUTOMOC ON)
if(NOT CMAKE_BUILD_TYPE)
    set(CMAKE_BUILD_TYPE Release)
endif()

find_package(Qt6 6.2 REQUIRED COMPONENTS Widgets Network DBus)
find_package(PkgConfig REQUIRED)
pkg_check_modules(XCB REQUIRED IMPORTED_TARGET xcb xcb-keysyms)

add_executable(kiskac
    src/main.cpp
    src/App.cpp
    src/Capture.cpp
    src/GlobalHotKey.cpp
    src/Logo.cpp
    src/Overlay.cpp
    src/SelfTest.cpp
    src/Settings.cpp
    src/SettingsDialog.cpp
    src/Shapes.cpp
    src/Updater.cpp
)
target_compile_definitions(kiskac PRIVATE KISKAC_VERSION="${KISKAC_VERSION}")
target_compile_options(kiskac PRIVATE -Wall -Wextra)
target_link_libraries(kiskac PRIVATE Qt6::Widgets Qt6::Network Qt6::DBus PkgConfig::XCB)

include(GNUInstallDirs)
install(TARGETS kiskac RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR})
install(FILES kiskac.desktop DESTINATION ${CMAKE_INSTALL_DATADIR}/applications)
install(FILES ${CMAKE_CURRENT_BINARY_DIR}/kiskac.png DESTINATION ${CMAKE_INSTALL_DATADIR}/icons/hicolor/256x256/apps OPTIONAL)
