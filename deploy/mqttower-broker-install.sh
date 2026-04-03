#!/usr/bin/env bash
# MQTTower Broker — LXC install (runs inside the container).
# Can be sourced by a future dashboard installer when env vars are preset.
# Copyright (c) FinalFactory — MIT
set -euo pipefail

: "${GITHUB_REPO_OWNER:=FinalFactory}"
: "${GITHUB_REPO_NAME:=MQTTower}"
: "${ASSET_NAME:=mqttower-agent-linux-x64.tar.gz}"
: "${INSTALL_DIR:=/opt/mqttower-agent}"
: "${STATE_VERSION_FILE:=${INSTALL_DIR}/.version}"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BOLD='\033[1m'
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

# Skip prompts if all required MQTTOWER_* vars are set (modular / dashboard caller).
all_env_preset() {
  [[ -n "${MQTTOWER_DASHBOARD_URL:-}" && -n "${MQTTOWER_REG_SECRET:-}" && -n "${MQTTOWER_API_KEY:-}" \
    && -n "${MQTTOWER_MQTT_PORT:-}" && -n "${MQTTOWER_AGENT_PORT:-}" ]]
}

collect_config() {
  if all_env_preset; then
    msg_info "Using configuration from environment (non-interactive)."
    return 0
  fi
  msg_info "Interactive configuration (set MQTTOWER_* env vars to skip)."
  prompt_if_empty MQTTOWER_DASHBOARD_URL "Dashboard base URL (e.g. http://192.168.1.10:8080)" ""
  prompt_if_empty MQTTOWER_REG_SECRET "Registration secret (same as MQTTower:RegistrationSecret on dashboard)" ""
  if [[ -z "${MQTTOWER_API_KEY:-}" ]]; then
    MQTTOWER_API_KEY="$(random_key)"
    msg_info "Generated API key: ${MQTTOWER_API_KEY}"
  fi
  : "${MQTTOWER_MQTT_PORT:=1883}"
  : "${MQTTOWER_AGENT_PORT:=5080}"
  read -r -p "MQTT listener port [${MQTTOWER_MQTT_PORT}]: " _mp || true
  [[ -n "${_mp:-}" ]] && MQTTOWER_MQTT_PORT="$_mp"
  read -r -p "Agent HTTP port [${MQTTOWER_AGENT_PORT}]: " _ap || true
  [[ -n "${_ap:-}" ]] && MQTTOWER_AGENT_PORT="$_ap"
}

