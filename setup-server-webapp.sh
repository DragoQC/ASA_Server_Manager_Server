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
VERBOSE=0

log_webapp() {
  echo -e "${SECTION_COLOR}[ASA Server Node API]${RESET} $1"
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

download_required_packages_file() {
  local requirements_url="$1"
  local temp_file

  temp_file="$(mktemp)"

  if ! curl -fsSL "${requirements_url}" -o "${temp_file}"; then
    rm -f "${temp_file}"
    log_error "Could not download requirements file: ${requirements_url}"
    exit 1
  fi

  printf '%s\n' "${temp_file}"
}

load_required_packages() {
  local requirements_file="$1"

  if [ ! -f "${requirements_file}" ]; then
    log_error "Requirements file was not found: ${requirements_file}"
    exit 1
  fi

  REQUIRED_PACKAGES=()

  while IFS= read -r line || [ -n "${line}" ]; do
    line="${line%%#*}"
    line="$(printf '%s' "${line}" | xargs)"

    if [ -z "${line}" ]; then
      continue
    fi

    if [[ "${line}" == *"|"* ]]; then
      IFS='|' read -r -a package_options <<< "${line}"
      local selected_package
      selected_package="$(find_first_available_package "${package_options[@]}")" || {
        log_error "Could not find any supported package for: ${line}"
        exit 1
      }
      REQUIRED_PACKAGES+=("${selected_package}")
      continue
    fi

    REQUIRED_PACKAGES+=("${line}")
  done < "${requirements_file}"
}

while (($# > 0)); do
  case "$1" in
    -v|--verbose)
      VERBOSE=1
      shift
      ;;
    *)
      log_error "Unknown argument: $1"
      exit 1
      ;;
  esac
done

run_quiet() {
  if [ "${VERBOSE}" -eq 1 ]; then
    "$@"
  else
    "$@" >/dev/null 2>&1
  fi
}

USER_NAME="${USER_NAME:-asa_web_app}"
GROUP_NAME="${GROUP_NAME:-$USER_NAME}"
BASE_DIR="${BASE_DIR:-/opt/asa}"
WEBAPP_ROOT="${WEBAPP_ROOT:-$BASE_DIR/webapp}"
REPO_DIR="${REPO_DIR:-$WEBAPP_ROOT/src}"
PUBLISH_DIR="${PUBLISH_DIR:-$WEBAPP_ROOT/publish}"
NEXT_PUBLISH_DIR="${NEXT_PUBLISH_DIR:-$WEBAPP_ROOT/publish-next}"
PREVIOUS_PUBLISH_DIR="${PREVIOUS_PUBLISH_DIR:-$WEBAPP_ROOT/publish-prev}"
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
APP_DATA_ROOT="${APP_DATA_ROOT:-$BASE_DIR/data}"
APP_DB_PATH="${APP_DATA_ROOT}/asa-manager.db"
LEGACY_DB_PATH="${PUBLISH_DIR}/Data/asa-manager.db"
SUDOERS_FILE="/etc/sudoers.d/${USER_NAME}-systemctl"
UPDATE_LINK_PATH="${UPDATE_LINK_PATH:-/usr/local/bin/update-asa-server-webapp}"
SHORT_UPDATE_LINK_PATH="${SHORT_UPDATE_LINK_PATH:-/usr/local/bin/update}"
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
SYSTEM_PACKAGES_FILE_URL="${SYSTEM_PACKAGES_FILE_URL:-https://raw.githubusercontent.com/DragoQC/asa_server_node_api/main/requirements/system-packages.txt}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

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

install_update_command() {
  local command_path="$1"

  cat <<EOF > "${command_path}"
#!/usr/bin/env bash
exec "${REPO_DIR}/update-asa-server-webapp.sh" "\$@"
EOF

  chmod 0755 "${command_path}"
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
run_quiet dpkg --add-architecture i386
run_quiet apt update
TEMP_SYSTEM_PACKAGES_FILE="$(download_required_packages_file "${SYSTEM_PACKAGES_FILE_URL}")"
load_required_packages "${TEMP_SYSTEM_PACKAGES_FILE}"
rm -f "${TEMP_SYSTEM_PACKAGES_FILE}"
run_quiet apt install -y "${REQUIRED_PACKAGES[@]}"
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
  "${APP_DATA_ROOT}" \
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

if [ ! -f "${APP_DB_PATH}" ] && [ -f "${LEGACY_DB_PATH}" ]; then
  cp -a "${LEGACY_DB_PATH}" "${APP_DB_PATH}"
  chown "${USER_NAME}:${GROUP_NAME}" "${APP_DB_PATH}"
  log_ok "Migrated existing app DB to ${APP_DB_PATH}."
fi

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
run_quiet visudo -cf "${SUDOERS_FILE}"
log_ok "Granted ${USER_NAME} access to query systemd, read asa and WireGuard logs, manage asa, run the cluster client scripts, prepare backup tools per format, update /etc/fstab through the apply script, and control wg-quick@wg0."

if [ ! -x "${DOTNET_BIN}" ] || ! "${DOTNET_BIN}" --list-sdks 2>/dev/null | grep -q "^${DOTNET_VERSION}\\."; then
  log_dotnet "Installing latest .NET SDK ${DOTNET_VERSION}..."
  TEMP_INSTALL_SCRIPT="$(mktemp)"
  run_quiet curl -fsSL https://dot.net/v1/dotnet-install.sh -o "${TEMP_INSTALL_SCRIPT}"
  run_quiet bash "${TEMP_INSTALL_SCRIPT}" --channel "${DOTNET_VERSION}" --install-dir "${DOTNET_ROOT}"
  rm -f "${TEMP_INSTALL_SCRIPT}"
  run_quiet ln -sf "${DOTNET_ROOT}/dotnet" "${DOTNET_BIN}"
  log_ok "Installed latest .NET SDK ${DOTNET_VERSION}."
else
  log_ok ".NET SDK ${DOTNET_VERSION} already installed."
fi

export DOTNET_ROOT
export DOTNET_CLI_TELEMETRY_OPTOUT
export PATH="/usr/local/bin:${PATH}"

log_git "Fetching repository..."
if [ ! -d "${REPO_DIR}/.git" ]; then
  rm -rf "${REPO_DIR}"
  mkdir -p "$(dirname "${REPO_DIR}")"
  if [ "${VERBOSE}" -eq 1 ]; then
    run_as_app_user env GIT_TERMINAL_PROMPT=0 git clone --branch "${REPO_BRANCH}" "${REPO_URL}" "${REPO_DIR}"
  else
    run_as_app_user env GIT_TERMINAL_PROMPT=0 git clone --quiet --branch "${REPO_BRANCH}" "${REPO_URL}" "${REPO_DIR}" >/dev/null 2>&1
  fi
  log_ok "Cloned ${REPO_URL}."
else
  if [ "${VERBOSE}" -eq 1 ]; then
    run_as_app_user env GIT_TERMINAL_PROMPT=0 git -C "${REPO_DIR}" fetch --all --prune
    run_as_app_user env GIT_TERMINAL_PROMPT=0 git -C "${REPO_DIR}" checkout "${REPO_BRANCH}"
    run_as_app_user env GIT_TERMINAL_PROMPT=0 git -C "${REPO_DIR}" reset --hard "origin/${REPO_BRANCH}"
  else
    run_as_app_user env GIT_TERMINAL_PROMPT=0 git -C "${REPO_DIR}" fetch --all --prune --quiet >/dev/null 2>&1
    run_as_app_user env GIT_TERMINAL_PROMPT=0 git -C "${REPO_DIR}" checkout "${REPO_BRANCH}" >/dev/null 2>&1
    run_as_app_user env GIT_TERMINAL_PROMPT=0 git -C "${REPO_DIR}" reset --hard "origin/${REPO_BRANCH}" >/dev/null 2>&1
  fi
  log_ok "Updated local repository copy."
fi

log_dotnet "Publishing web app..."
run_quiet rm -rf "${PUBLISH_DIR}"
run_quiet mkdir -p "${PUBLISH_DIR}"
chown -R "${USER_NAME}:${GROUP_NAME}" "${WEBAPP_ROOT}"

if [ "${VERBOSE}" -eq 1 ]; then
  run_as_app_user_bash "export DOTNET_ROOT='${DOTNET_ROOT}'; export DOTNET_CLI_TELEMETRY_OPTOUT='${DOTNET_CLI_TELEMETRY_OPTOUT}'; export PATH='${DOTNET_ROOT}:/usr/local/bin:/usr/bin:/bin'; cd '${REPO_DIR}'; '${DOTNET_BIN}' publish '${APP_PROJECT_RELATIVE_PATH}' -c Release -o '${PUBLISH_DIR}'"
else
  run_as_app_user_bash "export DOTNET_ROOT='${DOTNET_ROOT}'; export DOTNET_CLI_TELEMETRY_OPTOUT='${DOTNET_CLI_TELEMETRY_OPTOUT}'; export PATH='${DOTNET_ROOT}:/usr/local/bin:/usr/bin:/bin'; cd '${REPO_DIR}'; '${DOTNET_BIN}' publish '${APP_PROJECT_RELATIVE_PATH}' -c Release -o '${PUBLISH_DIR}' >/dev/null 2>&1"
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

if [ -f "${REPO_DIR}/update-asa-server-webapp.sh" ]; then
  chmod 0755 "${REPO_DIR}/update-asa-server-webapp.sh"
  install_update_command "${UPDATE_LINK_PATH}"
  install_update_command "${SHORT_UPDATE_LINK_PATH}"
  log_ok "Installed ${UPDATE_LINK_PATH} updater command."
  log_ok "Installed ${SHORT_UPDATE_LINK_PATH} updater command."
fi

run_quiet ln -sfn "${GAME_SERVICE_FILE}" "${SYSTEMD_GAME_SERVICE_FILE}"
run_quiet systemctl daemon-reload
run_quiet systemctl enable --now asa-cluster-mount.timer
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
Environment=ASA_SERVER_NODE_DATA_DIR=${APP_DATA_ROOT}
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1
ExecStart=${DOTNET_BIN} ${PUBLISH_DIR}/${APP_DLL_NAME}
Restart=always
RestartSec=5
KillSignal=SIGINT

[Install]
WantedBy=multi-user.target
EOF

run_quiet systemctl daemon-reload
run_quiet systemctl enable --now "${SERVICE_NAME}"
log_ok "Created and started ${SERVICE_NAME}."

MACHINE_IP="$(hostname -I | awk '{print $1}')"
if [ -z "${MACHINE_IP}" ]; then
  MACHINE_IP="127.0.0.1"
fi

log_webapp "Current IPv4 addresses:"
ip -4 -o addr show scope global | awk '{print "  - " $2 ": " $4}'
log_webapp "You can now connect at http://${MACHINE_IP}:8000 and use admin / admin"
if [ "${VERBOSE}" -eq 1 ]; then
  log_webapp "Service status:"
  systemctl status "${SERVICE_NAME}" --no-pager
else
  log_info "Service ${SERVICE_NAME} is active."
fi
