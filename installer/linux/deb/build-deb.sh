#!/usr/bin/env bash
set -euo pipefail

# このスクリプトは publish 済みアプリを Debian パッケージ化する。
# 主用途: CI から variant/arch ごとの .deb を生成すること。

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../../.." && pwd)"
RESOURCES_DIR="${SCRIPT_DIR}/resources"

usage() {
  cat <<EOF
Usage:
  $(basename "$0") \
    --variant <sc|fd> \
    --arch <amd64|arm64> \
    --version <version> \
    --publish-dir <path> \
    [--output-dir <path>]

Example:
  $(basename "$0") \
    --variant sc \
    --arch amd64 \
    --version 1.2.3 \
    --publish-dir "$REPO_ROOT/output/installer/sc/linux-x64/app" \
    --output-dir "$REPO_ROOT/output/installer/linux"
EOF
}

# 入力引数を読み取り、必須値が足りない場合は usage を返す。
VARIANT=""
ARCH=""
VERSION=""
PUBLISH_DIR=""
OUTPUT_DIR="${REPO_ROOT}/output/installer/linux"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --variant)
      VARIANT="${2:-}"
      shift 2
      ;;
    --arch)
      ARCH="${2:-}"
      shift 2
      ;;
    --version)
      VERSION="${2:-}"
      shift 2
      ;;
    --publish-dir)
      PUBLISH_DIR="${2:-}"
      shift 2
      ;;
    --output-dir)
      OUTPUT_DIR="${2:-}"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      exit 1
      ;;
  esac
done

if [[ -z "$VARIANT" || -z "$ARCH" || -z "$VERSION" || -z "$PUBLISH_DIR" ]]; then
  usage
  exit 1
fi

if [[ "$VARIANT" != "sc" && "$VARIANT" != "fd" ]]; then
  echo "--variant must be sc or fd" >&2
  exit 1
fi

if [[ "$ARCH" != "amd64" && "$ARCH" != "arm64" ]]; then
  echo "--arch must be amd64 or arm64" >&2
  exit 1
fi

if [[ ! -d "$PUBLISH_DIR" ]]; then
  echo "Publish directory not found: $PUBLISH_DIR" >&2
  exit 1
fi

if [[ ! -f "$PUBLISH_DIR/RadiCorder" && ! -f "$PUBLISH_DIR/RadiCorder.dll" ]]; then
  echo "RadiCorder binary not found in publish directory: $PUBLISH_DIR" >&2
  exit 1
fi

# パッケージ依存関係は variant で変える。
# - sc: ランタイム同梱前提
# - fd: OS 側の dotnet / aspnetcore runtime 必須
PACKAGE_NAME="radicorder"
PACKAGE_TITLE="RadiCorder (${VARIANT^^})"
DEPENDS="ffmpeg"
if [[ "$VARIANT" == "fd" ]]; then
  DEPENDS="ffmpeg, dotnet-runtime-10.0, aspnetcore-runtime-10.0"
fi

STAGE_DIR="$(mktemp -d)"
trap 'rm -rf "$STAGE_DIR"' EXIT

# Debian パッケージの標準レイアウトを stage に作る。
mkdir -p "$STAGE_DIR/DEBIAN"
mkdir -p "$STAGE_DIR/opt/radicorder"
mkdir -p "$STAGE_DIR/etc/radicorder"
mkdir -p "$STAGE_DIR/etc/default"
mkdir -p "$STAGE_DIR/lib/systemd/system"
mkdir -p "$STAGE_DIR/usr/share/doc/${PACKAGE_NAME}"

cp -a "$PUBLISH_DIR/." "$STAGE_DIR/opt/radicorder/"

# 配布時のライセンス明記のため、ドキュメント類を同梱する。
if [[ -f "$REPO_ROOT/README.MD" ]]; then
  cp "$REPO_ROOT/README.MD" "$STAGE_DIR/usr/share/doc/${PACKAGE_NAME}/README.MD"
fi
if [[ -f "$REPO_ROOT/THIRD_PARTY_NOTICES.md" ]]; then
  cp "$REPO_ROOT/THIRD_PARTY_NOTICES.md" "$STAGE_DIR/usr/share/doc/${PACKAGE_NAME}/THIRD_PARTY_NOTICES.md"
