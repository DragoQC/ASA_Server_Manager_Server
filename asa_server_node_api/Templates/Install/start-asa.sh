#!/usr/bin/env bash

set -e
# Load the runtime variables used by this script from the generated env files.
source /opt/asa/server/asa.env
source /opt/asa/proton/proton.env

BASE_DIR="/opt/asa"
SERVER_FILES_DIR="$BASE_DIR/server"
STEAMCMD_DIR="$BASE_DIR/steam"
PROTON_VERSION="${PROTON_VERSION:-}"
PROTON_DIR="$BASE_DIR/proton/$PROTON_VERSION"

# -----------------------------
# Optional cluster support
# -----------------------------
CLUSTER_ARGS=""
if [ -n "$CLUSTER_ID" ]; then
  mkdir -p "$CLUSTER_DIR"
  CLUSTER_ARGS="-ClusterDirOverride=$CLUSTER_DIR -ClusterId=$CLUSTER_ID"
fi

# -----------------------------
# Optional extra args
# -----------------------------
CONFIG_EXTRA_ARGS=""
if [ -n "$EXTRA_ARGS" ]; then
  CONFIG_EXTRA_ARGS=$EXTRA_ARGS
fi

# -----------------------------
# Proton environment
# -----------------------------
mkdir -p "$SERVER_FILES_DIR/steamapps/compatdata/2430930"
export STEAM_COMPAT_DATA_PATH="$SERVER_FILES_DIR/steamapps/compatdata/2430930"
export STEAM_COMPAT_CLIENT_INSTALL_PATH="$BASE_DIR"

# -----------------------------
# Mods
# -----------------------------
MOD_ARG=""
if [ -n "$MOD_IDS" ]; then
  MOD_ARG="-Mods=$MOD_IDS"
fi

# -----------------------------
# Start server (PID belongs to systemd)
# -----------------------------
exec "$PROTON_DIR/proton" run \
  "$SERVER_FILES_DIR/ShooterGame/Binaries/Win64/ArkAscendedServer.exe" \
  "$MAP_NAME?listen?SessionName=$SERVER_NAME" \
  -WinLiveMaxPlayers=$MAX_PLAYERS \
  -Port=$GAME_PORT \
  $MOD_ARG \
  $CLUSTER_ARGS \
  -QueryPort=$QUERY_PORT \
  -RCONPort=$RCON_PORT \
  -NoSteamClient \
  -NoSteam \
  -NoEOS \
  -nullrhi \
  -nosound \
  -NoSplash \
  -log \
  -server \
  -nosteamclient \
  -game \
  $CONFIG_EXTRA_ARGS
