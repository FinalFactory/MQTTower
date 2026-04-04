#!/usr/bin/env bash
source <(curl -fsSL https://raw.githubusercontent.com/community-scripts/ProxmoxVE/main/misc/build.func)
# Copyright (c) 2021-2026 community-scripts ORG
# Author: FinalFactory
# License: MIT | https://github.com/community-scripts/ProxmoxVE/raw/main/LICENSE
# Source: https://github.com/FinalFactory/MQTTower

APP="MQTTower"
var_tags="${var_tags:-mqtt;iot}"
var_cpu="${var_cpu:-1}"
var_ram="${var_ram:-1536}"
var_disk="${var_disk:-8}"
var_os="${var_os:-debian}"
var_version="${var_version:-13}"
var_unprivileged="${var_unprivileged:-1}"

header_info "$APP"
variables
color
catch_errors

MQTTOWER_DEPLOY_BASE="${MQTTOWER_DEPLOY_BASE:-https://raw.githubusercontent.com/FinalFactory/MQTTower/main/deploy}"
MQTTOWER_INSTALL_URL="${MQTTOWER_INSTALL_URL:-${MQTTOWER_DEPLOY_BASE}/install/mqttower-install.sh}"

function update_script() {
  header_info
  check_container_storage
  check_container_resources

  if [[ ! -d /opt/mqttower-agent && ! -d /opt/mqttower-web ]]; then
    msg_error "No ${APP} Installation Found!"
    exit
  fi

  if [[ -d /opt/mqttower-agent ]]; then
    if check_for_gh_release "mqttower-agent" "FinalFactory/MQTTower"; then
      msg_info "Stopping mqttower-agent"
      systemctl stop mqttower-agent
      msg_ok "Stopped mqttower-agent"

      CLEAN_INSTALL=1 fetch_and_deploy_gh_release "mqttower-agent" \
        "FinalFactory/MQTTower" "prebuild" "latest" \
        "/opt/mqttower-agent" "mqttower-agent-linux-x64.tar.gz"

      msg_info "Starting mqttower-agent"
      systemctl start mqttower-agent
      msg_ok "Started mqttower-agent"
    fi
  fi

  if [[ -d /opt/mqttower-web ]]; then
    if check_for_gh_release "mqttower-web" "FinalFactory/MQTTower"; then
      msg_info "Stopping mqttower"
      systemctl stop mqttower
      msg_ok "Stopped mqttower"

      CLEAN_INSTALL=1 fetch_and_deploy_gh_release "mqttower-web" \
        "FinalFactory/MQTTower" "prebuild" "latest" \
        "/opt/mqttower-web" "mqttower-web-linux-x64.tar.gz"

      msg_info "Starting mqttower"
      systemctl start mqttower
      msg_ok "Started mqttower"
    fi
  fi

  msg_ok "Updated successfully!"
  exit
}

start

# =============================================================================
# Custom questions (after standard wizard, before build)
# =============================================================================

rand_hex() {
  openssl rand -hex 16 2>/dev/null || head -c 16 /dev/urandom | xxd -p
}

# msg_info() starts a background spinner on stderr; whiptail also draws on stderr.
# Without stopping the spinner and clearing the line, dialogs render corrupted.
mqttower_whiptail() {
  stop_spinner
  clear_line
  whiptail "$@" 3>&1 1>&2 2>&3
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

  DEPLOY_MODE="$(mqttower_whiptail --title "${APP}" --radiolist \
    "What to install in the new LXC?" 18 70 3 \
    broker    "Mosquitto + Agent (remote dashboard)" ON \
    dashboard "Dashboard only (remote broker)"       OFF \
    fullstack "Full stack: Mosquitto + Agent + Dashboard" OFF \
    )" || exit 1
  [[ -n "$DEPLOY_MODE" ]] || { msg_error "No mode selected."; exit 1; }
}

collect_mqttower_env() {
  case "${DEPLOY_MODE}" in
    broker)
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
        MQTTOWER_DASHBOARD_URL="$(mqttower_whiptail --title "Dashboard URL" --inputbox \
          "MQTTower dashboard base URL (e.g. http://192.168.1.10:8080)" 10 70 "" \
          )" || exit 1
      fi
      [[ -n "${MQTTOWER_DASHBOARD_URL}" ]] || { msg_error "Dashboard URL is required."; exit 1; }
      if [[ -z "${MQTTOWER_REG_SECRET}" ]]; then
        MQTTOWER_REG_SECRET="$(mqttower_whiptail --title "Registration Secret" --inputbox \
          "Registration secret (MQTTower:RegistrationSecret)" 10 70 "" \
          )" || exit 1
      fi
      if [[ -z "${MQTTOWER_API_KEY}" ]]; then
        MQTTOWER_API_KEY="$(rand_hex)"
        msg_info "Generated API key: ${MQTTOWER_API_KEY}"
      fi
      ;;
    dashboard)
      export MQTTOWER_ADMIN_USER="${MQTTOWER_ADMIN_USER:-admin}"
      export MQTTOWER_ADMIN_PASS="${MQTTOWER_ADMIN_PASS:-}"
      export MQTTOWER_REG_SECRET="${MQTTOWER_REG_SECRET:-}"
      export MQTTOWER_BROKER_HOST="${MQTTOWER_BROKER_HOST:-}"
      export MQTTOWER_BROKER_PORT="${MQTTOWER_BROKER_PORT:-1883}"
      export MQTTOWER_WEB_PORT="${MQTTOWER_WEB_PORT:-8080}"
      export MQTTOWER_MQTT_USER="${MQTTOWER_MQTT_USER:-mqttower-admin}"
      if [[ -z "${MQTTOWER_ADMIN_PASS}" ]]; then
        MQTTOWER_ADMIN_PASS="$(rand_hex)"
        msg_info "Generated admin password: ${MQTTOWER_ADMIN_PASS}"
      fi
      export MQTTOWER_ADMIN_PASS
      if [[ -z "${MQTTOWER_REG_SECRET}" ]]; then
        MQTTOWER_REG_SECRET="$(mqttower_whiptail --title "Registration Secret" --inputbox \
          "Registration secret (same on dashboard and agents)" 10 70 "" \
          )" || exit 1
      fi
      [[ -n "${MQTTOWER_REG_SECRET}" ]] || { msg_error "Registration secret is required."; exit 1; }
      if [[ -z "${MQTTOWER_BROKER_HOST}" ]]; then
        MQTTOWER_BROKER_HOST="$(mqttower_whiptail --title "Broker Host" --inputbox \
          "MQTT broker host (IP of broker LXC)" 10 70 "" \
          )" || exit 1
      fi
      [[ -n "${MQTTOWER_BROKER_HOST}" ]] || { msg_error "Broker host is required."; exit 1; }
      if [[ -z "${MQTTOWER_MQTT_PASS:-}" ]]; then
        MQTTOWER_MQTT_PASS="$(whiptail --title "MQTT Password" --inputbox \
          "MQTT broker password (DynSec admin; from broker LXC output)" 10 70 "" \
          3>&1 1>&2 2>&3)" || exit 1
      fi
      [[ -n "${MQTTOWER_MQTT_PASS:-}" ]] || { msg_error "MQTT broker password is required."; exit 1; }
      export MQTTOWER_MQTT_PASS
      ;;
    fullstack)
      export MQTTOWER_MQTT_PORT="${MQTTOWER_MQTT_PORT:-1883}"
      export MQTTOWER_AGENT_PORT="${MQTTOWER_AGENT_PORT:-5080}"
      export MQTTOWER_BROKER_PORT="${MQTTOWER_BROKER_PORT:-1883}"
      export MQTTOWER_ADMIN_USER="${MQTTOWER_ADMIN_USER:-admin}"
      export MQTTOWER_ADMIN_PASS="${MQTTOWER_ADMIN_PASS:-}"
      export MQTTOWER_REG_SECRET="${MQTTOWER_REG_SECRET:-}"
      export MQTTOWER_WEB_PORT="${MQTTOWER_WEB_PORT:-8080}"
      if [[ -z "${MQTTOWER_ADMIN_PASS}" ]]; then
        MQTTOWER_ADMIN_PASS="$(rand_hex)"
        msg_info "Generated admin password: ${MQTTOWER_ADMIN_PASS}"
      fi
      export MQTTOWER_ADMIN_PASS
      if [[ -z "${MQTTOWER_REG_SECRET}" ]]; then
        MQTTOWER_REG_SECRET="$(mqttower_whiptail --title "Registration Secret" --inputbox \
          "Registration secret (dashboard and agents)" 10 70 "" \
          )" || exit 1
      fi
      [[ -n "${MQTTOWER_REG_SECRET}" ]] || { msg_error "Registration secret is required."; exit 1; }
      MQTTOWER_BROKER_HOST="127.0.0.1"
      export MQTTOWER_BROKER_HOST
      MQTTOWER_DASHBOARD_URL="http://127.0.0.1:${MQTTOWER_WEB_PORT}"
      export MQTTOWER_DASHBOARD_URL
      MQTTOWER_PUBLIC_AGENT_URL="http://127.0.0.1:${MQTTOWER_AGENT_PORT}"
      export MQTTOWER_PUBLIC_AGENT_URL
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
      ;;
  esac
}

