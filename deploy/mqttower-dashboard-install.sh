#!/usr/bin/env bash
# MQTTower Dashboard — LXC install (runs inside the container).
# Does not install Mosquitto; set MQTTOWER_BROKER_HOST to your broker LXC IP.
# Env: MQTTOWER_ADMIN_USER, MQTTOWER_ADMIN_PASS, MQTTOWER_REG_SECRET, MQTTOWER_BROKER_HOST, MQTTOWER_BROKER_PORT, MQTTOWER_WEB_PORT
# Copyright (c) FinalFactory — MIT
set -euo pipefail

: "${GITHUB_REPO_OWNER:=FinalFactory}"
: "${GITHUB_REPO_NAME:=MQTTower}"
: "${ASSET_NAME:=mqttower-web-linux-x64.tar.gz}"
: "${INSTALL_DIR:=/opt/mqttower-web}"
: "${DATA_DIR:=/var/lib/mqttower}"
: "${STATE_VERSION_FILE:=${INSTALL_DIR}/.version}"

YELLOW='\033[1;33m'
GREEN='\033[0;32m'
RED='\033[0;31m'
CL='\033[0m'

msg_info() { echo -e "${YELLOW}▶ ${1}${CL}"; }
msg_ok() { echo -e "${GREEN}✓ ${1}${CL}"; }
msg_error() { echo -e "${RED}✗ ${1}${CL}" >&2; }
fatal() { msg_error "$1"; exit 1; }

need_cmd() { command -v "$1" >/dev/null 2>&1 || fatal "Missing command: $1"; }

get_ipv4() {
  hostname -I 2>/dev/null | tr ' ' '\n' | grep -E '^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$' | head -n1 || true
}

prompt_if_empty() {
  local var="$1" prompt="$2" default="${3:-}"
  if [[ -z "${!var:-}" ]]; then
    if [[ -t 0 ]]; then
      read -r -p "$prompt${default:+ [$default]}: " val || true
      if [[ -z "$val" && -n "$default" ]]; then
        val="$default"
      fi
      printf -v "$var" '%s' "$val"
    else
      fatal "Non-interactive mode: set $var"
    fi
  fi
}

random_key() {
  openssl rand -hex 16 2>/dev/null || head -c 16 /dev/urandom | xxd -p
}

all_env_preset() {
  [[ -n "${MQTTOWER_ADMIN_USER:-}" && -n "${MQTTOWER_ADMIN_PASS:-}" && -n "${MQTTOWER_REG_SECRET:-}" \
    && -n "${MQTTOWER_BROKER_HOST:-}" && -n "${MQTTOWER_BROKER_PORT:-}" && -n "${MQTTOWER_WEB_PORT:-}" ]]
}

collect_config() {
  if all_env_preset; then
    msg_info "Using configuration from environment (non-interactive)."
    return 0
  fi
  msg_info "Interactive configuration (set env vars to skip)."
  : "${MQTTOWER_ADMIN_USER:=admin}"
  prompt_if_empty MQTTOWER_ADMIN_USER "Admin username" "admin"
  if [[ -z "${MQTTOWER_ADMIN_PASS:-}" ]]; then
    MQTTOWER_ADMIN_PASS="$(random_key)"
    msg_info "Generated admin password: ${MQTTOWER_ADMIN_PASS}"
  fi
  prompt_if_empty MQTTOWER_REG_SECRET "Registration secret (MQTTower:RegistrationSecret; agents must share this)" ""
  prompt_if_empty MQTTOWER_BROKER_HOST "MQTT broker host (IP of broker LXC, e.g. 192.168.1.50)" ""
  : "${MQTTOWER_BROKER_PORT:=1883}"
  read -r -p "MQTT broker port [${MQTTOWER_BROKER_PORT}]: " _bp || true
  [[ -n "${_bp:-}" ]] && MQTTOWER_BROKER_PORT="$_bp"
  : "${MQTTOWER_WEB_PORT:=8080}"
  read -r -p "Dashboard HTTP port [${MQTTOWER_WEB_PORT}]: " _wp || true
  [[ -n "${_wp:-}" ]] && MQTTOWER_WEB_PORT="$_wp"
}

