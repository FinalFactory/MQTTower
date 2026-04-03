#!/usr/bin/env bash
# Copyright (c) 2021-2026 community-scripts ORG
# Author: FinalFactory
# License: MIT | https://github.com/community-scripts/ProxmoxVE/raw/main/LICENSE
# Source: https://github.com/FinalFactory/MQTTower
#
# MQTTower — Proxmox VE LXC installer (host-side).
# Usage (host):
#   bash mqttower.sh
#   MQTTOWER_DEPLOY_MODE=broker|dashboard|fullstack bash mqttower.sh
#   bash mqttower.sh update [CTID]
#
# When run inside an LXC (no pveversion), runs in-container update (same as /usr/bin/update).

set -Eeuo pipefail

CS_MISC="${CS_MISC:-https://raw.githubusercontent.com/community-scripts/ProxmoxVE/main/misc}"
MQTTOWER_DEPLOY_BASE="${MQTTOWER_DEPLOY_BASE:-https://raw.githubusercontent.com/FinalFactory/MQTTower/main/deploy}"

if [[ -n "${BASH_SOURCE[0]:-}" ]]; then
  SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
else
  SCRIPT_DIR="$(pwd)"
fi

INSTALL_SCRIPT_LOCAL="${SCRIPT_DIR}/../install/mqttower-install.sh"
INSTALL_SCRIPT_TMP=""
INSTALL_SCRIPT_SH=""

cleanup_install_script() {
  [[ -n "${INSTALL_SCRIPT_TMP}" ]] && rm -f "${INSTALL_SCRIPT_TMP}"
}

ensure_install_script() {
  if [[ -f "$INSTALL_SCRIPT_LOCAL" ]]; then
    INSTALL_SCRIPT_SH="$INSTALL_SCRIPT_LOCAL"
    return 0
  fi
  msg_info "Fetching mqttower-install.sh from GitHub..."
  INSTALL_SCRIPT_TMP="$(mktemp)"
  curl -fsSL "${MQTTOWER_DEPLOY_BASE}/install/mqttower-install.sh" -o "$INSTALL_SCRIPT_TMP"
  chmod +x "$INSTALL_SCRIPT_TMP"
  INSTALL_SCRIPT_SH="$INSTALL_SCRIPT_TMP"
}

# --- community-scripts UI ---
if ! source <(curl -fsSL "${CS_MISC}/core.func"); then
  echo "Failed to download core.func" >&2
  exit 115
fi
if ! source <(curl -fsSL "${CS_MISC}/error_handler.func"); then
  echo "Failed to download error_handler.func" >&2
  exit 115
fi
load_functions
catch_errors

mqttower_header() {
  clear
  echo -e "${BOLD}${BL}MQTTower${CL}"
  echo -e "${TAB}${INFO}${YW}LXC installer — Mosquitto + Agent and/or Dashboard${CL}"
  echo ""
}

require_pve() {
  command -v pveversion >/dev/null 2>&1 || {
    msg_error "Run this script on a Proxmox VE host (pveversion not found)."
    exit 104
  }
  command -v pct >/dev/null 2>&1 || {
    msg_error "pct not found."
    exit 104
  }
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
    msg_error "No Debian CT template in local storage. Run: pveam update && pveam download local debian-13-standard (or similar)."
    exit 105
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
    read -r -p "${text} [${default}]: " out
    echo "${out:-$default}"
  fi
}

fatal() {
  msg_error "$1"
  exit 1
}

create_lxc() {
  local adv_mode="${1:-simple}"
  local cpu ram disk hostname bridge storage template ctid

  cpu="${var_cpu:-1}"
  ram="${var_ram:-512}"
  disk="${var_disk:-4}"
  hostname="${var_hostname:-mqttower}"
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
    broker | dashboard | fullstack)
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

rand_hex() {
  openssl rand -hex 16 2>/dev/null || head -c 16 /dev/urandom | xxd -p
}

