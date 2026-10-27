#!/usr/bin/env bash
# Sync the canonical WSL source to a disposable Windows build mirror and build/publish/run there.
#
#   ./build.sh            Debug build
#   ./build.sh publish    Release, self-contained win-x64 publish
#   ./build.sh run        Debug build, then launch the app on Windows
#
# The mirror is only a build copy: never edit files there. It is marked with a sentinel
# file, and the script refuses to sync (and delete) into a non-empty folder without it.
set -euo pipefail

MODE="${1:-build}"

SRC_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MIRROR_WIN='C:\Users\pcpc\source\GameMusicFlyout-build'
MIRROR_DIR="$(wslpath -u "$MIRROR_WIN")"
SENTINEL=".gmf-build-mirror"
DOTNET='/mnt/c/Program Files/dotnet/dotnet.exe'
PROJECT_REL='src\GameMusicFlyout\GameMusicFlyout.csproj'

if [[ ! -x "$DOTNET" ]]; then
    echo "error: .NET SDK not found at $DOTNET" >&2
    exit 1
fi

if [[ -d "$MIRROR_DIR" && ! -f "$MIRROR_DIR/$SENTINEL" && -n "$(ls -A "$MIRROR_DIR")" ]]; then
    echo "error: $MIRROR_WIN exists, is not empty, and is not marked as a build mirror." >&2
    echo "       Refusing to sync into it (sync deletes stale files)." >&2
    exit 1
fi

mkdir -p "$MIRROR_DIR"
touch "$MIRROR_DIR/$SENTINEL"

echo "==> Syncing $SRC_DIR -> $MIRROR_WIN"
# --delete removes stale/renamed source files; excluded paths (build output, sentinel) are left alone.
rsync -a --delete \
    --exclude '.git/' \
    --exclude 'bin/' \
    --exclude 'obj/' \
    --exclude 'publish/' \
    --exclude '.vs/' \
    --exclude "$SENTINEL" \
    "$SRC_DIR/" "$MIRROR_DIR/"

# Run Windows tools from a drive path, never \\wsl.localhost\...
cd "$MIRROR_DIR"

case "$MODE" in
    build|run)
        echo "==> dotnet build (Debug)"
        "$DOTNET" build "$PROJECT_REL" -c Debug -nologo -v minimal 2>&1 | tr -d '\r'
        EXE_WIN="$MIRROR_WIN\\src\\GameMusicFlyout\\bin\\Debug\\net10.0-windows10.0.19041.0\\GameMusicFlyout.exe"
        ;;
    publish)
        echo "==> dotnet publish (Release, win-x64, self-contained)"
        "$DOTNET" publish "$PROJECT_REL" -c Release -r win-x64 --self-contained true \
            -o "$MIRROR_WIN\\publish" -nologo -v minimal 2>&1 | tr -d '\r'
        EXE_WIN="$MIRROR_WIN\\publish\\GameMusicFlyout.exe"
        ;;
    *)
        echo "usage: $0 [build|publish|run]" >&2
        exit 2
        ;;
esac

echo "==> Output: $EXE_WIN"

if [[ "$MODE" == "run" ]]; then
    echo "==> Launching on Windows"
    powershell.exe -NoProfile -Command "Start-Process -FilePath '$EXE_WIN'" 2>&1 | tr -d '\r'
fi
