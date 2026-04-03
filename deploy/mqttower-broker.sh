#!/usr/bin/env bash
# MQTTower Broker — Proxmox VE LXC installer (host-side).
# Self-contained (does not use community-scripts build.func; compatible style).
# Usage:
#   ./mqttower-broker.sh              Create a new LXC and install broker
#   ./mqttower-broker.sh update [CTID]  Run GitHub release update inside existing CT
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
INSTALL_SH="${SCRIPT_DIR}/mqttower-broker-install.sh"

ensure_installer() {
  local name="mqttower-broker-install.sh"
  local default="${SCRIPT_DIR}/${name}"
  if [[ -f "$default" ]]; then
    INSTALL_SH="$default"
    return 0
  fi
  msg_info "Fetching ${name} from GitHub..."
  INSTALL_SH="$(mktemp)"
  curl -fsSL "${DOWNLOAD_BASE}/${name}" -o "$INSTALL_SH"
  chmod +x "$INSTALL_SH"
  trap 'rm -f "${INSTALL_SH}"' EXIT
}

header_info() {
  echo -e "${CYAN}${BOLD}MQTTower Broker LXC${CL}"
  echo -e "${CYAN}Mosquitto + Agent (dashboard is separate)${CL}"
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
    fatal "No Debian CT template in local storage. Run: pveam update && pveam download local debian-13-standard_13.0-1_amd64.tar.zst (or similar)."
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

update_existing_ct() {
  local ctid="${1:-}"
  if [[ -z "$ctid" ]]; then
    echo "Running containers (pick CT ID for MQTTower broker):"
    pct list 2>/dev/null || true
    read -r -p "CT ID: " ctid
  fi
  [[ -n "$ctid" ]] || fatal "CT ID required."
  if ! pct status "$ctid" &>/dev/null; then
    fatal "No container $ctid"
  fi
  msg_info "Running update on CT $ctid..."
  pct exec "$ctid" -- /opt/mqttower-agent/update.sh || fatal "Update failed (is MQTTower broker installed in this CT?)"
  msg_ok "Update finished."
  exit 0
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

create_lxc() {
  local mode="${1:-simple}"
  local cpu ram disk hostname bridge storage template ctid

  cpu="${var_cpu:-1}"
  ram="${var_ram:-512}"
  disk="${var_disk:-4}"
  hostname="${var_hostname:-mqttower-broker}"
  bridge="${var_bridge:-vmbr0}"
  storage="$(default_storage)"
  template="$(pick_template)"

  if [[ "$mode" == "advanced" ]]; then
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

collect_mqttower_env() {
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

run_install_inside_ct() {
  local ctid="$1"
  [[ -f "$INSTALL_SH" ]] || fatal "Missing ${INSTALL_SH}"

  msg_info "Pushing install script to CT ${ctid}..."
  pct push "$ctid" "$INSTALL_SH" /tmp/mqttower-broker-install.sh
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

  msg_info "Running install inside CT (apt, Mosquitto, .NET, Agent)..."
  pct exec "$ctid" -- bash -lc 'set -a; source /tmp/mqttower-broker.env; set +a; bash /tmp/mqttower-broker-install.sh'

  local ip
  ip="$(pct exec "$ctid" -- hostname -I 2>/dev/null | awk '{print $1}')"
  msg_ok "Done."
  echo ""
  echo -e "${BOLD}CT ${ctid}${CL} — MQTT ${MQTTOWER_MQTT_PORT}, Agent http://${ip}:${MQTTOWER_AGENT_PORT}"
  echo "Approve the broker in the dashboard (Brokers) if pending."
  echo "Update later: ${BOLD}./mqttower-broker.sh update ${ctid}${CL}"
}

main() {
  require_pve
  header_info

  if [[ "${1:-}" == "update" || "${1:-}" == "--update" ]]; then
    shift
    update_existing_ct "${1:-}"
  fi

  ensure_installer

  local mode="simple"
  if command -v whiptail >/dev/null 2>&1; then
    if whiptail --title "Mode" --yesno "Advanced settings (CPU, RAM, disk, bridge, storage)?" 10 60; then
      mode="advanced"
    fi
  else
    read -r -p "Advanced settings? [y/N]: " a
    [[ "${a,,}" == "y" ]] && mode="advanced"
  fi

  create_lxc "$mode"
  local ctid="${CREATED_CTID:?}"
  pct start "$ctid"
  msg_info "Waiting for network..."
  wait_for_ct_net "$ctid" || true

  collect_mqttower_env
  run_install_inside_ct "$ctid"
}

main "$@"