bootstrap_minimal() {
  export DEBIAN_FRONTEND=noninteractive
  msg_info "Installing bootstrap packages (curl, jq, openssl)..."
  apt-get update -y
  apt-get install -y curl ca-certificates jq openssl
}

install_packages() {
  export DEBIAN_FRONTEND=noninteractive
  msg_info "Updating OS packages..."
  apt-get update -y
  apt-get upgrade -y
  if [[ ! -f /etc/apt/sources.list.d/microsoft-prod.list ]]; then
    msg_info "Adding Microsoft package repository (.NET)..."
    curl -fsSL "https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb" -o /tmp/packages-microsoft-prod.deb
    dpkg -i /tmp/packages-microsoft-prod.deb
    rm -f /tmp/packages-microsoft-prod.deb
  fi
  apt-get update -y
  apt-get install -y dotnet-runtime-9.0
  msg_ok "Dependencies installed."
}

fetch_latest_release_json() {
  curl -fsSL "https://api.github.com/repos/${GITHUB_REPO_OWNER}/${GITHUB_REPO_NAME}/releases/latest"
}

download_web_release() {
  local tmp
  tmp="$(mktemp)"
  fetch_latest_release_json >"$tmp"
  local tag url
  tag="$(jq -r '.tag_name' "$tmp")"
  url="$(jq -r --arg name "$ASSET_NAME" '.assets[] | select(.name==$name) | .browser_download_url' "$tmp" | head -n1)"
  if [[ -z "$url" || "$url" == "null" ]]; then
    rm -f "$tmp"
    fatal "Release asset '${ASSET_NAME}' not found in latest GitHub release."
  fi
  msg_info "Downloading ${ASSET_NAME} (${tag})..."
  rm -rf "${INSTALL_DIR}"
  mkdir -p "$INSTALL_DIR"
  curl -fSL "$url" -o /tmp/mqttower-web.tgz
  tar -xzf /tmp/mqttower-web.tgz -C "$INSTALL_DIR"
  rm -f /tmp/mqttower-web.tgz
  echo "$tag" >"$STATE_VERSION_FILE"
  rm -f "$tmp"
  msg_ok "MQTTower Web ${tag} installed to ${INSTALL_DIR}"
}

prepare_data_dir() {
  mkdir -p "$DATA_DIR"
  touch "${DATA_DIR}/mosquitto.log"
  if [[ ! -f "${DATA_DIR}/mosquitto.conf" ]]; then
    echo "# Placeholder for default local broker profile. MQTT uses MQTTower__BrokerHost." >"${DATA_DIR}/mosquitto.conf"
  fi
  chmod 755 "$DATA_DIR"
  msg_ok "Data directory: ${DATA_DIR}"
}

write_web_env() {
  mkdir -p /etc/mqttower
  umask 077
  cat >/etc/mqttower/environment <<EOF
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:${MQTTOWER_WEB_PORT}
ConnectionStrings__Default=Data Source=${DATA_DIR}/mqttower.db
MQTTower__DatabasePath=Data Source=${DATA_DIR}/mqttower.db
MQTTower__BrokerHost=${MQTTOWER_BROKER_HOST}
MQTTower__BrokerPort=${MQTTOWER_BROKER_PORT}
MQTTower__MosquittoConfigPath=${DATA_DIR}/mosquitto.conf
MQTTower__MosquittoLogPath=${DATA_DIR}/mosquitto.log
MQTTower__RegistrationSecret=${MQTTOWER_REG_SECRET}
MQTTOWER_ADMIN_USER=${MQTTOWER_ADMIN_USER}
MQTTOWER_ADMIN_PASS=${MQTTOWER_ADMIN_PASS}
EOF
  chmod 600 /etc/mqttower/environment
  msg_ok "Wrote /etc/mqttower/environment"
}

write_systemd_unit() {
  cat >/etc/systemd/system/mqttower.service <<'EOF'
[Unit]
Description=MQTTower Dashboard (Web)
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
EnvironmentFile=/etc/mqttower/environment
WorkingDirectory=/opt/mqttower-web
ExecStart=/usr/bin/dotnet /opt/mqttower-web/MQTTower.Web.dll
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF
}