collect_broker_env() {
  export MQTTOWER_DASHBOARD_URL="${MQTTOWER_DASHBOARD_URL:-}"
  export MQTTOWER_REG_SECRET="${MQTTOWER_REG_SECRET:-}"
  export MQTTOWER_API_KEY="${MQTTOWER_API_KEY:-}"
  export MQTTOWER_MQTT_PORT="${MQTTOWER_MQTT_PORT:-1883}"
  export MQTTOWER_AGENT_PORT="${MQTTOWER_AGENT_PORT:-5080}"
  export MQTTOWER_PUBLIC_AGENT_URL="${MQTTOWER_PUBLIC_AGENT_URL:-}"
  export MQTTOWER_MQTT_USER="${MQTTOWER_MQTT_USER:-mqttower-admin}"
  if [[ -z "${MQTTOWER_MQTT_PASS:-}" ]]; then
    MQTTOWER_MQTT_PASS="$(rand_hex)"
    msg_info "Generated MQTT admin password (DynSec): ${MQTTOWER_MQTT_PASS}"
  fi
  export MQTTOWER_MQTT_PASS

  if [[ -z "${MQTTOWER_DASHBOARD_URL}" ]]; then
    MQTTOWER_DASHBOARD_URL="$(whiptail_or_read "Dashboard" "MQTTower dashboard base URL (e.g. http://192.168.1.10:8080)" "")" || exit 1
  fi
  [[ -n "${MQTTOWER_DASHBOARD_URL}" ]] || fatal "Dashboard URL is required."
  if [[ -z "${MQTTOWER_REG_SECRET}" ]]; then
    MQTTOWER_REG_SECRET="$(whiptail_or_read "Secret" "Registration secret (MQTTower:RegistrationSecret)" "")" || exit 1
  fi
  if [[ -z "${MQTTOWER_API_KEY}" ]]; then
    MQTTOWER_API_KEY="$(rand_hex)"
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
    MQTTOWER_ADMIN_PASS="$(rand_hex)"
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
    export MQTTOWER_MQTT_USER="${MQTTOWER_MQTT_USER:-mqttower-admin}"
    if [[ -z "${MQTTOWER_MQTT_PASS:-}" ]]; then
      MQTTOWER_MQTT_PASS="$(whiptail_or_read "MQTT" "MQTT broker password (DynSec admin; from broker LXC output)" "")" || exit 1
    fi
    [[ -n "${MQTTOWER_MQTT_PASS:-}" ]] || fatal "MQTT broker password is required."
    export MQTTOWER_MQTT_PASS
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
    MQTTOWER_ADMIN_PASS="$(rand_hex)"
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
    MQTTOWER_API_KEY="$(rand_hex)"
    msg_info "Generated agent API key: ${MQTTOWER_API_KEY}"
  fi
  export MQTTOWER_API_KEY

  export MQTTOWER_MQTT_USER="${MQTTOWER_MQTT_USER:-mqttower-admin}"
  if [[ -z "${MQTTOWER_MQTT_PASS:-}" ]]; then
    MQTTOWER_MQTT_PASS="$(rand_hex)"
    msg_info "Generated MQTT admin password (DynSec): ${MQTTOWER_MQTT_PASS}"
  fi
  export MQTTOWER_MQTT_PASS

  export MQTTOWER_LOCAL_AGENT_URL="${MQTTOWER_PUBLIC_AGENT_URL}"
  export MQTTOWER_LOCAL_AGENT_API_KEY="${MQTTOWER_API_KEY}"
}

write_env_and_run_install() {
  local ctid="$1"
  ensure_install_script
  msg_info "Copying install script to CT ${ctid}..."
  pct push "$ctid" "$INSTALL_SCRIPT_SH" /tmp/mqttower-install.sh
  pct exec "$ctid" -- chmod +x /tmp/mqttower-install.sh

  local envf
  envf="$(mktemp)"
  {
    echo "export MQTTOWER_DEPLOY_MODE=$(printf '%q' "$DEPLOY_MODE")"
    echo "export APPLICATION=$(printf '%q' "${APPLICATION:-MQTTower}")"
    echo "export app=$(printf '%q' "${app:-mqttower}")"
    echo "export VERBOSE=$(printf '%q' "${VERBOSE:-no}")"
    echo "export SSH_ROOT=$(printf '%q' "${SSH_ROOT:-no}")"
    echo "export MQTTOWER_CT_URL=$(printf '%q' "${MQTTOWER_CT_URL:-${MQTTOWER_DEPLOY_BASE}/ct/mqttower.sh}")"
    case "${DEPLOY_MODE}" in
      broker)
        echo "export MQTTOWER_DASHBOARD_URL=$(printf '%q' "$MQTTOWER_DASHBOARD_URL")"
        echo "export MQTTOWER_REG_SECRET=$(printf '%q' "$MQTTOWER_REG_SECRET")"
        echo "export MQTTOWER_API_KEY=$(printf '%q' "$MQTTOWER_API_KEY")"
        echo "export MQTTOWER_MQTT_PORT=$(printf '%q' "$MQTTOWER_MQTT_PORT")"
        echo "export MQTTOWER_AGENT_PORT=$(printf '%q' "$MQTTOWER_AGENT_PORT")"
        echo "export MQTTOWER_MQTT_USER=$(printf '%q' "$MQTTOWER_MQTT_USER")"
        echo "export MQTTOWER_MQTT_PASS=$(printf '%q' "$MQTTOWER_MQTT_PASS")"
        [[ -n "${MQTTOWER_PUBLIC_AGENT_URL:-}" ]] && echo "export MQTTOWER_PUBLIC_AGENT_URL=$(printf '%q' "$MQTTOWER_PUBLIC_AGENT_URL")"
        ;;
      dashboard)
        echo "export MQTTOWER_ADMIN_USER=$(printf '%q' "$MQTTOWER_ADMIN_USER")"
        echo "export MQTTOWER_ADMIN_PASS=$(printf '%q' "$MQTTOWER_ADMIN_PASS")"
        echo "export MQTTOWER_REG_SECRET=$(printf '%q' "$MQTTOWER_REG_SECRET")"
        echo "export MQTTOWER_BROKER_HOST=$(printf '%q' "$MQTTOWER_BROKER_HOST")"
        echo "export MQTTOWER_BROKER_PORT=$(printf '%q' "$MQTTOWER_BROKER_PORT")"
        echo "export MQTTOWER_WEB_PORT=$(printf '%q' "$MQTTOWER_WEB_PORT")"
        echo "export MQTTOWER_MQTT_USER=$(printf '%q' "$MQTTOWER_MQTT_USER")"
        echo "export MQTTOWER_MQTT_PASS=$(printf '%q' "$MQTTOWER_MQTT_PASS")"
        ;;
      fullstack)
        echo "export MQTTOWER_ADMIN_USER=$(printf '%q' "$MQTTOWER_ADMIN_USER")"
        echo "export MQTTOWER_ADMIN_PASS=$(printf '%q' "$MQTTOWER_ADMIN_PASS")"
        echo "export MQTTOWER_REG_SECRET=$(printf '%q' "$MQTTOWER_REG_SECRET")"
        echo "export MQTTOWER_BROKER_HOST=$(printf '%q' "$MQTTOWER_BROKER_HOST")"
        echo "export MQTTOWER_BROKER_PORT=$(printf '%q' "$MQTTOWER_BROKER_PORT")"
        echo "export MQTTOWER_WEB_PORT=$(printf '%q' "$MQTTOWER_WEB_PORT")"
        echo "export MQTTOWER_MQTT_PORT=$(printf '%q' "$MQTTOWER_MQTT_PORT")"
        echo "export MQTTOWER_AGENT_PORT=$(printf '%q' "$MQTTOWER_AGENT_PORT")"
        echo "export MQTTOWER_DASHBOARD_URL=$(printf '%q' "$MQTTOWER_DASHBOARD_URL")"
        echo "export MQTTOWER_API_KEY=$(printf '%q' "$MQTTOWER_API_KEY")"
        echo "export MQTTOWER_MQTT_USER=$(printf '%q' "$MQTTOWER_MQTT_USER")"
        echo "export MQTTOWER_MQTT_PASS=$(printf '%q' "$MQTTOWER_MQTT_PASS")"
        echo "export MQTTOWER_PUBLIC_AGENT_URL=$(printf '%q' "$MQTTOWER_PUBLIC_AGENT_URL")"
        echo "export MQTTOWER_LOCAL_AGENT_URL=$(printf '%q' "$MQTTOWER_LOCAL_AGENT_URL")"
        echo "export MQTTOWER_LOCAL_AGENT_API_KEY=$(printf '%q' "$MQTTOWER_LOCAL_AGENT_API_KEY")"
        ;;
    esac
  } >"$envf"

  pct push "$ctid" "$envf" /tmp/mqttower.env
  rm -f "$envf"
  pct exec "$ctid" -- chmod 600 /tmp/mqttower.env

  msg_info "Running install inside CT ${ctid}..."
  pct exec "$ctid" -- bash -lc 'set -a; source /tmp/mqttower.env; set +a; bash /tmp/mqttower-install.sh'
}

