#!/usr/bin/env bash

export LC_ALL=C.UTF-8
export LANG=C.UTF-8
export LANGUAGE=C.UTF-8

set -euo pipefail

RESET='\033[0m'
SUCCESS_COLOR='\033[38;5;82m'
INFO_COLOR='\033[38;5;250m'
WARN_COLOR='\033[38;5;220m'
ERROR_COLOR='\033[38;5;196m'
SECTION_COLOR='\033[38;5;141m'
GIT_COLOR='\033[38;5;45m'
DOTNET_COLOR='\033[38;5;39m'

log_webapp() {
  echo -e "${SECTION_COLOR}[WebApp]${RESET} $1"
}

log_git() {
  echo -e "${GIT_COLOR}[Git]${RESET} $1"
}

log_dotnet() {
  echo -e "${DOTNET_COLOR}[Dotnet]${RESET} $1"
}

log_ok() {
  echo -e "${SUCCESS_COLOR}✔ $1${RESET}"
}

log_info() {
  echo -e "${INFO_COLOR}ℹ $1${RESET}"
}

log_warn() {
  echo -e "${WARN_COLOR}⚠ $1${RESET}"
}

log_error() {
  echo -e "${ERROR_COLOR}✖ $1${RESET}"
}

USER_NAME="${USER_NAME:-asa_web_app}"
GROUP_NAME="${GROUP_NAME:-$USER_NAME}"
BASE_DIR="${BASE_DIR:-/opt/asa}"
WEBAPP_ROOT="${WEBAPP_ROOT:-$BASE_DIR/webapp}"
REPO_DIR="${REPO_DIR:-$WEBAPP_ROOT/src}"
PUBLISH_DIR="${PUBLISH_DIR:-$WEBAPP_ROOT/publish}"
SERVICE_NAME="${SERVICE_NAME:-asa-webapp}"
SERVICE_FILE="/etc/systemd/system/${SERVICE_NAME}.service"
REPO_URL="${REPO_URL:-https://github.com/DragoQC/asa_server_node_api.git}"
REPO_BRANCH="${REPO_BRANCH:-main}"
DOTNET_VERSION="${DOTNET_VERSION:-10.0}"
DOTNET_ROOT="${DOTNET_ROOT:-/usr/share/dotnet}"
DOTNET_BIN="${DOTNET_BIN:-/usr/local/bin/dotnet}"
APP_PROJECT_RELATIVE_PATH="asa_server_node_api/asa_server_node_api.csproj"
APP_DLL_NAME="asa_server_node_api.dll"
APP_URL="${APP_URL:-http://0.0.0.0:8000}"
APP_HOME="${APP_HOME:-$BASE_DIR}"
SUDOERS_FILE="/etc/sudoers.d/${USER_NAME}-systemctl"
GAME_SERVICE_TEMPLATE_RELATIVE_PATH="asa_server_node_api/Templates/Install/asa.service"
GAME_SERVICE_DIR="${BASE_DIR}/systemd"
GAME_SERVICE_FILE="${GAME_SERVICE_DIR}/asa.service"
SYSTEMD_GAME_SERVICE_FILE="/etc/systemd/system/asa.service"
VPN_DIR="${BASE_DIR}/vpn"
NFS_DIR="${BASE_DIR}/nfs"
BACKUP_DIR="${BASE_DIR}/backup"
CLUSTER_CLIENT_PREP_SCRIPT_TEMPLATE_RELATIVE_PATH="asa_server_node_api/Templates/Cluster/prepare-cluster-client.sh"
CLUSTER_CLIENT_PREP_SCRIPT_PATH="${NFS_DIR}/prepare-cluster-client.sh"
CLUSTER_CLIENT_APPLY_SCRIPT_TEMPLATE_RELATIVE_PATH="asa_server_node_api/Templates/Cluster/apply-nfs-client-config.sh"
CLUSTER_CLIENT_APPLY_SCRIPT_PATH="${NFS_DIR}/apply-nfs-client-config.sh"
CLUSTER_MOUNT_RETRY_SCRIPT_TEMPLATE_RELATIVE_PATH="asa_server_node_api/Templates/Cluster/mount-cluster-share.sh"
CLUSTER_MOUNT_RETRY_SCRIPT_PATH="${NFS_DIR}/mount-cluster-share.sh"
CLUSTER_MOUNT_RETRY_SERVICE_TEMPLATE_RELATIVE_PATH="asa_server_node_api/Templates/Cluster/asa-cluster-mount.service"
CLUSTER_MOUNT_RETRY_SERVICE_PATH="/etc/systemd/system/asa-cluster-mount.service"
CLUSTER_MOUNT_RETRY_TIMER_TEMPLATE_RELATIVE_PATH="asa_server_node_api/Templates/Cluster/asa-cluster-mount.timer"
CLUSTER_MOUNT_RETRY_TIMER_PATH="/etc/systemd/system/asa-cluster-mount.timer"
ZIP_TOOLS_PREP_SCRIPT_TEMPLATE_RELATIVE_PATH="asa_server_node_api/Templates/Backup/prepare-zip-tools.sh"
ZIP_TOOLS_PREP_SCRIPT_PATH="${BACKUP_DIR}/prepare-zip-tools.sh"
TAR_TOOLS_PREP_SCRIPT_TEMPLATE_RELATIVE_PATH="asa_server_node_api/Templates/Backup/prepare-tar-tools.sh"
TAR_TOOLS_PREP_SCRIPT_PATH="${BACKUP_DIR}/prepare-tar-tools.sh"
WIREGUARD_DIR="/etc/wireguard"
WIREGUARD_CONFIG_LINK_PATH="${WIREGUARD_DIR}/wg0.conf"

