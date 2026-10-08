#!/usr/bin/env bash
# Builds the Bobrlog .deb and .rpm packages into artifacts/packages/.
#
# Versioning: Nerdbank.GitVersioning, version.json holds "<year>.<month>" (e.g. 26.10) and the git
# height becomes the patch number (26.10.0, 26.10.1, ...). When the month changes, version.json is
# updated to the current year/month first (commit it so the next builds count from there).
#
# Usage: packaging/build-packages.sh [--no-bump] [--skip-tests] [deb|rpm ...]
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/artifacts/packages"
STAGE="$ROOT/artifacts/stage"
TOOLS="$ROOT/artifacts/tools"
NFPM_VERSION=2.47.0
NFPM_SHA256=0660ca602b2d2d2ae4781a06c692b3eeb9d437ffea05b831d76e41f4a3188783

bump=true
tests=true
formats=()
for arg in "$@"; do
    case "$arg" in
        --no-bump) bump=false ;;
        --skip-tests) tests=false ;;
        deb|rpm) formats+=("$arg") ;;
        *) echo "Usage: $0 [--no-bump] [--skip-tests] [deb|rpm ...]" >&2; exit 2 ;;
    esac
done
[[ ${#formats[@]} -gt 0 ]] || formats=(deb rpm)

cd "$ROOT"
dotnet tool restore >/dev/null

# ── version ──
expected="$(date +%y).$(date +%-m)"
current="$(sed -n 's/^ *"version": *"\([^"]*\)".*/\1/p' version.json)"
if [[ "$current" != "$expected" ]]; then
    if $bump; then
        # Not "nbgv set-version": that rewrites version.json and drops the other settings.
        sed -i "s/^\( *\"version\": *\"\)[^\"]*\"/\1$expected\"/" version.json
        echo "version.json: $current -> $expected (commit this change)"
    else
        echo "warning: version.json is $current, current month would be $expected" >&2
    fi
fi

version="$(dotnet nbgv get-version -v SimpleVersion)"
prerelease=""
if [[ "$(dotnet nbgv get-version -v PublicRelease)" != "True" ]]; then
    # Builds outside main / release tags sort before the release of the same version.
    prerelease="g$(dotnet nbgv get-version -v GitCommitIdShort)"
fi
echo "Bobrlog version: $version${prerelease:+~$prerelease}"

# ── build ──
if $tests; then
    dotnet test "$ROOT/tests/Bobrlog.Core.Tests/Bobrlog.Core.Tests.csproj" -c Release
fi

rm -rf "$STAGE"
mkdir -p "$STAGE" "$OUT"
dotnet publish "$ROOT/src/Bobrlog.App/Bobrlog.App.csproj" \
    -c Release -r linux-x64 --self-contained true -o "$STAGE/app"
find "$STAGE/app" -name '*.pdb' -delete
sed "s|@BIN@|/usr/bin/bobrlog|" "$ROOT/packaging/bobrlog.desktop" > "$STAGE/bobrlog.desktop"
chmod -R go-w "$STAGE"

# ── nfpm (single static binary, builds both formats without dpkg/rpmbuild) ──
nfpm="$(command -v nfpm || true)"
if [[ -z "$nfpm" ]]; then
    nfpm="$TOOLS/nfpm-$NFPM_VERSION/nfpm"
    if [[ ! -x "$nfpm" ]]; then
        archive="$TOOLS/nfpm_${NFPM_VERSION}_Linux_x86_64.tar.gz"
        mkdir -p "$(dirname "$nfpm")"
        curl -fsSL -o "$archive" \
            "https://github.com/goreleaser/nfpm/releases/download/v$NFPM_VERSION/nfpm_${NFPM_VERSION}_Linux_x86_64.tar.gz"
        echo "$NFPM_SHA256  $archive" | sha256sum -c --quiet -
        tar -xzf "$archive" -C "$(dirname "$nfpm")" nfpm
        rm "$archive"
    fi
fi

export BOBRLOG_VERSION="$version" BOBRLOG_PRERELEASE="$prerelease"
for format in "${formats[@]}"; do
    rm -f "$OUT"/bobrlog*."$format"
    "$nfpm" package --config "$ROOT/packaging/nfpm.yaml" --packager "$format" --target "$OUT/"
done
ls -1 "$OUT"