fi
if [[ -d "$REPO_ROOT/THIRD_PARTY_LICENSES" ]]; then
  cp -a "$REPO_ROOT/THIRD_PARTY_LICENSES" "$STAGE_DIR/usr/share/doc/${PACKAGE_NAME}/THIRD_PARTY_LICENSES"
elif [[ -d "$REPO_ROOT/THIRD_PARTY_LICENCES" ]]; then
  cp -a "$REPO_ROOT/THIRD_PARTY_LICENCES" "$STAGE_DIR/usr/share/doc/${PACKAGE_NAME}/THIRD_PARTY_LICENSES"
fi
if [[ -f "$REPO_ROOT/LICENSE" ]]; then
  cp "$REPO_ROOT/LICENSE" "$STAGE_DIR/usr/share/doc/${PACKAGE_NAME}/LICENSE"
elif [[ -f "$REPO_ROOT/LICENSE.MD" ]]; then
  cp "$REPO_ROOT/LICENSE.MD" "$STAGE_DIR/usr/share/doc/${PACKAGE_NAME}/LICENSE.MD"
fi

# 実行形式に応じて systemd が呼ぶ起動ラッパーを生成する。
if [[ -f "$STAGE_DIR/opt/radicorder/RadiCorder" ]]; then
  cat > "$STAGE_DIR/opt/radicorder/start-radicorder.sh" <<'EOF'
#!/bin/sh
set -eu
exec /opt/radicorder/RadiCorder "$@"
EOF
elif [[ -f "$STAGE_DIR/opt/radicorder/RadiCorder.dll" ]]; then
  cat > "$STAGE_DIR/opt/radicorder/start-radicorder.sh" <<'EOF'
#!/bin/sh
set -eu
exec dotnet /opt/radicorder/RadiCorder.dll "$@"
EOF
else
  echo "Neither RadiCorder nor RadiCorder.dll found after staging publish output." >&2
  exit 1
fi

chmod 0755 "$STAGE_DIR/opt/radicorder/start-radicorder.sh"
# maintainer scripts / debconf 定義 / unit ファイルを配置する。
cp "$RESOURCES_DIR/radicorder.service" "$STAGE_DIR/lib/systemd/system/radicorder.service"
cp "$RESOURCES_DIR/debian/templates" "$STAGE_DIR/DEBIAN/templates"
cp "$RESOURCES_DIR/debian/config" "$STAGE_DIR/DEBIAN/config"
cp "$RESOURCES_DIR/debian/postinst" "$STAGE_DIR/DEBIAN/postinst"
cp "$RESOURCES_DIR/debian/prerm" "$STAGE_DIR/DEBIAN/prerm"
cp "$RESOURCES_DIR/debian/postrm" "$STAGE_DIR/DEBIAN/postrm"

chmod 0755 "$STAGE_DIR/DEBIAN/config"
chmod 0755 "$STAGE_DIR/DEBIAN/postinst"
chmod 0755 "$STAGE_DIR/DEBIAN/prerm"
chmod 0755 "$STAGE_DIR/DEBIAN/postrm"

# control ファイル（メタデータと依存関係）を生成する。
cat > "$STAGE_DIR/DEBIAN/control" <<EOF
Package: ${PACKAGE_NAME}
Version: ${VERSION}
Section: web
Priority: optional
Architecture: ${ARCH}
Maintainer: RadiCorder Project
Depends: ${DEPENDS}
Description: ${PACKAGE_TITLE}
 RadiCorder is a personal web app for internet radio recording and playback.
 This package installs RadiCorder with systemd unit and interactive initial settings.
EOF

mkdir -p "$OUTPUT_DIR"
OUTPUT_FILE="${OUTPUT_DIR}/${PACKAGE_NAME}_${VERSION}_${ARCH}-${VARIANT}.deb"

# 最終 .deb を生成する。
dpkg-deb --build --root-owner-group "$STAGE_DIR" "$OUTPUT_FILE"
echo "Created: $OUTPUT_FILE"