if [ "${EUID}" -ne 0 ]; then
  log_error "This script must be run as root."
  exit 1
fi

run_as_app_user() {
  runuser -u "${USER_NAME}" -- "$@"
}

run_as_app_user_bash() {
  runuser -u "${USER_NAME}" -- bash -lc "$1"
}

find_first_available_package() {
  for package_name in "$@"; do
    if apt-cache show "${package_name}" >/dev/null 2>&1; then
      printf '%s\n' "${package_name}"
      return 0
    fi
  done

  return 1
}

log_webapp "asa_server_node_api – Web App Installer"

log_webapp "Installing dependencies..."
dpkg --add-architecture i386
apt update
ICU_PACKAGE="$(find_first_available_package libicu76 libicu72 libicu-dev)" || {
  log_error "Could not find a supported libicu package in apt."
  exit 1
}
apt install -y \
  git \
  curl \
  wget \
  ca-certificates \
  sudo \
  libgssapi-krb5-2 \
  "${ICU_PACKAGE}" \
  libssl3 \
  zlib1g \
  libc6-i386 \
  lib32gcc-s1 \
  lib32stdc++6
log_ok "Installed dependencies."

if ! getent group "${GROUP_NAME}" >/dev/null 2>&1; then
  groupadd --system "${GROUP_NAME}"
fi

if ! id -u "${USER_NAME}" >/dev/null 2>&1; then
  useradd \
    --system \
    --gid "${GROUP_NAME}" \
    --home-dir "${APP_HOME}" \
    --create-home \
    --shell /bin/bash \
    "${USER_NAME}"
  log_ok "Created user ${USER_NAME}."
else
  log_info "User ${USER_NAME} already exists."
fi

mkdir -p \
  "${BASE_DIR}" \
  "${BASE_DIR}/cluster" \
  "${BASE_DIR}/backup" \
  "${BASE_DIR}/backup/imports" \
  "${BASE_DIR}/backup/restore-work" \
  "${BASE_DIR}/proton" \
  "${BASE_DIR}/server" \
  "${BASE_DIR}/steam" \
  "${BASE_DIR}/vpn" \
  "${NFS_DIR}" \
  "${WIREGUARD_DIR}" \
  "${GAME_SERVICE_DIR}" \
  "${WEBAPP_ROOT}" \
  "${PUBLISH_DIR}"

chown -R "${USER_NAME}:${GROUP_NAME}" "${BASE_DIR}"
chmod 0755 "${BASE_DIR}"
log_ok "Prepared ${BASE_DIR}."