resolve_public_url() {
  if [[ -z "${MQTTOWER_PUBLIC_AGENT_URL:-}" ]]; then
    local ip
    ip="$(get_ipv4)"
    if [[ -n "$ip" ]]; then
      MQTTOWER_PUBLIC_AGENT_URL="http://${ip}:${MQTTOWER_AGENT_PORT}"
      msg_info "Public Agent URL (registration): ${MQTTOWER_PUBLIC_AGENT_URL}"
    else
      prompt_if_empty MQTTOWER_PUBLIC_AGENT_URL "Public URL for this agent (as dashboard will call it)" "http://127.0.0.1:${MQTTOWER_AGENT_PORT}"
    fi
  fi
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
  apt-get install -y mosquitto mosquitto-clients

  if [[ ! -f /etc/apt/sources.list.d/microsoft-prod.list ]]; then
    msg_info "Adding Microsoft package repository (.NET)..."
    local deb_ver="12"
    curl -fsSL "https://packages.microsoft.com/config/debian/${deb_ver}/packages-microsoft-prod.deb" -o /tmp/packages-microsoft-prod.deb
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

download_agent_release() {
  local tmp
  tmp="$(mktemp)"
  fetch_latest_release_json >"$tmp"
  local tag url
  tag="$(jq -r '.tag_name' "$tmp")"
  url="$(jq -r --arg name "$ASSET_NAME" '.assets[] | select(.name==$name) | .browser_download_url' "$tmp" | head -n1)"
  if [[ -z "$url" || "$url" == "null" ]]; then
    rm -f "$tmp"
    fatal "Release asset '${ASSET_NAME}' not found in latest GitHub release. Tag a release with the release workflow."
  fi
  msg_info "Downloading ${ASSET_NAME} (${tag})..."
  rm -rf "${INSTALL_DIR}"
  mkdir -p "$INSTALL_DIR"
  curl -fSL "$url" -o /tmp/mqttower-agent.tgz
  tar -xzf /tmp/mqttower-agent.tgz -C "$INSTALL_DIR"
  rm -f /tmp/mqttower-agent.tgz
  echo "$tag" >"$STATE_VERSION_FILE"
  rm -f "$tmp"
  msg_ok "MQTTower Agent ${tag} installed to ${INSTALL_DIR}"
}

write_mosquitto_conf() {
  local port="$1"
  mkdir -p /var/lib/mosquitto
  chown mosquitto:mosquitto /var/lib/mosquitto 2>/dev/null || true
  cat >/etc/mosquitto/conf.d/mqttower.conf <<EOF
listener ${port}
allow_anonymous true
persistence true
persistence_location /var/lib/mosquitto/
log_dest file /var/log/mosquitto/mosquitto.log
log_type all
EOF
  touch /var/log/mosquitto/mosquitto.log
  chown mosquitto:mosquitto /var/log/mosquitto/mosquitto.log 2>/dev/null || true
  msg_ok "Mosquitto config: /etc/mosquitto/conf.d/mqttower.conf (listener ${port})"
}

write_agent_env() {
  mkdir -p /etc/mqttower-agent
  umask 077
  cat >/etc/mqttower-agent/environment <<EOF
ASPNETCORE_ENVIRONMENT=Production
MQTTower__MosquittoConfigPath=/etc/mosquitto/conf.d/mqttower.conf
MQTTower__MosquittoLogPath=/var/log/mosquitto/mosquitto.log
Agent__HttpPort=${MQTTOWER_AGENT_PORT}
Agent__HttpsPort=0
Agent__DashboardUrl=${MQTTOWER_DASHBOARD_URL}
Agent__RegistrationToken=${MQTTOWER_REG_SECRET}
Agent__ApiKey=${MQTTOWER_API_KEY}
Agent__AutoRegister=true
Agent__PublicAgentUrl=${MQTTOWER_PUBLIC_AGENT_URL}
Agent__RestartCommand=systemctl reload mosquitto
EOF
  chmod 600 /etc/mqttower-agent/environment
  msg_ok "Wrote /etc/mqttower-agent/environment"
}

write_systemd_unit() {
  cat >/etc/systemd/system/mqttower-agent.service <<'EOF'
[Unit]
Description=MQTTower Agent (Mosquitto sidecar)
After=network-online.target mosquitto.service
Wants=network-online.target
Requires=mosquitto.service

[Service]
Type=simple
EnvironmentFile=/etc/mqttower-agent/environment
WorkingDirectory=/opt/mqttower-agent
ExecStart=/usr/bin/dotnet /opt/mqttower-agent/MQTTower.Agent.dll
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
INSTALL_DIR="/opt/mqttower-agent"
STATE="${INSTALL_DIR}/.version"
ASSET_NAME="mqttower-agent-linux-x64.tar.gz"
GITHUB_REPO_OWNER="${GITHUB_REPO_OWNER:-FinalFactory}"
GITHUB_REPO_NAME="${GITHUB_REPO_NAME:-MQTTower}"
LOG_TAG="mqttower-agent-update"

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
systemctl stop mqttower-agent || true
upd="${INSTALL_DIR}/update.sh"
upd_bak=""
[[ -f "$upd" ]] && cp -a "$upd" /tmp/mqttower-update.sh.bak && upd_bak=1
rm -rf "${INSTALL_DIR}"
mkdir -p "$INSTALL_DIR"
tar -xzf "$tmp" -C "$INSTALL_DIR"
rm -f "$tmp"
[[ -n "$upd_bak" ]] && cp -a /tmp/mqttower-update.sh.bak "$upd" && chmod +x "$upd" && rm -f /tmp/mqttower-update.sh.bak
echo "$tag" >"$STATE"
systemctl start mqttower-agent
log "Updated to ${tag}"
UPDATEEOF
  chmod +x "${INSTALL_DIR}/update.sh"
}

write_update_timer() {
  cat >/etc/systemd/system/mqttower-agent-update.service <<EOF
[Unit]
Description=Check for MQTTower Agent updates (GitHub Releases)
After=network-online.target

[Service]
Type=oneshot
ExecStart=${INSTALL_DIR}/update.sh
EOF
  cat >/etc/systemd/system/mqttower-agent-update.timer <<'EOF'
[Unit]
Description=Daily MQTTower Agent update check

[Timer]
OnCalendar=daily
RandomizedDelaySec=45m
Persistent=true

[Install]
WantedBy=timers.target
EOF
  systemctl daemon-reload
  systemctl enable --now mqttower-agent-update.timer
  msg_ok "Daily update timer enabled."
}

write_motd() {
  local ip
  ip="$(get_ipv4)"
  mkdir -p /etc/profile.d
  cat >/etc/profile.d/99-mqttower-broker.sh <<EOF
# shellcheck shell=bash
[[ -z "\${PS1:-}" ]] && return 0
echo ""
echo "MQTTower Broker — Mosquitto + Agent"
echo "  MQTT: ${ip:-?}:${MQTTOWER_MQTT_PORT}"
echo "  Agent HTTP: http://${ip:-?}:${MQTTOWER_AGENT_PORT}"
echo ""
EOF
  chmod +x /etc/profile.d/99-mqttower-broker.sh
}

main() {
  bootstrap_minimal
  need_cmd curl
  need_cmd jq
  collect_config
  resolve_public_url
  install_packages
  download_agent_release
  write_mosquitto_conf "${MQTTOWER_MQTT_PORT}"
  write_agent_env
  write_systemd_unit
  write_update_script
  write_update_timer
  systemctl enable --now mosquitto
  systemctl enable --now mqttower-agent
  write_motd
  msg_ok "MQTTower Broker install complete."
  echo ""
  echo "MQTT: ${MQTTOWER_MQTT_PORT}  |  Agent: http://$(get_ipv4):${MQTTOWER_AGENT_PORT}"
  echo "Register in dashboard (Brokers) with Public URL: ${MQTTOWER_PUBLIC_AGENT_URL}"
}

main "$@"