# --- In-container update (tools.func + community-scripts patterns) ---
mqttower_update_inside_container() {
  if ! source <(curl -fsSL "${CS_MISC}/tools.func"); then
    msg_error "Failed to download tools.func"
    exit 115
  fi
  export VERBOSE="${VERBOSE:-no}"
  set_std_mode
  color
  catch_errors
  ensure_dependencies jq

  local has_agent=0 has_web=0
  [[ -d /opt/mqttower-agent ]] && has_agent=1
  [[ -d /opt/mqttower-web ]] && has_web=1
  if [[ "$has_agent" -eq 0 && "$has_web" -eq 0 ]]; then
    msg_error "No MQTTower installation found (/opt/mqttower-agent or /opt/mqttower-web)."
    exit 1
  fi

  # Legacy version file migration for check_for_gh_release / fetch_and_deploy
  if [[ -f /opt/mqttower-agent/.version && ! -f "$HOME/.mqttower-agent" ]]; then
    local v
    v="$(tr -d '\n' </opt/mqttower-agent/.version)"
    [[ "$v" =~ ^v[0-9] ]] && v="${v#v}"
    echo "$v" >"$HOME/.mqttower-agent"
  fi
  if [[ -f /opt/mqttower-web/.version && ! -f "$HOME/.mqttower-web" ]]; then
    local v2
    v2="$(tr -d '\n' </opt/mqttower-web/.version)"
    [[ "$v2" =~ ^v[0-9] ]] && v2="${v2#v}"
    echo "$v2" >"$HOME/.mqttower-web"
  fi

  if [[ "$has_agent" -eq 1 ]]; then
    if check_for_gh_release "mqttower-agent" "FinalFactory/MQTTower"; then
      msg_info "Stopping mqttower-agent"
      systemctl stop mqttower-agent
      msg_ok "Stopped mqttower-agent"
      CLEAN_INSTALL=1 fetch_and_deploy_gh_release "mqttower-agent" "FinalFactory/MQTTower" "prebuild" "latest" "/opt/mqttower-agent" "mqttower-agent-linux-x64.tar.gz"
      msg_info "Starting mqttower-agent"
      systemctl start mqttower-agent
      msg_ok "Started mqttower-agent"
    fi
  fi

  if [[ "$has_web" -eq 1 ]]; then
    if check_for_gh_release "mqttower-web" "FinalFactory/MQTTower"; then
      msg_info "Stopping mqttower"
      systemctl stop mqttower
      msg_ok "Stopped mqttower"
      CLEAN_INSTALL=1 fetch_and_deploy_gh_release "mqttower-web" "FinalFactory/MQTTower" "prebuild" "latest" "/opt/mqttower-web" "mqttower-web-linux-x64.tar.gz"
      msg_info "Starting mqttower"
      systemctl start mqttower
      msg_ok "Started mqttower"
    fi
  fi

  msg_ok "Updated successfully!"
  exit 0
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

  local upd
  upd="$(mktemp)"
  cat <<'EOFUPDATE' >"$upd"
#!/usr/bin/env bash
set -Eeuo pipefail
CS_MISC="${CS_MISC:-https://raw.githubusercontent.com/community-scripts/ProxmoxVE/main/misc}"
source <(curl -fsSL "${CS_MISC}/core.func")
source <(curl -fsSL "${CS_MISC}/error_handler.func")
load_functions
catch_errors
source <(curl -fsSL "${CS_MISC}/tools.func")
export VERBOSE="${VERBOSE:-no}"
set_std_mode
color

has_agent=0 has_web=0
[[ -d /opt/mqttower-agent ]] && has_agent=1
[[ -d /opt/mqttower-web ]] && has_web=1
if [[ "$has_agent" -eq 0 && "$has_web" -eq 0 ]]; then
  msg_error "No MQTTower install found in this CT."
  exit 1
fi
ensure_dependencies jq

if [[ -f /opt/mqttower-agent/.version && ! -f "$HOME/.mqttower-agent" ]]; then
  v="$(tr -d '\n' </opt/mqttower-agent/.version)"
  [[ "$v" =~ ^v[0-9] ]] && v="${v#v}"
  echo "$v" >"$HOME/.mqttower-agent"
fi
if [[ -f /opt/mqttower-web/.version && ! -f "$HOME/.mqttower-web" ]]; then
  v="$(tr -d '\n' </opt/mqttower-web/.version)"
  [[ "$v" =~ ^v[0-9] ]] && v="${v#v}"
  echo "$v" >"$HOME/.mqttower-web"
fi

if [[ "$has_agent" -eq 1 ]]; then
  if check_for_gh_release "mqttower-agent" "FinalFactory/MQTTower"; then
    msg_info "Stopping mqttower-agent"
    systemctl stop mqttower-agent
    msg_ok "Stopped mqttower-agent"
    CLEAN_INSTALL=1 fetch_and_deploy_gh_release "mqttower-agent" "FinalFactory/MQTTower" "prebuild" "latest" "/opt/mqttower-agent" "mqttower-agent-linux-x64.tar.gz"
    systemctl start mqttower-agent
    msg_ok "Started mqttower-agent"
  fi
fi
if [[ "$has_web" -eq 1 ]]; then
  if check_for_gh_release "mqttower-web" "FinalFactory/MQTTower"; then
    msg_info "Stopping mqttower"
    systemctl stop mqttower
    msg_ok "Stopped mqttower"
    CLEAN_INSTALL=1 fetch_and_deploy_gh_release "mqttower-web" "FinalFactory/MQTTower" "prebuild" "latest" "/opt/mqttower-web" "mqttower-web-linux-x64.tar.gz"
    systemctl start mqttower
    msg_ok "Started mqttower"
  fi
fi
msg_ok "Update finished."
EOFUPDATE

  msg_info "Updating MQTTower inside CT ${ctid}..."
  pct push "$ctid" "$upd" /tmp/mqttower-update-run.sh
  rm -f "$upd"
  pct exec "$ctid" -- chmod +x /tmp/mqttower-update-run.sh
  pct exec "$ctid" -- bash /tmp/mqttower-update-run.sh
  msg_ok "Update finished."
}

