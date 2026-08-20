#!/usr/bin/env bash
set -e

RESTART=false
for arg in "$@"; do
    if [ "$arg" == "--restart" ]; then
        RESTART=true
    fi
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TARGET_DIR="/run/media/system/Data/Games/Steam/steamapps/common/VRCVideoCacher"
CONTAINER_NAME="arch"
TMP_OUT="${SCRIPT_DIR}/output_steam_linux"

echo "=== Building yt-dlp-stub ==="
distrobox enter "${CONTAINER_NAME}" -- dotnet publish "${SCRIPT_DIR}/yt-dlp-stub/yt-dlp-stub.csproj" -c Release -r win-x64
cp "${SCRIPT_DIR}/yt-dlp-stub/bin/Release/net10.0/win-x64/publish/yt-dlp-stub.exe" "${SCRIPT_DIR}/VRCVideoCacher/"

echo "=== Building VRCVideoCacher for Steam (Linux x64) ==="
rm -rf "${TMP_OUT}"
distrobox enter "${CONTAINER_NAME}" -- dotnet publish "${SCRIPT_DIR}/VRCVideoCacher/VRCVideoCacher.csproj" \
    -c SteamRelease \
    -r linux-x64 \
    -o "${TMP_OUT}" \
    --self-contained true \
    -p:PublishSingleFile=false \
    -p:PublishTrimmed=false

echo "=== Deploying to ${TARGET_DIR} ==="
mkdir -p "${TARGET_DIR}"

# Preserve existing symlinks or user data if present (e.g. CachedAssets, logs)
rsync -av --delete --exclude='CachedAssets' --exclude='logs' "${TMP_OUT}/" "${TARGET_DIR}/"

echo "=== Deployment Complete ==="

if [ "$RESTART" = true ]; then
    echo "=== (Re)starting VRCVideoCacher ==="
    pkill -9 -f VRCVideoCacher 2>/dev/null || true
    sleep 1
    nohup "${TARGET_DIR}/VRCVideoCacher" --no-steam > /dev/null 2>&1 &
    echo "VRCVideoCacher started."

    echo "=== Waiting 5s for logs... ==="
    sleep 5
    LOG_FILE=$(ls -t "${HOME}/.config/VRCVideoCacher/Logs/VRCVideoCacher"*.log 2>/dev/null | head -n 1)
    if [ -n "${LOG_FILE}" ] && [ -f "${LOG_FILE}" ]; then
        echo "=== Last 15 log lines (${LOG_FILE}) ==="
        tail -n 15 "${LOG_FILE}"
    fi
fi
