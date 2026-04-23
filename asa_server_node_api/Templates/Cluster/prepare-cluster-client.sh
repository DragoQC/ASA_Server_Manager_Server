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
  rpcbind \
  nfs-common

dpkg --configure -a
apt-get install -f -y

systemctl enable --now rpcbind.service
systemctl reset-failed rpc-statd.service || true
systemctl restart rpc-statd.service

if ! systemctl --quiet is-active rpcbind.service; then
  echo "rpcbind failed to start after installing cluster client tools." >&2
  exit 1
fi

if ! systemctl --quiet is-active rpc-statd.service; then
  echo "rpc-statd failed to start after installing cluster client tools." >&2
  exit 1
fi

echo "Installed cluster client tools. This node is ready for WireGuard and NFS configuration."