main_install_host() {
  require_pve
  shell_check
  root_check
  pve_check
  arch_check
  ssh_check
  mqttower_header

  pick_deploy_mode
  apply_mode_defaults

  trap cleanup_install_script EXIT

  local adv_mode="simple"
  if command -v whiptail >/dev/null 2>&1; then
    if whiptail --title "MQTTower" --yesno "Advanced settings (CPU, RAM, disk, bridge, storage)?" 10 60; then
      adv_mode="advanced"
    fi
  else
    read -r -p "Advanced settings? [y/N]: " a
    [[ "${a,,}" == "y" ]] && adv_mode="advanced"
  fi

  create_lxc "$adv_mode"
  local ctid="${CREATED_CTID:?}"
  export APPLICATION="${APPLICATION:-MQTTower}"
  export app="${app:-mqttower}"

  pct start "$ctid"
  msg_info "Waiting for network..."
  wait_for_ct_net "$ctid" || true

  case "${DEPLOY_MODE}" in
    broker) collect_broker_env ;;
    dashboard) collect_dashboard_env ;;
    fullstack) collect_fullstack_env ;;
  esac

  write_env_and_run_install "$ctid"

  local ip
  ip="$(pct exec "$ctid" -- hostname -I 2>/dev/null | awk '{print $1}')"

  msg_ok "Completed successfully!"
  echo -e "${CREATING}${GN}MQTTower setup has been successfully initialized!${CL}"

  case "${DEPLOY_MODE}" in
    broker)
      echo -e "${INFO}${YW} CT ${ctid} — MQTT ${MQTTOWER_MQTT_PORT}, Agent http://${ip}:${MQTTOWER_AGENT_PORT}${CL}"
      echo -e "${TAB}${INFO}MQTT DynSec: user ${MQTTOWER_MQTT_USER} — use this password on the dashboard (MQTTower__BrokerPassword).${CL}"
      ;;
    dashboard)
      echo -e "${INFO}${YW} CT ${ctid} — Dashboard ${TAB}${GATEWAY}${BGN}http://${ip}:${MQTTOWER_WEB_PORT}${CL}"
      echo -e "${TAB}${INFO}Login user: ${MQTTOWER_ADMIN_USER}${CL}"
      ;;
    fullstack)
      echo -e "${INFO}${YW} CT ${ctid}${CL}"
      echo -e "${TAB}${GATEWAY}${BGN}http://${ip}:${MQTTOWER_WEB_PORT}${CL} — MQTT ${MQTTOWER_MQTT_PORT}, Agent http://${ip}:${MQTTOWER_AGENT_PORT}"
      echo -e "${TAB}${INFO}Login user: ${MQTTOWER_ADMIN_USER}${CL}"
      echo -e "${TAB}${INFO}Local agent is pre-linked; MQTT uses DynSec user ${MQTTOWER_MQTT_USER}.${CL}"
      ;;
  esac

  echo -e "${TAB}${INFO}Update later (run on Proxmox host):${CL}"
  echo -e "${TAB}bash -c \"\$(curl -fsSL ${MQTTOWER_DEPLOY_BASE}/ct/mqttower.sh)\" update ${ctid}"
}

# --- entry: inside LXC (e.g. /usr/bin/update), run GitHub release update only ---
if ! command -v pveversion &>/dev/null; then
  mqttower_update_inside_container
  exit 0
fi

case "${1:-}" in
  update | --update)
    shift
    require_pve
    shell_check
    root_check
    update_existing_ct "${1:-}"
    exit 0
    ;;
esac

main_install_host
exit 0