cat <<EOF > "${SUDOERS_FILE}"
${USER_NAME} ALL=(root) NOPASSWD: /usr/bin/systemctl daemon-reload
${USER_NAME} ALL=(root) NOPASSWD: /usr/bin/systemctl enable asa
${USER_NAME} ALL=(root) NOPASSWD: /usr/bin/systemctl show asa --property=ActiveState --property=SubState --property=Result --property=UnitFileState --property=ActiveEnterTimestamp
${USER_NAME} ALL=(root) NOPASSWD: /usr/bin/systemctl status asa --no-pager --full
${USER_NAME} ALL=(root) NOPASSWD: /usr/bin/journalctl -u asa -n 80 --no-pager
${USER_NAME} ALL=(root) NOPASSWD: /usr/bin/journalctl -u ${SERVICE_NAME} -n 80 --no-pager
${USER_NAME} ALL=(root) NOPASSWD: /usr/bin/systemctl start asa
${USER_NAME} ALL=(root) NOPASSWD: /usr/bin/systemctl stop asa
${USER_NAME} ALL=(root) NOPASSWD: /usr/bin/systemctl restart asa
${USER_NAME} ALL=(root) NOPASSWD: /usr/bin/systemctl start --no-block asa
${USER_NAME} ALL=(root) NOPASSWD: /usr/bin/systemctl stop --no-block asa
${USER_NAME} ALL=(root) NOPASSWD: /usr/bin/systemctl restart --no-block asa
${USER_NAME} ALL=(root) NOPASSWD: ${CLUSTER_CLIENT_PREP_SCRIPT_PATH}
${USER_NAME} ALL=(root) NOPASSWD: ${CLUSTER_CLIENT_APPLY_SCRIPT_PATH}
${USER_NAME} ALL=(root) NOPASSWD: ${ZIP_TOOLS_PREP_SCRIPT_PATH}
${USER_NAME} ALL=(root) NOPASSWD: ${TAR_TOOLS_PREP_SCRIPT_PATH}
${USER_NAME} ALL=(root) NOPASSWD: /usr/bin/systemctl enable asa-cluster-mount.timer
${USER_NAME} ALL=(root) NOPASSWD: /usr/bin/systemctl start asa-cluster-mount.timer
${USER_NAME} ALL=(root) NOPASSWD: /usr/bin/systemctl restart asa-cluster-mount.timer
${USER_NAME} ALL=(root) NOPASSWD: /usr/bin/systemctl start asa-cluster-mount.service
${USER_NAME} ALL=(root) NOPASSWD: /usr/bin/systemctl enable wg-quick@wg0
${USER_NAME} ALL=(root) NOPASSWD: /usr/bin/systemctl start wg-quick@wg0
${USER_NAME} ALL=(root) NOPASSWD: /usr/bin/systemctl restart wg-quick@wg0
${USER_NAME} ALL=(root) NOPASSWD: /usr/bin/systemctl stop wg-quick@wg0
${USER_NAME} ALL=(root) NOPASSWD: /usr/bin/journalctl -u wg-quick@wg0 -n 80 --no-pager
${USER_NAME} ALL=(root) NOPASSWD: /usr/bin/journalctl -u opt-asa-cluster.mount -n 80 --no-pager
EOF
chmod 0440 "${SUDOERS_FILE}"
visudo -cf "${SUDOERS_FILE}"
log_ok "Granted ${USER_NAME} access to query systemd, read asa and WireGuard logs, manage asa, run the cluster client scripts, prepare backup tools per format, update /etc/fstab through the apply script, and control wg-quick@wg0."

if [ ! -x "${DOTNET_BIN}" ] || ! "${DOTNET_BIN}" --list-sdks 2>/dev/null | grep -q "^${DOTNET_VERSION}\\."; then
  log_dotnet "Installing latest .NET SDK ${DOTNET_VERSION}..."
  TEMP_INSTALL_SCRIPT="$(mktemp)"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "${TEMP_INSTALL_SCRIPT}"
  bash "${TEMP_INSTALL_SCRIPT}" --channel "${DOTNET_VERSION}" --install-dir "${DOTNET_ROOT}"
  rm -f "${TEMP_INSTALL_SCRIPT}"
  ln -sf "${DOTNET_ROOT}/dotnet" "${DOTNET_BIN}"
  log_ok "Installed latest .NET SDK ${DOTNET_VERSION}."
else
  log_ok ".NET SDK ${DOTNET_VERSION} already installed."
fi

export DOTNET_ROOT
export PATH="/usr/local/bin:${PATH}"

