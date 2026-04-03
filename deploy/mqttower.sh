#!/usr/bin/env bash
# MQTTower — Proxmox VE LXC installer (host-side).
# Usage:
#   ./mqttower.sh                    Interactive: choose Broker, Dashboard, or Full stack
#   MQTTOWER_DEPLOY_MODE=broker|dashboard|fullstack ./mqttower.sh   Non-interactive mode
#   ./mqttower.sh update [CTID]      Update agent and/or web inside CT (auto-detected)
#
# Copyright (c) FinalFactory — MIT
set -euo pipefail

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
BOLD='\033[1m'
CL='\033[0m'

msg_info() { echo -e "${YELLOW}▶ ${1}${CL}"; }
msg_ok() { echo -e "${GREEN}✓ ${1}${CL}"; }
msg_error() { echo -e "${RED}✗ ${1}${CL}" >&2; }
fatal() { msg_error "$1"; exit 1; }

if [[ -n "${BASH_SOURCE[0]:-}" ]]; then
  SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
else
  SCRIPT_DIR="$(pwd)"
fi
DOWNLOAD_BASE="${MQTTOWER_DEPLOY_BASE:-https://raw.githubusercontent.com/FinalFactory/MQTTower/main/deploy}"

BROKER_INSTALL_SH="${SCRIPT_DIR}/mqttower-broker-install.sh"
DASH_INSTALL_SH="${SCRIPT_DIR}/mqttower-dashboard-install.sh"
BROKER_INSTALL_TMP=""
DASH_INSTALL_TMP=""

cleanup_installers() {
  [[ -n "${BROKER_INSTALL_TMP}" ]] && rm -f "${BROKER_INSTALL_TMP}"
  [[ -n "${DASH_INSTALL_TMP}" ]] && rm -f "${DASH_INSTALL_TMP}"
}

ensure_broker_installer() {
  local default="${SCRIPT_DIR}/mqttower-broker-install.sh"
  if [[ -f "$default" ]]; then
    BROKER_INSTALL_SH="$default"
    return 0
  fi
  msg_info "Fetching mqttower-broker-install.sh from GitHub..."
  BROKER_INSTALL_TMP="$(mktemp)"
  curl -fsSL "${DOWNLOAD_BASE}/mqttower-broker-install.sh" -o "$BROKER_INSTALL_TMP"
  chmod +x "$BROKER_INSTALL_TMP"
  BROKER_INSTALL_SH="$BROKER_INSTALL_TMP"
}

ensure_dashboard_installer() {
  local default="${SCRIPT_DIR}/mqttower-dashboard-install.sh"
  if [[ -f "$default" ]]; then
    DASH_INSTALL_SH="$default"
    return 0
  fi
  msg_info "Fetching mqttower-dashboard-install.sh from GitHub..."
  DASH_INSTALL_TMP="$(mktemp)"
  curl -fsSL "${DOWNLOAD_BASE}/mqttower-dashboard-install.sh" -o "$DASH_INSTALL_TMP"
  chmod +x "$DASH_INSTALL_TMP"
  DASH_INSTALL_SH="$DASH_INSTALL_TMP"
}

header_info() {
  echo -e "${CYAN}${BOLD}MQTTower LXC installer${CL}"
  echo -e "${CYAN}Mosquitto + Agent, Dashboard, or both in one container${CL}"
  echo ""
}

require_pve() {
  command -v pveversion >/dev/null 2>&1 || fatal "Run this script on a Proxmox VE host (pveversion not found)."
  command -v pct >/dev/null 2>&1 || fatal "pct not found."
}

next_ctid() {
  if command -v pvesh >/dev/null 2>&1; then
    pvesh get /cluster/nextid --output-format text 2>/dev/null | head -n1 && return 0
  fi
  local max=99
  while read -r line; do
    local id
    id="$(echo "$line" | awk '{print $1}' | tr -d ' ')"
    [[ "$id" =~ ^[0-9]+$ ]] || continue
    ((id > max)) && max="$id"
  done < <(pct list 2>/dev/null || true)
  echo $((max + 1))
}