write_update_script() {
  cat >"${INSTALL_DIR}/update.sh" <<'UPDATEEOF'
#!/usr/bin/env bash
set -euo pipefail
INSTALL_DIR="/opt/mqttower-web"
STATE="${INSTALL_DIR}/.version"
ASSET_NAME="mqttower-web-linux-x64.tar.gz"
GITHUB_REPO_OWNER="${GITHUB_REPO_OWNER:-FinalFactory}"
GITHUB_REPO_NAME="${GITHUB_REPO_NAME:-MQTTower}"
LOG_TAG="mqttower-web-update"

log() { logger -t "$LOG_TAG" "$*" || true; echo "$*"; }

json="$(curl -fsSL "https://api.github.com/repos/${GITHUB_REPO_OWNER}/${GITHUB_REPO_NAME}/releases/latest")" || { log "Failed to fetch releases"; exit 1; }
tag="$(echo "$json" | jq -r '.tag_name')"
url="$(echo "$json" | jq -r --arg name "$ASSET_NAME" '.assets[] | select(.name==$name) | .browser_download_url' | head -n1)"
if [[ -z "$url" || "$url" == "null" ]]; then
  log "No asset ${ASSET_NAME} in ${tag}"
  exit 0
fi
current=""
[[ -f "$STATE" ]] && current="$(cat "$STATE")"
if [[ "$current" == "$tag" ]]; then
  log "Already at ${tag}"
  exit 0
fi
log "Updating ${current:-none} -> ${tag}"
tmp="$(mktemp)"
curl -fSL "$url" -o "$tmp"
systemctl stop mqttower || true
upd="${INSTALL_DIR}/update.sh"
upd_bak=""
[[ -f "$upd" ]] && cp -a "$upd" /tmp/mqttower-web-update.sh.bak && upd_bak=1
rm -rf "${INSTALL_DIR}"
mkdir -p "$INSTALL_DIR"
tar -xzf "$tmp" -C "$INSTALL_DIR"
rm -f "$tmp"
[[ -n "$upd_bak" ]] && cp -a /tmp/mqttower-web-update.sh.bak "$upd" && chmod +x "$upd" && rm -f /tmp/mqttower-web-update.sh.bak
echo "$tag" >"$STATE"
systemctl start mqttower
log "Updated to ${tag}"
UPDATEEOF
  chmod +x "${INSTALL_DIR}/update.sh"
}

write_update_timer() {
  cat >/etc/systemd/system/mqttower-update.service <<EOF
[Unit]
Description=Check for MQTTower Web updates (GitHub Releases)
After=network-online.target

[Service]
Type=oneshot
ExecStart=${INSTALL_DIR}/update.sh
EOF
  cat >/etc/systemd/system/mqttower-update.timer <<'EOF'
[Unit]
Description=Daily MQTTower Web update check

[Timer]
OnCalendar=daily
RandomizedDelaySec=45m
Persistent=true

[Install]
WantedBy=timers.target
EOF
  systemctl daemon-reload
  systemctl enable --now mqttower-update.timer
  msg_ok "Daily update timer enabled."
}

write_motd() {
  local ip
  ip="$(get_ipv4)"
  mkdir -p /etc/profile.d
  cat >/etc/profile.d/99-mqttower-dashboard.sh <<EOF
# shellcheck shell=bash
[[ -z "\${PS1:-}" ]] && return 0
echo ""
echo "MQTTower Dashboard"
echo "  URL: http://${ip:-?}:${MQTTOWER_WEB_PORT}"
echo ""
EOF
  chmod +x /etc/profile.d/99-mqttower-dashboard.sh
}

main() {
  bootstrap_minimal
  need_cmd curl
  need_cmd jq
  collect_config
  install_packages
  download_web_release
  prepare_data_dir
  write_web_env
  write_systemd_unit
  write_update_script
  write_update_timer
  systemctl enable --now mqttower
  write_motd
  msg_ok "MQTTower Dashboard install complete."
  echo ""
  echo "Open http://$(get_ipv4):${MQTTOWER_WEB_PORT} — login: ${MQTTOWER_ADMIN_USER}"
}

main "$@"