log_git "Fetching repository..."
if [ ! -d "${REPO_DIR}/.git" ]; then
  rm -rf "${REPO_DIR}"
  mkdir -p "$(dirname "${REPO_DIR}")"
  run_as_app_user env GIT_TERMINAL_PROMPT=0 git clone --branch "${REPO_BRANCH}" "${REPO_URL}" "${REPO_DIR}"
  log_ok "Cloned ${REPO_URL}."
else
  run_as_app_user env GIT_TERMINAL_PROMPT=0 git -C "${REPO_DIR}" fetch --all --prune
  run_as_app_user env GIT_TERMINAL_PROMPT=0 git -C "${REPO_DIR}" checkout "${REPO_BRANCH}"
  run_as_app_user env GIT_TERMINAL_PROMPT=0 git -C "${REPO_DIR}" reset --hard "origin/${REPO_BRANCH}"
  log_ok "Updated local repository copy."
fi

log_dotnet "Publishing web app..."
rm -rf "${PUBLISH_DIR}"
mkdir -p "${PUBLISH_DIR}"
chown -R "${USER_NAME}:${GROUP_NAME}" "${WEBAPP_ROOT}"

run_as_app_user_bash "export DOTNET_ROOT='${DOTNET_ROOT}'; export PATH='${DOTNET_ROOT}:/usr/local/bin:/usr/bin:/bin'; cd '${REPO_DIR}'; '${DOTNET_BIN}' publish '${APP_PROJECT_RELATIVE_PATH}' -c Release -o '${PUBLISH_DIR}'"

if [ -d "${REPO_DIR}/asa_server_node_api/Data" ]; then
  mkdir -p "${PUBLISH_DIR}/Data"
  cp -a "${REPO_DIR}/asa_server_node_api/Data/." "${PUBLISH_DIR}/Data/"
fi

if [ ! -f "${GAME_SERVICE_FILE}" ] && [ -f "${REPO_DIR}/${GAME_SERVICE_TEMPLATE_RELATIVE_PATH}" ]; then
  cp "${REPO_DIR}/${GAME_SERVICE_TEMPLATE_RELATIVE_PATH}" "${GAME_SERVICE_FILE}"
fi

if [ -f "${REPO_DIR}/${CLUSTER_CLIENT_PREP_SCRIPT_TEMPLATE_RELATIVE_PATH}" ]; then
  cp "${REPO_DIR}/${CLUSTER_CLIENT_PREP_SCRIPT_TEMPLATE_RELATIVE_PATH}" "${CLUSTER_CLIENT_PREP_SCRIPT_PATH}"
  chown root:root "${CLUSTER_CLIENT_PREP_SCRIPT_PATH}"
  chmod 0755 "${NFS_DIR}" "${CLUSTER_CLIENT_PREP_SCRIPT_PATH}"
fi

if [ -f "${REPO_DIR}/${CLUSTER_CLIENT_APPLY_SCRIPT_TEMPLATE_RELATIVE_PATH}" ]; then
  cp "${REPO_DIR}/${CLUSTER_CLIENT_APPLY_SCRIPT_TEMPLATE_RELATIVE_PATH}" "${CLUSTER_CLIENT_APPLY_SCRIPT_PATH}"
  chown root:root "${CLUSTER_CLIENT_APPLY_SCRIPT_PATH}"
  chmod 0755 "${NFS_DIR}" "${CLUSTER_CLIENT_APPLY_SCRIPT_PATH}"
fi

if [ -f "${REPO_DIR}/${CLUSTER_MOUNT_RETRY_SCRIPT_TEMPLATE_RELATIVE_PATH}" ]; then
  cp "${REPO_DIR}/${CLUSTER_MOUNT_RETRY_SCRIPT_TEMPLATE_RELATIVE_PATH}" "${CLUSTER_MOUNT_RETRY_SCRIPT_PATH}"
  chown root:root "${CLUSTER_MOUNT_RETRY_SCRIPT_PATH}"
  chmod 0755 "${NFS_DIR}" "${CLUSTER_MOUNT_RETRY_SCRIPT_PATH}"
fi

if [ -f "${REPO_DIR}/${CLUSTER_MOUNT_RETRY_SERVICE_TEMPLATE_RELATIVE_PATH}" ]; then
  cp "${REPO_DIR}/${CLUSTER_MOUNT_RETRY_SERVICE_TEMPLATE_RELATIVE_PATH}" "${CLUSTER_MOUNT_RETRY_SERVICE_PATH}"
  chown root:root "${CLUSTER_MOUNT_RETRY_SERVICE_PATH}"
  chmod 0644 "${CLUSTER_MOUNT_RETRY_SERVICE_PATH}"