pick_template() {
  local t="${MQTTOWER_TEMPLATE:-}"
  if [[ -n "$t" ]]; then
    echo "$t"
    return
  fi
  local line
  line="$(pveam list local 2>/dev/null | grep -F 'debian-13-standard' | head -n1)" || true
  if [[ -z "$line" ]]; then
    line="$(pveam list local 2>/dev/null | grep -E 'debian-1[2-9].*standard' | head -n1)" || true
  fi
  if [[ -z "$line" ]]; then
    fatal "No Debian CT template in local storage. Run: pveam update && pveam download local debian-13-standard (or similar)."
  fi
  echo "$line" | awk '{print $1}'
}

default_storage() {
  if [[ -n "${STORAGE:-}" ]]; then
    echo "$STORAGE"
    return
  fi
  local s
  s="$(pvesm status 2>/dev/null | awk 'NR>1 && $1 !~ /^Name$/ {print $1}' | head -n1)"
  [[ -n "$s" ]] && echo "$s" && return
  echo "local-lvm"
}

whiptail_or_read() {
  local title="$1" text="$2" default="$3" out
  if command -v whiptail >/dev/null 2>&1; then
    out="$(whiptail --title "$title" --inputbox "$text" 10 70 "$default" 3>&1 1>&2 2>&3)" || return 1
    echo "$out"
  else
    read -r -p "$text [$default]: " out
    echo "${out:-$default}"
  fi
}

# DEPLOY_MODE: broker | dashboard | fullstack
create_lxc() {
  local adv_mode="${1:-simple}"
  local cpu ram disk hostname bridge storage template ctid

  cpu="${var_cpu:-1}"
  ram="${var_ram:-512}"
  disk="${var_disk:-4}"
  hostname="${var_hostname:-mqttower-broker}"
  bridge="${var_bridge:-vmbr0}"
  storage="$(default_storage)"
  template="$(pick_template)"

  if [[ "$adv_mode" == "advanced" ]]; then
    cpu="$(whiptail_or_read "CPU" "CPU cores" "$cpu")" || exit 1
    ram="$(whiptail_or_read "RAM" "RAM (MB)" "$ram")" || exit 1
    disk="$(whiptail_or_read "Disk" "Disk size (GB)" "$disk")" || exit 1
    hostname="$(whiptail_or_read "Hostname" "Container hostname" "$hostname")" || exit 1
    bridge="$(whiptail_or_read "Bridge" "Network bridge (e.g. vmbr0)" "$bridge")" || exit 1
    storage="$(whiptail_or_read "Storage" "Storage pool for rootfs" "$storage")" || exit 1
  fi

  ctid="$(next_ctid)"
  msg_info "Using CT ID ${ctid}, template ${template}, storage ${storage}, ${cpu} CPU, ${ram}MB RAM, ${disk}GB disk."

  local rootfs="${storage}:${disk}"
  local net0="name=eth0,bridge=${bridge},firewall=1"

  msg_info "Creating LXC ${ctid}..."
  pct create "$ctid" "local:vztmpl/${template}" \
    -hostname "$hostname" \
    -memory "$ram" \
    -cores "$cpu" \
    -net0 "$net0" \
    -rootfs "$rootfs" \
    -unprivileged 1 \
    -features nesting=1 \
    -onboot 1

  msg_ok "Container ${ctid} created."
  CREATED_CTID="$ctid"
}

wait_for_ct_net() {
  local ctid="$1" i
  for i in $(seq 1 30); do
    if pct exec "$ctid" -- hostname -I 2>/dev/null | grep -qE '[0-9]+\.'; then
      return 0
    fi
    sleep 2
  done
  msg_error "Timeout waiting for network inside CT ${ctid}"
  return 1
}

