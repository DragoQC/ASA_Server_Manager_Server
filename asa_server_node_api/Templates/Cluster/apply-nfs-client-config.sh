#!/usr/bin/env bash

set -euo pipefail

CONFIG_FILE="/opt/asa/nfs/client.mount.conf"
FSTAB_FILE="/etc/fstab"

if [ "${EUID}" -ne 0 ]; then
  echo "This script must be run as root." >&2
  exit 1
fi

if [ ! -f "${CONFIG_FILE}" ]; then
  echo "NFS client config is missing at ${CONFIG_FILE}." >&2
  exit 1
fi

CONFIG_LINE="$(grep -Ev '^[[:space:]]*($|#)' "${CONFIG_FILE}" | head -n 1 || true)"
if [ -z "${CONFIG_LINE}" ]; then
  echo "NFS client config does not contain a valid fstab line." >&2
  exit 1
fi

MOUNT_PATH="$(printf '%s\n' "${CONFIG_LINE}" | awk '{print $2}')"
if [ -z "${MOUNT_PATH}" ]; then
  echo "NFS client config does not contain a valid mount path." >&2
  exit 1
fi

mkdir -p "${MOUNT_PATH}"

TEMP_FILE="$(mktemp)"
export CONFIG_LINE

awk -v mount_path="${MOUNT_PATH}" '
BEGIN { replaced = 0 }
{
  if ($0 ~ /^[[:space:]]*#/ || $0 ~ /^[[:space:]]*$/) {
    print
    next
  }

  line = $0
  gsub(/^[[:space:]]+/, "", line)
  field_count = split(line, parts, /[[:space:]]+/)
  if (field_count >= 2 && parts[2] == mount_path) {
    if (!replaced) {
      print ENVIRON["CONFIG_LINE"]
      replaced = 1
    }
    next
  }

  print
}
END {
  if (!replaced) {
    print ENVIRON["CONFIG_LINE"]
  }
}
' "${FSTAB_FILE}" > "${TEMP_FILE}"

install -m 0644 "${TEMP_FILE}" "${FSTAB_FILE}"
rm -f "${TEMP_FILE}"

systemctl daemon-reload

echo "Updated /etc/fstab for ${MOUNT_PATH}."