fi

if [ -f "${REPO_DIR}/${CLUSTER_MOUNT_RETRY_TIMER_TEMPLATE_RELATIVE_PATH}" ]; then
  cp "${REPO_DIR}/${CLUSTER_MOUNT_RETRY_TIMER_TEMPLATE_RELATIVE_PATH}" "${CLUSTER_MOUNT_RETRY_TIMER_PATH}"
  chown root:root "${CLUSTER_MOUNT_RETRY_TIMER_PATH}"
  chmod 0644 "${CLUSTER_MOUNT_RETRY_TIMER_PATH}"
fi

if [ -f "${REPO_DIR}/${ZIP_TOOLS_PREP_SCRIPT_TEMPLATE_RELATIVE_PATH}" ]; then
  cp "${REPO_DIR}/${ZIP_TOOLS_PREP_SCRIPT_TEMPLATE_RELATIVE_PATH}" "${ZIP_TOOLS_PREP_SCRIPT_PATH}"
  chown root:root "${ZIP_TOOLS_PREP_SCRIPT_PATH}"
  chmod 0755 "${BACKUP_DIR}" "${ZIP_TOOLS_PREP_SCRIPT_PATH}"
fi

if [ -f "${REPO_DIR}/${TAR_TOOLS_PREP_SCRIPT_TEMPLATE_RELATIVE_PATH}" ]; then
  cp "${REPO_DIR}/${TAR_TOOLS_PREP_SCRIPT_TEMPLATE_RELATIVE_PATH}" "${TAR_TOOLS_PREP_SCRIPT_PATH}"
  chown root:root "${TAR_TOOLS_PREP_SCRIPT_PATH}"
  chmod 0755 "${BACKUP_DIR}" "${TAR_TOOLS_PREP_SCRIPT_PATH}"
fi

chown "${USER_NAME}:${GROUP_NAME}" "${VPN_DIR}" "${NFS_DIR}" "${BACKUP_DIR}"

ln -sfn "${VPN_DIR}/wg0.conf" "${WIREGUARD_CONFIG_LINK_PATH}"

chown -R "${USER_NAME}:${GROUP_NAME}" "${WEBAPP_ROOT}"
chown -R "${USER_NAME}:${GROUP_NAME}" "${GAME_SERVICE_DIR}"
log_ok "Published web app to ${PUBLISH_DIR}."

ln -sfn "${GAME_SERVICE_FILE}" "${SYSTEMD_GAME_SERVICE_FILE}"
systemctl daemon-reload
systemctl enable --now asa-cluster-mount.timer
log_ok "Linked ${SYSTEMD_GAME_SERVICE_FILE} to ${GAME_SERVICE_FILE}."
log_info "The game server service asa.service is prepared only. It is not enabled or started automatically."
# Needs to do that so we can make our user able to run and change it
log_webapp "Creating systemd service..."
cat <<EOF > "${SERVICE_FILE}"
[Unit]
Description=asa_server_node_api Web App
After=network.target

[Service]
Type=simple
User=${USER_NAME}
Group=${GROUP_NAME}
WorkingDirectory=${PUBLISH_DIR}
Environment=DOTNET_ROOT=${DOTNET_ROOT}
Environment=ASPNETCORE_URLS=${APP_URL}
ExecStart=${DOTNET_BIN} ${PUBLISH_DIR}/${APP_DLL_NAME}
Restart=always
RestartSec=5
KillSignal=SIGINT

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable --now "${SERVICE_NAME}"
log_ok "Created and started ${SERVICE_NAME}."

MACHINE_IP="$(hostname -I | awk '{print $1}')"
if [ -z "${MACHINE_IP}" ]; then
  MACHINE_IP="127.0.0.1"
fi

log_webapp "Current IPv4 addresses:"
ip -4 -o addr show scope global | awk '{print "  - " $2 ": " $4}'
log_webapp "You can now connect at http://${MACHINE_IP}:8000 and use admin / admin"
log_webapp "Service status:"
systemctl status "${SERVICE_NAME}" --no-pager