pick_deploy_mode() {
  local m="${MQTTOWER_DEPLOY_MODE:-}"
  m="${m,,}"
  case "$m" in
    broker|dashboard|fullstack)
      DEPLOY_MODE="$m"
      msg_info "Deploy mode: ${DEPLOY_MODE} (from MQTTOWER_DEPLOY_MODE)"
      return 0
      ;;
  esac

  if command -v whiptail >/dev/null 2>&1; then
    local choice
    choice="$(whiptail --title "MQTTower" --radiolist "What to install in the new LXC?" 18 70 3 \
      broker "Mosquitto + Agent (remote dashboard)" ON \
      dashboard "Dashboard only (remote broker)" OFF \
      fullstack "Full stack: Mosquitto + Agent + Dashboard" OFF \
      3>&1 1>&2 2>&3)" || exit 1
    [[ -n "$choice" ]] || fatal "No mode selected."
    DEPLOY_MODE="$choice"
  else
    echo "Select install mode:"
    echo "  1) Broker — Mosquitto + Agent"
    echo "  2) Dashboard — Web UI only"
    echo "  3) Full stack — Broker + Dashboard in one LXC"
    read -r -p "Choice [1-3]: " c
    case "$c" in
      1) DEPLOY_MODE="broker" ;;
      2) DEPLOY_MODE="dashboard" ;;
      3) DEPLOY_MODE="fullstack" ;;
      *) fatal "Invalid choice" ;;
    esac
  fi
}

apply_mode_defaults() {
  case "${DEPLOY_MODE}" in
    broker)
      var_cpu="${var_cpu:-1}"
      var_ram="${var_ram:-512}"
      var_disk="${var_disk:-4}"
      var_hostname="${var_hostname:-mqttower-broker}"
      ;;
    dashboard)
      var_cpu="${var_cpu:-1}"
      var_ram="${var_ram:-1024}"
      var_disk="${var_disk:-4}"
      var_hostname="${var_hostname:-mqttower}"
      ;;
    fullstack)
      var_cpu="${var_cpu:-1}"
      var_ram="${var_ram:-1536}"
      var_disk="${var_disk:-8}"
      var_hostname="${var_hostname:-mqttower}"
      ;;
  esac
}

