#!/usr/bin/env bash

set -euo pipefail

CONFIG_FILE="/opt/asa/nfs/client.mount.conf"
WG_INTERFACE="wg0"

if [ "${EUID}" -ne 0 ]; then
  echo "This script must be run as root." >&2
  exit 1
fi

if [ ! -f "${CONFIG_FILE}" ]; then
  echo "NFS client config is missing at ${CONFIG_FILE}. Nothing to mount yet."
  exit 0
fi

CONFIG_LINE="$(grep -Ev '^[[:space:]]*($|#)' "${CONFIG_FILE}" | head -n 1 || true)"
if [ -z "${CONFIG_LINE}" ]; then
  echo "NFS client config does not contain a valid fstab line."
  exit 0
fi

SHARE_PATH="$(printf '%s\n' "${CONFIG_LINE}" | awk '{print $1}')"
MOUNT_PATH="$(printf '%s\n' "${CONFIG_LINE}" | awk '{print $2}')"
REMOTE_HOST="${SHARE_PATH%%:*}"

if [ -z "${MOUNT_PATH}" ] || [ -z "${REMOTE_HOST}" ] || [ "${REMOTE_HOST}" = "${SHARE_PATH}" ]; then
  echo "NFS client config does not contain a valid remote host and mount path."
  exit 1
fi

if mountpoint -q "${MOUNT_PATH}"; then
  echo "${MOUNT_PATH} is already mounted."
  exit 0
fi

if ! ip link show "${WG_INTERFACE}" >/dev/null 2>&1; then
  echo "${WG_INTERFACE} is not present yet. Will retry later."
  exit 0
fi

if ! ip route get "${REMOTE_HOST}" 2>/dev/null | grep -q "dev ${WG_INTERFACE}"; then
  echo "${REMOTE_HOST} is not routed through ${WG_INTERFACE} yet. Will retry later."
  exit 0
fi

mkdir -p "${MOUNT_PATH}"

if ! timeout 15s mount -v "${MOUNT_PATH}"; then
  echo "Mount attempt for ${MOUNT_PATH} failed. Will retry later." >&2
  exit 1
fi

echo "Mounted ${MOUNT_PATH} from ${SHARE_PATH}."