# =============================================================================
# Custom build_container — identical to framework except install script URL.
# build_container() hardcodes the install URL to community-scripts/ProxmoxVE.
# Once MQTTower is accepted upstream, replace this with a plain build_container call.
# =============================================================================
mqttower_build_container() {
  # --- Network string (same logic as build.func lines 3536-3585) ---
  NET_STRING="-net0 name=eth0,bridge=${BRG:-vmbr0}"
  [[ -n "${MAC:-}" ]] && case "$MAC" in ,hwaddr=*) NET_STRING+="$MAC" ;; *) NET_STRING+=",hwaddr=$MAC" ;; esac
  NET_STRING+=",ip=${NET:-dhcp}"
  [[ -n "${GATE:-}" ]] && case "$GATE" in ,gw=) ;; ,gw=*) NET_STRING+="$GATE" ;; *) NET_STRING+=",gw=$GATE" ;; esac
  [[ -n "${VLAN:-}" ]] && case "$VLAN" in ,tag=*) NET_STRING+="$VLAN" ;; *) NET_STRING+=",tag=$VLAN" ;; esac
  [[ -n "${MTU:-}" ]]  && case "$MTU" in ,mtu=*) NET_STRING+="$MTU" ;; *) NET_STRING+=",mtu=$MTU" ;; esac
  case "${IPV6_METHOD:-none}" in
    auto)   NET_STRING+=",ip6=auto" ;;
    dhcp)   NET_STRING+=",ip6=dhcp" ;;
    static) [[ -n "${IPV6_ADDR:-}" ]] && { NET_STRING+=",ip6=$IPV6_ADDR"; [[ -n "${IPV6_GATE:-}" ]] && NET_STRING+=",gw6=$IPV6_GATE"; } ;;
  esac

  # --- Features ---
  FEATURES="nesting=1"
  [[ "$CT_TYPE" == "1" ]] && FEATURES="${FEATURES},keyctl=1"

  # --- Download install.func for the container ---
  export FUNCTIONS_FILE_PATH
  FUNCTIONS_FILE_PATH="$(curl -fsSL https://raw.githubusercontent.com/community-scripts/ProxmoxVE/main/misc/install.func)"
  if [[ -z "$FUNCTIONS_FILE_PATH" || ${#FUNCTIONS_FILE_PATH} -lt 100 ]]; then
    msg_error "Failed to download install.func"
    exit 115
  fi

  # --- Standard exports (same as build.func build_container) ---
  export DIAGNOSTICS="${DIAGNOSTICS:-no}"
  export RANDOM_UUID="${RANDOM_UUID:-$(cat /proc/sys/kernel/random/uuid)}"
  export EXECUTION_ID="${EXECUTION_ID:-$RANDOM_UUID}"
  export SESSION_ID="${SESSION_ID:-${RANDOM_UUID:0:8}}"
  export CACHER="${APT_CACHER:-}"
  export CACHER_IP="${APT_CACHER_IP:-}"
  export tz="${timezone:-$(timedatectl show --value --property=Timezone 2>/dev/null || echo UTC)}"
  export APPLICATION="$APP"
  export app="$NSAPP"
  export PASSWORD="${PW:-}"
  export VERBOSE="${VERBOSE:-no}"
  export SSH_ROOT="${SSH:-no}"
  export SSH_AUTHORIZED_KEY="${SSH_AUTHORIZED_KEY:-}"
  export CTID="${CT_ID:-}"
  export CTTYPE="${CT_TYPE:-1}"
  export PCT_OSTYPE="$var_os"
  export PCT_OSVERSION="$var_version"
  export PCT_DISK_SIZE="${DISK_SIZE:-$var_disk}"
  export IPV6_METHOD="${IPV6_METHOD:-none}"

  BUILD_LOG="${BUILD_LOG:-/tmp/create-lxc-${SESSION_ID}.log}"
  export BUILD_LOG
  export INSTALL_LOG="/root/.install-${SESSION_ID}.log"

  # --- MQTTower-specific exports ---
  export MQTTOWER_DEPLOY_MODE="$DEPLOY_MODE"
  export MQTTOWER_CT_URL="${MQTTOWER_CT_URL:-${MQTTOWER_DEPLOY_BASE}/ct/mqttower.sh}"

  # --- Build PCT_OPTIONS string ---
  PCT_OPTIONS_STRING="  -hostname ${HN:-mqttower}"
  [[ -n "${TAGS:-}" ]] && PCT_OPTIONS_STRING+=$'\n'"  -tags $TAGS"
  [[ -n "$FEATURES" ]] && PCT_OPTIONS_STRING="  -features $FEATURES"$'\n'"$PCT_OPTIONS_STRING"
  [[ -n "${SD:-}" ]] && PCT_OPTIONS_STRING+=$'\n'"  $SD"
  [[ -n "${NS:-}" ]] && PCT_OPTIONS_STRING+=$'\n'"  $NS"
  PCT_OPTIONS_STRING+=$'\n'"  $NET_STRING"
  PCT_OPTIONS_STRING+=$'\n'"  -onboot 1"
  PCT_OPTIONS_STRING+=$'\n'"  -cores ${CORE_COUNT:-$var_cpu}"
  PCT_OPTIONS_STRING+=$'\n'"  -memory ${RAM_SIZE:-$var_ram}"
  PCT_OPTIONS_STRING+=$'\n'"  -unprivileged ${CT_TYPE:-1}"
  [[ -n "${PW:-}" ]] && PCT_OPTIONS_STRING+=$'\n'"  $PW"

  export PCT_OPTIONS="$PCT_OPTIONS_STRING"
  export TEMPLATE_STORAGE="${var_template_storage:-}"
  export CONTAINER_STORAGE="${var_container_storage:-}"

  # --- Create LXC (framework function: template discovery, storage, pct create) ---
  create_lxc_container || exit $?

  # --- Start container and wait for network ---
  msg_info "Starting LXC Container"
  pct start "$CTID"
  for i in {1..10}; do
    if pct status "$CTID" | grep -q "status: running"; then
      msg_ok "Started LXC Container"
      break
    fi
    sleep 1
    [[ "$i" -eq 10 ]] && { msg_error "LXC Container did not start"; exit 117; }
  done

  msg_info "Waiting for network in LXC container"
  local ip_in_lxc="" wait_secs=0
  while true; do
    ip_in_lxc=$(pct exec "$CTID" -- ip -4 addr show dev eth0 2>/dev/null | awk '/inet / {print $2}' | cut -d/ -f1)
    [[ -z "$ip_in_lxc" ]] && ip_in_lxc=$(pct exec "$CTID" -- ip -6 addr show dev eth0 scope global 2>/dev/null | awk '/inet6 / {print $2}' | cut -d/ -f1 | head -n1)
    [[ -n "$ip_in_lxc" ]] && break
    sleep 1
    wait_secs=$((wait_secs + 1))
    if ((wait_secs % 20 == 0)); then
      msg_warn "No IP on eth0 after ${wait_secs}s — still waiting (Ctrl+C to abort)"
    fi
  done
  msg_ok "Network in LXC is reachable (${ip_in_lxc})"

  # --- Base packages ---
  msg_info "Customizing LXC Container"
  sleep 2
  pct exec "$CTID" -- bash -c "apt-get update 2>&1 && apt-get install -y sudo curl mc gnupg2 jq 2>&1" >>"$BUILD_LOG" 2>&1 || {
    msg_error "Failed to install base packages"
    exit 116
  }
  msg_ok "Customized LXC Container"

  # --- Run MQTTower install script (the ONLY difference vs framework build_container) ---
  # Use pct exec (not lxc-attach): unprivileged LXC often returns EPERM from lxc-attach, and
  # pct exec does not inherit the host env — pass MQTTOWER_* explicitly (collect_mqttower_env).
  # Append to BUILD_LOG (same as base apt) so failures show the real error; the UI often maps
  # inner exit 1 to "Operation not permitted" even when the cause is e.g. apt or curl to GitHub.
  msg_info "Running MQTTower install script (full output: ${BUILD_LOG})"
  pct exec "$CTID" -- env \
    CONTAINER_INSTALLING=true \
    "MQTTOWER_DEPLOY_MODE=${DEPLOY_MODE}" \
    "MQTTOWER_CT_URL=${MQTTOWER_CT_URL:-${MQTTOWER_DEPLOY_BASE}/ct/mqttower.sh}" \
    "MQTTOWER_DASHBOARD_URL=${MQTTOWER_DASHBOARD_URL:-}" \
    "MQTTOWER_REG_SECRET=${MQTTOWER_REG_SECRET:-}" \
    "MQTTOWER_API_KEY=${MQTTOWER_API_KEY:-}" \
    "MQTTOWER_MQTT_PORT=${MQTTOWER_MQTT_PORT:-}" \
    "MQTTOWER_AGENT_PORT=${MQTTOWER_AGENT_PORT:-}" \
    "MQTTOWER_PUBLIC_AGENT_URL=${MQTTOWER_PUBLIC_AGENT_URL:-}" \
    "MQTTOWER_MQTT_USER=${MQTTOWER_MQTT_USER:-}" \
    "MQTTOWER_MQTT_PASS=${MQTTOWER_MQTT_PASS:-}" \
    "MQTTOWER_ADMIN_USER=${MQTTOWER_ADMIN_USER:-}" \
    "MQTTOWER_ADMIN_PASS=${MQTTOWER_ADMIN_PASS:-}" \
    "MQTTOWER_BROKER_HOST=${MQTTOWER_BROKER_HOST:-}" \
    "MQTTOWER_BROKER_PORT=${MQTTOWER_BROKER_PORT:-}" \
    "MQTTOWER_WEB_PORT=${MQTTOWER_WEB_PORT:-}" \
    "MQTTOWER_LOCAL_AGENT_URL=${MQTTOWER_LOCAL_AGENT_URL:-}" \
    "MQTTOWER_LOCAL_AGENT_API_KEY=${MQTTOWER_LOCAL_AGENT_API_KEY:-}" \
    bash -c "$(curl -fsSL "$MQTTOWER_INSTALL_URL")" >>"$BUILD_LOG" 2>&1 || {
    msg_error "MQTTower install script failed — see tail of ${BUILD_LOG}"
    exit 1
  }
}

pick_deploy_mode
collect_mqttower_env
mqttower_build_container
description

msg_ok "Completed Successfully!\n"
echo -e "${CREATING}${GN}${APP} setup has been successfully initialized!${CL}"
case "${DEPLOY_MODE}" in
  broker)
    echo -e "${INFO}${YW} Access it using the following URL:${CL}"
    echo -e "${TAB}${GATEWAY}${BGN}http://${IP}:${MQTTOWER_AGENT_PORT:-5080}${CL}"
    echo -e "${TAB}${INFO}MQTT port: ${MQTTOWER_MQTT_PORT:-1883} — DynSec user: ${MQTTOWER_MQTT_USER:-mqttower-admin}${CL}"
    ;;
  dashboard)
    echo -e "${INFO}${YW} Access it using the following URL:${CL}"
    echo -e "${TAB}${GATEWAY}${BGN}http://${IP}:${MQTTOWER_WEB_PORT:-8080}${CL}"
    echo -e "${TAB}${INFO}Login user: ${MQTTOWER_ADMIN_USER:-admin}${CL}"
    ;;
  fullstack)
    echo -e "${INFO}${YW} Access it using the following URL:${CL}"
    echo -e "${TAB}${GATEWAY}${BGN}http://${IP}:${MQTTOWER_WEB_PORT:-8080}${CL}"
    echo -e "${TAB}${INFO}MQTT port: ${MQTTOWER_MQTT_PORT:-1883} — Agent: http://${IP}:${MQTTOWER_AGENT_PORT:-5080}${CL}"
    echo -e "${TAB}${INFO}Login user: ${MQTTOWER_ADMIN_USER:-admin} — local agent pre-linked${CL}"
    ;;
esac
