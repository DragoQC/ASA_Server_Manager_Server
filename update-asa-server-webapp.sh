#!/usr/bin/env bash

export LC_ALL=C.UTF-8
export LANG=C.UTF-8
export LANGUAGE=C.UTF-8

set -euo pipefail

RESET='\033[0m'
SUCCESS_COLOR='\033[38;5;82m'
INFO_COLOR='\033[38;5;250m'
ERROR_COLOR='\033[38;5;196m'
SECTION_COLOR='\033[38;5;141m'
GIT_COLOR='\033[38;5;45m'
DOTNET_COLOR='\033[38;5;39m'
VERBOSE=0
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

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

log_error() {
  echo -e "${ERROR_COLOR}✖ $1${RESET}"
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
APP_DATA_ROOT="${APP_DATA_ROOT:-$BASE_DIR/data}"
APP_DB_PATH="${APP_DATA_ROOT}/asa-manager.db"
LEGACY_DB_PATH="${PUBLISH_DIR}/Data/asa-manager.db"
UPDATE_LINK_PATH="${UPDATE_LINK_PATH:-/usr/local/bin/update-asa-server-webapp}"
SHORT_UPDATE_LINK_PATH="${SHORT_UPDATE_LINK_PATH:-/usr/local/bin/update}"
SYSTEM_PACKAGES_FILE_RELATIVE_PATH="requirements/system-packages.txt"
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

write_service_file() {
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
ExecStart=${DOTNET_BIN} ${PUBLISH_DIR}/${APP_DLL_NAME}
Restart=always
RestartSec=5
KillSignal=SIGINT

[Install]
WantedBy=multi-user.target
EOF
}

log_webapp "asa_server_node_api Updater"

if [ ! -x "${DOTNET_BIN}" ] || ! "${DOTNET_BIN}" --list-sdks 2>/dev/null | grep -q "^${DOTNET_VERSION}\\."; then
  log_error ".NET ${DOTNET_VERSION} SDK is not installed. Run the setup script first."
  exit 1
fi

if ! id -u "${USER_NAME}" >/dev/null 2>&1; then
  log_error "App user ${USER_NAME} does not exist. Run the setup script first."
  exit 1
fi

mkdir -p "${BASE_DIR}" "${WEBAPP_ROOT}" "${APP_DATA_ROOT}"
chown -R "${USER_NAME}:${GROUP_NAME}" "${BASE_DIR}"

if [ ! -f "${APP_DB_PATH}" ] && [ -f "${LEGACY_DB_PATH}" ]; then
  cp -a "${LEGACY_DB_PATH}" "${APP_DB_PATH}"
  chown "${USER_NAME}:${GROUP_NAME}" "${APP_DB_PATH}"
  log_ok "Migrated existing app DB to ${APP_DB_PATH}."
fi

export DOTNET_ROOT
export DOTNET_CLI_TELEMETRY_OPTOUT
export PATH="/usr/local/bin:${PATH}"

log_git "Fetching repository..."
if [ ! -d "${REPO_DIR}/.git" ]; then
  mkdir -p "$(dirname "${REPO_DIR}")"
  if [ "${VERBOSE}" -eq 1 ]; then
    run_as_app_user env GIT_TERMINAL_PROMPT=0 git clone --branch "${REPO_BRANCH}" "${REPO_URL}" "${REPO_DIR}"
  else
    run_as_app_user env GIT_TERMINAL_PROMPT=0 git clone --quiet --branch "${REPO_BRANCH}" "${REPO_URL}" "${REPO_DIR}" >/dev/null 2>&1
  fi
  log_ok "Cloned ${REPO_URL} (${REPO_BRANCH})."
else
  if [ "${VERBOSE}" -eq 1 ]; then
    run_as_app_user env GIT_TERMINAL_PROMPT=0 git -C "${REPO_DIR}" fetch --all --prune
  else
    run_as_app_user env GIT_TERMINAL_PROMPT=0 git -C "${REPO_DIR}" fetch --all --prune --quiet >/dev/null 2>&1
  fi

  if ! run_as_app_user env GIT_TERMINAL_PROMPT=0 git -C "${REPO_DIR}" show-ref --verify --quiet "refs/remotes/origin/${REPO_BRANCH}"; then
    log_error "Remote branch origin/${REPO_BRANCH} was not found."
    exit 1
  fi

  if [ "${VERBOSE}" -eq 1 ]; then
    run_as_app_user env GIT_TERMINAL_PROMPT=0 git -C "${REPO_DIR}" checkout "${REPO_BRANCH}"
    run_as_app_user env GIT_TERMINAL_PROMPT=0 git -C "${REPO_DIR}" reset --hard "origin/${REPO_BRANCH}"
  else
    run_as_app_user env GIT_TERMINAL_PROMPT=0 git -C "${REPO_DIR}" checkout "${REPO_BRANCH}" >/dev/null 2>&1
    run_as_app_user env GIT_TERMINAL_PROMPT=0 git -C "${REPO_DIR}" reset --hard "origin/${REPO_BRANCH}" >/dev/null 2>&1
  fi
  log_ok "Updated local repository copy to origin/${REPO_BRANCH}."
fi

chmod 0755 "${REPO_DIR}/update-asa-server-webapp.sh"
install_update_command "${UPDATE_LINK_PATH}"
install_update_command "${SHORT_UPDATE_LINK_PATH}"
log_ok "Installed ${UPDATE_LINK_PATH} updater command."
log_ok "Installed ${SHORT_UPDATE_LINK_PATH} updater command."

log_webapp "Updating apt requirements..."
run_quiet apt update
load_required_packages "${REPO_DIR}/${SYSTEM_PACKAGES_FILE_RELATIVE_PATH}"
run_quiet apt install -y "${REQUIRED_PACKAGES[@]}"
log_ok "Updated dependencies."

log_dotnet "Publishing web app..."
run_quiet rm -rf "${NEXT_PUBLISH_DIR}"
run_quiet mkdir -p "${NEXT_PUBLISH_DIR}"
chown -R "${USER_NAME}:${GROUP_NAME}" "${WEBAPP_ROOT}"

if [ "${VERBOSE}" -eq 1 ]; then
  run_as_app_user_bash "export DOTNET_ROOT='${DOTNET_ROOT}'; export DOTNET_CLI_TELEMETRY_OPTOUT='${DOTNET_CLI_TELEMETRY_OPTOUT}'; export PATH='${DOTNET_ROOT}:/usr/local/bin:/usr/bin:/bin'; cd '${REPO_DIR}'; '${DOTNET_BIN}' publish '${APP_PROJECT_RELATIVE_PATH}' -c Release -o '${NEXT_PUBLISH_DIR}'"
else
  run_as_app_user_bash "export DOTNET_ROOT='${DOTNET_ROOT}'; export DOTNET_CLI_TELEMETRY_OPTOUT='${DOTNET_CLI_TELEMETRY_OPTOUT}'; export PATH='${DOTNET_ROOT}:/usr/local/bin:/usr/bin:/bin'; cd '${REPO_DIR}'; '${DOTNET_BIN}' publish '${APP_PROJECT_RELATIVE_PATH}' -c Release -o '${NEXT_PUBLISH_DIR}' >/dev/null 2>&1"
fi

write_service_file

if systemctl is-active --quiet "${SERVICE_NAME}"; then
  log_webapp "Stopping ${SERVICE_NAME}..."
  run_quiet systemctl stop "${SERVICE_NAME}"
fi

run_quiet rm -rf "${PREVIOUS_PUBLISH_DIR}"
if [ -d "${PUBLISH_DIR}" ]; then
  run_quiet mv "${PUBLISH_DIR}" "${PREVIOUS_PUBLISH_DIR}"
fi

run_quiet mv "${NEXT_PUBLISH_DIR}" "${PUBLISH_DIR}"
chown -R "${USER_NAME}:${GROUP_NAME}" "${WEBAPP_ROOT}"

run_quiet systemctl daemon-reload
run_quiet systemctl enable "${SERVICE_NAME}"
run_quiet systemctl restart "${SERVICE_NAME}"

run_quiet rm -rf "${PREVIOUS_PUBLISH_DIR}"

log_ok "Updated and restarted ${SERVICE_NAME}."
log_info "Repository branch: ${REPO_BRANCH}"
log_info "Publish path: ${PUBLISH_DIR}"
log_info "App data path: ${APP_DATA_ROOT}"
if [ "${VERBOSE}" -eq 1 ]; then
  log_webapp "Service status:"
  systemctl status "${SERVICE_NAME}" --no-pager
else
  log_info "Service ${SERVICE_NAME} is active."
fi