collect_broker_env() {
  export MQTTOWER_DASHBOARD_URL="${MQTTOWER_DASHBOARD_URL:-}"
  export MQTTOWER_REG_SECRET="${MQTTOWER_REG_SECRET:-}"
  export MQTTOWER_API_KEY="${MQTTOWER_API_KEY:-}"
  export MQTTOWER_MQTT_PORT="${MQTTOWER_MQTT_PORT:-1883}"
  export MQTTOWER_AGENT_PORT="${MQTTOWER_AGENT_PORT:-5080}"
  export MQTTOWER_PUBLIC_AGENT_URL="${MQTTOWER_PUBLIC_AGENT_URL:-}"

  if [[ -z "${MQTTOWER_DASHBOARD_URL}" ]]; then
    MQTTOWER_DASHBOARD_URL="$(whiptail_or_read "Dashboard" "MQTTower dashboard base URL (e.g. http://192.168.1.10:8080)" "")" || exit 1
  fi
  [[ -n "${MQTTOWER_DASHBOARD_URL}" ]] || fatal "Dashboard URL is required."
  if [[ -z "${MQTTOWER_REG_SECRET}" ]]; then
    MQTTOWER_REG_SECRET="$(whiptail_or_read "Secret" "Registration secret (MQTTower:RegistrationSecret)" "")" || exit 1
  fi
  if [[ -z "${MQTTOWER_API_KEY}" ]]; then
    MQTTOWER_API_KEY="$(openssl rand -hex 16 2>/dev/null || head -c 16 /dev/urandom | xxd -p)"
    msg_info "Generated API key: ${MQTTOWER_API_KEY}"
  fi
}

collect_dashboard_env() {
  export MQTTOWER_ADMIN_USER="${MQTTOWER_ADMIN_USER:-admin}"
  export MQTTOWER_ADMIN_PASS="${MQTTOWER_ADMIN_PASS:-}"
  export MQTTOWER_REG_SECRET="${MQTTOWER_REG_SECRET:-}"
  export MQTTOWER_BROKER_HOST="${MQTTOWER_BROKER_HOST:-}"
  export MQTTOWER_BROKER_PORT="${MQTTOWER_BROKER_PORT:-1883}"
  export MQTTOWER_WEB_PORT="${MQTTOWER_WEB_PORT:-8080}"

  if [[ -z "${MQTTOWER_ADMIN_PASS}" ]]; then
    MQTTOWER_ADMIN_PASS="$(openssl rand -hex 16 2>/dev/null || head -c 16 /dev/urandom | xxd -p)"
    msg_info "Generated admin password: ${MQTTOWER_ADMIN_PASS}"
  fi
  if [[ -z "${MQTTOWER_REG_SECRET}" ]]; then
    MQTTOWER_REG_SECRET="$(whiptail_or_read "Secret" "Registration secret (same on dashboard and agents)" "")" || exit 1
  fi
  [[ -n "${MQTTOWER_REG_SECRET}" ]] || fatal "Registration secret is required."
  if [[ "${DEPLOY_MODE}" != "fullstack" ]]; then
    if [[ -z "${MQTTOWER_BROKER_HOST}" ]]; then
      MQTTOWER_BROKER_HOST="$(whiptail_or_read "Broker" "MQTT broker host (IP of broker LXC)" "")" || exit 1
    fi
    [[ -n "${MQTTOWER_BROKER_HOST}" ]] || fatal "Broker host is required."
  else
    MQTTOWER_BROKER_HOST="127.0.0.1"
  fi
}

collect_fullstack_env() {
  export MQTTOWER_MQTT_PORT="${MQTTOWER_MQTT_PORT:-1883}"
  export MQTTOWER_AGENT_PORT="${MQTTOWER_AGENT_PORT:-5080}"
  export MQTTOWER_BROKER_PORT="${MQTTOWER_BROKER_PORT:-1883}"
  export MQTTOWER_ADMIN_USER="${MQTTOWER_ADMIN_USER:-admin}"
  export MQTTOWER_ADMIN_PASS="${MQTTOWER_ADMIN_PASS:-}"
  export MQTTOWER_REG_SECRET="${MQTTOWER_REG_SECRET:-}"

  if [[ -z "${MQTTOWER_ADMIN_PASS}" ]]; then
    MQTTOWER_ADMIN_PASS="$(openssl rand -hex 16 2>/dev/null || head -c 16 /dev/urandom | xxd -p)"
    msg_info "Generated admin password: ${MQTTOWER_ADMIN_PASS}"
  fi
  if [[ -z "${MQTTOWER_REG_SECRET}" ]]; then
    MQTTOWER_REG_SECRET="$(whiptail_or_read "Secret" "Registration secret (dashboard and agents)" "")" || exit 1
  fi
  [[ -n "${MQTTOWER_REG_SECRET}" ]] || fatal "Registration secret is required."

  MQTTOWER_WEB_PORT="$(whiptail_or_read "Web" "Dashboard HTTP port" "${MQTTOWER_WEB_PORT:-8080}")" || exit 1
  export MQTTOWER_WEB_PORT

  MQTTOWER_BROKER_HOST="127.0.0.1"
  export MQTTOWER_BROKER_HOST
  MQTTOWER_DASHBOARD_URL="http://127.0.0.1:${MQTTOWER_WEB_PORT}"
  export MQTTOWER_DASHBOARD_URL
  export MQTTOWER_PUBLIC_AGENT_URL="http://127.0.0.1:${MQTTOWER_AGENT_PORT}"
  if [[ -z "${MQTTOWER_API_KEY:-}" ]]; then
    MQTTOWER_API_KEY="$(openssl rand -hex 16 2>/dev/null || head -c 16 /dev/urandom | xxd -p)"
    msg_info "Generated agent API key: ${MQTTOWER_API_KEY}"
  fi
  export MQTTOWER_API_KEY
}

run_broker_install_in_ct() {
  local ctid="$1"
  [[ -f "$BROKER_INSTALL_SH" ]] || fatal "Missing broker install script"

  msg_info "Pushing broker install script to CT ${ctid}..."
  pct push "$ctid" "$BROKER_INSTALL_SH" /tmp/mqttower-broker-install.sh
  pct exec "$ctid" -- chmod +x /tmp/mqttower-broker-install.sh

  local envf
  envf="$(mktemp)"
  {
    echo "export MQTTOWER_DASHBOARD_URL=$(printf '%q' "$MQTTOWER_DASHBOARD_URL")"
    echo "export MQTTOWER_REG_SECRET=$(printf '%q' "$MQTTOWER_REG_SECRET")"
    echo "export MQTTOWER_API_KEY=$(printf '%q' "$MQTTOWER_API_KEY")"
    echo "export MQTTOWER_MQTT_PORT=$(printf '%q' "$MQTTOWER_MQTT_PORT")"
    echo "export MQTTOWER_AGENT_PORT=$(printf '%q' "$MQTTOWER_AGENT_PORT")"
    [[ -n "${MQTTOWER_PUBLIC_AGENT_URL:-}" ]] && echo "export MQTTOWER_PUBLIC_AGENT_URL=$(printf '%q' "$MQTTOWER_PUBLIC_AGENT_URL")"
  } >"$envf"

  pct push "$ctid" "$envf" /tmp/mqttower-broker.env
  rm -f "$envf"
  pct exec "$ctid" -- chmod 600 /tmp/mqttower-broker.env

  msg_info "Installing Mosquitto + Agent in CT ${ctid}..."
  pct exec "$ctid" -- bash -lc 'set -a; source /tmp/mqttower-broker.env; set +a; bash /tmp/mqttower-broker-install.sh'
}

run_dashboard_install_in_ct() {
  local ctid="$1"
  [[ -f "$DASH_INSTALL_SH" ]] || fatal "Missing dashboard install script"

  msg_info "Pushing dashboard install script to CT ${ctid}..."
  pct push "$ctid" "$DASH_INSTALL_SH" /tmp/mqttower-dashboard-install.sh
  pct exec "$ctid" -- chmod +x /tmp/mqttower-dashboard-install.sh

  local envf
  envf="$(mktemp)"
  {
    echo "export MQTTOWER_ADMIN_USER=$(printf '%q' "$MQTTOWER_ADMIN_USER")"
    echo "export MQTTOWER_ADMIN_PASS=$(printf '%q' "$MQTTOWER_ADMIN_PASS")"
    echo "export MQTTOWER_REG_SECRET=$(printf '%q' "$MQTTOWER_REG_SECRET")"
    echo "export MQTTOWER_BROKER_HOST=$(printf '%q' "$MQTTOWER_BROKER_HOST")"
    echo "export MQTTOWER_BROKER_PORT=$(printf '%q' "$MQTTOWER_BROKER_PORT")"
    echo "export MQTTOWER_WEB_PORT=$(printf '%q' "$MQTTOWER_WEB_PORT")"
  } >"$envf"

  pct push "$ctid" "$envf" /tmp/mqttower-dashboard.env
  rm -f "$envf"
  pct exec "$ctid" -- chmod 600 /tmp/mqttower-dashboard.env

  msg_info "Installing Dashboard in CT ${ctid}..."
  pct exec "$ctid" -- bash -lc 'set -a; source /tmp/mqttower-dashboard.env; set +a; bash /tmp/mqttower-dashboard-install.sh'
}

update_existing_ct() {
  local ctid="${1:-}"
  if [[ -z "$ctid" ]]; then
    echo "Containers (pick CT ID):"
    pct list 2>/dev/null || true
    read -r -p "CT ID: " ctid
  fi
  [[ -n "$ctid" ]] || fatal "CT ID required."
  if ! pct status "$ctid" &>/dev/null; then
    fatal "No container $ctid"
  fi

  local has_agent has_web
  has_agent=0
  has_web=0
  pct exec "$ctid" -- test -f /opt/mqttower-agent/.version 2>/dev/null && has_agent=1 || true
  pct exec "$ctid" -- test -f /opt/mqttower-web/.version 2>/dev/null && has_web=1 || true

  if [[ "$has_agent" -eq 0 && "$has_web" -eq 0 ]]; then
    fatal "No MQTTower install found in CT ${ctid} (missing /opt/mqttower-agent and /opt/mqttower-web)."
  fi

  if [[ "$has_agent" -eq 1 ]]; then
    msg_info "Updating agent in CT ${ctid}..."
    pct exec "$ctid" -- /opt/mqttower-agent/update.sh || fatal "Agent update failed."
  fi
  if [[ "$has_web" -eq 1 ]]; then
    msg_info "Updating dashboard in CT ${ctid}..."
    pct exec "$ctid" -- /opt/mqttower-web/update.sh || fatal "Dashboard update failed."
  fi
  msg_ok "Update finished."
  exit 0
}

main_install() {
  require_pve
  header_info

  pick_deploy_mode
  apply_mode_defaults

  trap cleanup_installers EXIT
  case "${DEPLOY_MODE}" in
    broker)
      ensure_broker_installer
      ;;
    dashboard)
      ensure_dashboard_installer
      ;;
    fullstack)
      ensure_broker_installer
      ensure_dashboard_installer
      ;;
  esac

  local adv_mode="simple"
  if command -v whiptail >/dev/null 2>&1; then
    if whiptail --title "Mode" --yesno "Advanced settings (CPU, RAM, disk, bridge, storage)?" 10 60; then
      adv_mode="advanced"
    fi
  else
    read -r -p "Advanced settings? [y/N]: " a
    [[ "${a,,}" == "y" ]] && adv_mode="advanced"
  fi

  create_lxc "$adv_mode"
  local ctid="${CREATED_CTID:?}"
  pct start "$ctid"
  msg_info "Waiting for network..."
  wait_for_ct_net "$ctid" || true

  local ip
  ip="$(pct exec "$ctid" -- hostname -I 2>/dev/null | awk '{print $1}')"

  case "${DEPLOY_MODE}" in
    broker)
      collect_broker_env
      run_broker_install_in_ct "$ctid"
      echo ""
      echo -e "${BOLD}CT ${ctid}${CL} — MQTT ${MQTTOWER_MQTT_PORT}, Agent http://${ip}:${MQTTOWER_AGENT_PORT}"
      echo "Approve the broker in the dashboard (Brokers) if pending."
      ;;
    dashboard)
      collect_dashboard_env
      run_dashboard_install_in_ct "$ctid"
      echo ""
      echo -e "${BOLD}CT ${ctid}${CL} — Dashboard http://${ip}:${MQTTOWER_WEB_PORT}"
      echo "Login user: ${MQTTOWER_ADMIN_USER}"
      ;;
    fullstack)
      collect_fullstack_env
      run_broker_install_in_ct "$ctid"
      run_dashboard_install_in_ct "$ctid"
      echo ""
      echo -e "${BOLD}CT ${ctid}${CL} — Dashboard http://${ip}:${MQTTOWER_WEB_PORT} — MQTT ${MQTTOWER_MQTT_PORT}, Agent http://${ip}:${MQTTOWER_AGENT_PORT}"
      echo "Login user: ${MQTTOWER_ADMIN_USER}"
      echo "Approve the broker in the dashboard (Brokers) if pending."
      ;;
  esac

  echo "Update later: ${BOLD}./mqttower.sh update ${ctid}${CL}"
  msg_ok "Done."
}

main() {
  if [[ "${1:-}" == "update" || "${1:-}" == "--update" ]]; then
    shift
    require_pve
    update_existing_ct "${1:-}"
  fi
  main_install
}

main "$@"
