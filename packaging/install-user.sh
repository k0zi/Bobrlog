#!/usr/bin/env bash
# Publishes Bobrlog (self-contained) and installs it for the current user only:
#   ~/.local/share/bobrlog/   application (+ bundled background service in service/)
#   ~/.local/bin/bobrlog      launcher symlink
#   ~/.local/share/applications/    GNOME menu entry
# Usage: packaging/install-user.sh [--uninstall]
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/bobrlog"
BIN_DIR="$HOME/.local/bin"
DESKTOP_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
ICON_BASE="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor"
ICON_SIZES=(32 48 64 128 256 512)

# Earlier versions were installed under the name "journalreader".
remove_legacy() {
    local data="${XDG_DATA_HOME:-$HOME/.local/share}"
    rm -rf "$data/journalreader" "$BIN_DIR/journalreader" "$DESKTOP_DIR/journalreader.desktop" \
        "$ICON_BASE/scalable/apps/journalreader.svg"
}

remove_icons() {
    for size in "${ICON_SIZES[@]}"; do
        rm -f "$ICON_BASE/${size}x${size}/apps/bobrlog.png"
    done
}

if [[ "${1:-}" == "--uninstall" ]]; then
    rm -rf "$APP_DIR" "$BIN_DIR/bobrlog" "$DESKTOP_DIR/bobrlog.desktop"
    remove_icons
    remove_legacy
    echo "Bobrlog eltávolítva. (A háttérszolgáltatást az alkalmazás Beállítások oldalán lehet eltávolítani.)"
    exit 0
fi

dotnet publish "$ROOT/src/Bobrlog.App/Bobrlog.App.csproj" \
    -c Release -r linux-x64 --self-contained true -o "$APP_DIR"

remove_legacy
mkdir -p "$BIN_DIR" "$DESKTOP_DIR"
ln -sf "$APP_DIR/bobrlog" "$BIN_DIR/bobrlog"
for size in "${ICON_SIZES[@]}"; do
    install -D -m 644 "$ROOT/packaging/icons/bobrlog-$size.png" "$ICON_BASE/${size}x${size}/apps/bobrlog.png"
done
gtk-update-icon-cache -q -t "$ICON_BASE" 2>/dev/null || true
sed "s|@BIN@|$APP_DIR/bobrlog|" "$ROOT/packaging/bobrlog.desktop" > "$DESKTOP_DIR/bobrlog.desktop"
update-desktop-database "$DESKTOP_DIR" 2>/dev/null || true

echo "Telepítve: $APP_DIR"
echo "Indítás: 'bobrlog' parancs vagy a GNOME alkalmazásmenü (Bobrlog)."
