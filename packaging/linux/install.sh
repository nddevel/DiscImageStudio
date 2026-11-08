#!/usr/bin/env bash
# Builds the GTK frontend as a self-contained app and installs it for the current user
# (or system-wide with PREFIX=/usr/local, run as root).
#
#   packaging/linux/install.sh            # installs into ~/.local
#   PREFIX=/usr/local packaging/linux/install.sh
#   packaging/linux/install.sh --uninstall
#
# Requirements: .NET 10 SDK, GTK >= 4.14 and libadwaita >= 1.7 at runtime.
set -euo pipefail

APP_ID=io.github.nddevel.DiscImageStudio
PREFIX=${PREFIX:-$HOME/.local}
LIBDIR="$PREFIX/lib/disc-image-studio-gnome"
BINDIR="$PREFIX/bin"
SHARE="$PREFIX/share"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"

if [[ "${1:-}" == "--uninstall" ]]; then
  rm -rf "$LIBDIR"
  rm -f "$BINDIR/disc-image-studio-gnome" \
        "$SHARE/applications/$APP_ID.desktop" \
        "$SHARE/metainfo/$APP_ID.metainfo.xml" \
        "$SHARE/mime/packages/$APP_ID.xml" \
        "$SHARE/icons/hicolor/256x256/apps/$APP_ID.png"
  update-mime-database "$SHARE/mime" 2>/dev/null || true
  echo "Removed Disc Image Studio from $PREFIX"
  exit 0
fi

case "$(uname -m)" in
  x86_64) RID=linux-x64 ;;
  aarch64|arm64) RID=linux-arm64 ;;
  *) echo "Unsupported architecture: $(uname -m)" >&2; exit 1 ;;
esac

dotnet publish "$ROOT/src/DiscImageStudio.Gnome/DiscImageStudio.Gnome.csproj" \
  -c Release -r "$RID" --self-contained true -o "$LIBDIR"

install -d "$BINDIR" "$SHARE/applications" "$SHARE/metainfo" "$SHARE/mime/packages" \
  "$SHARE/icons/hicolor/256x256/apps"
ln -sf "$LIBDIR/disc-image-studio-gnome" "$BINDIR/disc-image-studio-gnome"
install -m 644 "$HERE/$APP_ID.desktop" "$SHARE/applications/$APP_ID.desktop"
install -m 644 "$HERE/$APP_ID.metainfo.xml" "$SHARE/metainfo/$APP_ID.metainfo.xml"
install -m 644 "$HERE/$APP_ID.mime.xml" "$SHARE/mime/packages/$APP_ID.xml"
install -m 644 "$HERE/$APP_ID.png" "$SHARE/icons/hicolor/256x256/apps/$APP_ID.png"
update-mime-database "$SHARE/mime" 2>/dev/null || true
update-desktop-database "$SHARE/applications" 2>/dev/null || true
gtk4-update-icon-cache -qtf "$SHARE/icons/hicolor" 2>/dev/null || gtk-update-icon-cache -qtf "$SHARE/icons/hicolor" 2>/dev/null || true
echo "Installed Disc Image Studio to $PREFIX (run: disc-image-studio-gnome)"
