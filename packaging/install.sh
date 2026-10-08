#!/usr/bin/env bash
# Builds the system package for this distribution and installs it with the native package manager
# (apt on Debian/Ubuntu, dnf on Fedora/RHEL, zypper on openSUSE/SUSE).
#   /usr/lib/bobrlog/   application (+ bundled background service in service/)
#   /usr/bin/bobrlog    launcher
# Usage: packaging/install.sh [--no-build] | --uninstall
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/artifacts/packages"
SUDO=""
[[ $EUID -eq 0 ]] || SUDO=sudo

# Per-user installs made by the former install-user.sh (and the old "journalreader" name) would shadow
# the system menu entry.
remove_user_install() {
    local data="${XDG_DATA_HOME:-$HOME/.local/share}"
    local size
    rm -rf "$data/bobrlog" "$data/journalreader" "$HOME/.local/bin/bobrlog" "$HOME/.local/bin/journalreader" \
        "$data/applications/bobrlog.desktop" "$data/applications/journalreader.desktop" \
        "$data/icons/hicolor/scalable/apps/journalreader.svg"
    for size in 32 48 64 128 256 512; do
        rm -f "$data/icons/hicolor/${size}x${size}/apps/bobrlog.png"
    done
}

if command -v apt-get >/dev/null; then
    format=deb
    install_cmd=(apt-get install -y --reinstall)
    remove_cmd=(apt-get remove -y bobrlog)
elif command -v dnf >/dev/null; then
    format=rpm
    install_cmd=(dnf install -y)
    remove_cmd=(dnf remove -y bobrlog)
elif command -v zypper >/dev/null; then
    format=rpm
    install_cmd=(zypper --non-interactive install --allow-unsigned-rpm --force)
    remove_cmd=(zypper --non-interactive remove bobrlog)
else
    echo "Unsupported distribution: apt-get, dnf or zypper is required." >&2
    exit 1
fi

if [[ "${1:-}" == "--uninstall" ]]; then
    $SUDO "${remove_cmd[@]}"
    remove_user_install
    echo "Bobrlog eltávolítva."
    exit 0
fi

[[ "${1:-}" == "--no-build" ]] || "$ROOT/packaging/build-packages.sh" "$format"

package="$(ls -t "$OUT"/bobrlog*."$format" 2>/dev/null | head -n 1)"
if [[ -z "$package" ]]; then
    echo "No .$format package in $OUT; run packaging/build-packages.sh first." >&2
    exit 1
fi

$SUDO "${install_cmd[@]}" "$package"
remove_user_install

echo "Telepítve: $(basename "$package")"
echo "Indítás: 'bobrlog' parancs vagy az alkalmazásmenü (Bobrlog)."
