#!/usr/bin/env bash

set -euo pipefail

if [ "${EUID}" -ne 0 ]; then
  echo "This script must be run as root." >&2
  exit 1
fi

export DEBIAN_FRONTEND=noninteractive

apt-get update
apt-get install -y \
  wireguard \
  wireguard-tools \
  cifs-utils

dpkg --configure -a
apt-get install -f -y

if [ ! -x "/sbin/mount.cifs" ] && [ ! -x "/usr/sbin/mount.cifs" ]; then
  echo "mount.cifs is missing after installing cluster client tools." >&2
  exit 1
fi

echo "Installed cluster client tools. This node is ready for WireGuard and SMB configuration."
