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
  nfs-common

echo "Installed cluster client tools. This node is ready for WireGuard and NFS configuration."
