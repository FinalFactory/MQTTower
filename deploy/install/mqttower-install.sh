#!/usr/bin/env bash
# Copyright (c) 2021-2026 community-scripts ORG
# Author: FinalFactory
# License: MIT | https://github.com/community-scripts/ProxmoxVE/raw/main/LICENSE
# Source: https://github.com/FinalFactory/MQTTower

: "${GITHUB_REPO_OWNER:=FinalFactory}"
: "${GITHUB_REPO_NAME:=MQTTower}"
: "${MQTTOWER_DEPLOY_MODE:?MQTTOWER_DEPLOY_MODE must be set (broker|dashboard|fullstack)}"

INSTALL_FUNC_URL="${INSTALL_FUNC_URL:-https://raw.githubusercontent.com/community-scripts/ProxmoxVE/main/misc/install.func}"
FUNCTIONS_FILE_PATH="${FUNCTIONS_FILE_PATH:-$(curl -fsSL "$INSTALL_FUNC_URL")}"

source /dev/stdin <<<"$FUNCTIONS_FILE_PATH"
color
verb_ip6
catch_errors
setting_up_container
network_check
update_os

export APPLICATION="${APPLICATION:-MQTTower}"
export app="${app:-mqttower}"

: "${ASSET_AGENT:=mqttower-agent-linux-x64.tar.gz}"
: "${ASSET_WEB:=mqttower-web-linux-x64.tar.gz}"
: "${INSTALL_DIR_AGENT:=/opt/mqttower-agent}"
: "${INSTALL_DIR_WEB:=/opt/mqttower-web}"
: "${DATA_DIR:=/var/lib/mqttower}"

need_cmd() {
  command -v "$1" >/dev/null 2>&1 || {
    msg_error "Missing command: $1"
    exit 127
  }
}

get_ipv4() {
  hostname -I 2>/dev/null | tr ' ' '\n' | grep -E '^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$' | head -n1 || true
}

rand_hex() {
  openssl rand -hex 16 2>/dev/null || head -c 16 /dev/urandom | xxd -p
}

# Unprivileged LXC: systemctl enable may fail with EPERM when creating unit symlinks; start often works.
systemctl_enable_now_best_effort() {
  local svc="$1"
  if systemctl enable -q --now "$svc" 2>/dev/null; then
    return 0
  fi
  msg_info "systemctl enable --now failed for ${svc}; trying start only (common in unprivileged LXC)."
  systemctl daemon-reload 2>/dev/null || true
  if systemctl start "$svc" 2>/dev/null && systemctl is-active --quiet "$svc" 2>/dev/null; then
    return 0
  fi
  if command -v service >/dev/null 2>&1 && service "$svc" start 2>/dev/null; then
    systemctl is-active --quiet "$svc" 2>/dev/null && return 0
    pgrep -x mosquitto >/dev/null 2>&1 && [[ "$svc" == "mosquitto" ]] && return 0
  fi
  msg_error "Could not start ${svc}. See: journalctl -xeu ${svc}"
  return 1
}

# Append KEY=VALUE to file only if KEY is not already present (safe re-runs / new version keys).
ensure_env_key() {
  local file="$1" key="$2" value="$3"
  if ! grep -q "^${key}=" "$file" 2>/dev/null; then
    printf '%s=%s\n' "$key" "$value" >> "$file"
  fi
}

install_dotnet_runtime() {
  export DEBIAN_FRONTEND=noninteractive
  msg_info "Installing Microsoft .NET repository"
  local deb_ver
  deb_ver="$(. /etc/os-release 2>/dev/null && echo "${VERSION_ID%%.*}" || echo "12")"
  if [[ "$deb_ver" != "12" && "$deb_ver" != "13" ]]; then
    deb_ver="12"
  fi
  if [[ ! -f /etc/apt/sources.list.d/microsoft-prod.list ]]; then
    $STD curl -fsSL "https://packages.microsoft.com/config/debian/${deb_ver}/packages-microsoft-prod.deb" -o /tmp/packages-microsoft-prod.deb
    $STD dpkg -i /tmp/packages-microsoft-prod.deb
    rm -f /tmp/packages-microsoft-prod.deb
  fi
  $STD apt-get update -y
  # Agent and Web need Microsoft.AspNetCore.App (aspnetcore-runtime), not only dotnet-runtime.
  $STD apt-get install -y aspnetcore-runtime-9.0
  msg_ok "Installed ASP.NET Core runtime"
}

dynsec_plugin_path() {
  local p
  for p in \
    /usr/lib/x86_64-linux-gnu/mosquitto_dynamic_security.so \
    /usr/lib/aarch64-linux-gnu/mosquitto_dynamic_security.so \
    /usr/lib/arm-linux-gnueabihf/mosquitto_dynamic_security.so \
    /usr/lib/mosquitto_dynamic_security.so; do
    if [[ -f "$p" ]]; then
      echo "$p"
      return 0
    fi
  done
  p="$(find /usr/lib -maxdepth 2 -name 'mosquitto_dynamic_security.so' -type f 2>/dev/null | head -n1)"
  if [[ -n "$p" && -f "$p" ]]; then
    echo "$p"
    return 0
  fi
  msg_error "mosquitto_dynamic_security.so not found"
  exit 127
}

# Debian often ships listener/port in the main file or another conf.d drop-in; mqttower.conf adds listener + DynSec.
# Duplicate listeners cause mosquitto to exit with status 3.
mosquitto_disable_conflicting_listeners() {
  local f
  local -a files
  mapfile -t files < <(find /etc/mosquitto/conf.d -maxdepth 1 -name '*.conf' -type f 2>/dev/null || true)
  for f in /etc/mosquitto/mosquitto.conf "${files[@]}"; do
    [[ -f "$f" ]] || continue
    [[ "$(basename "$f")" == "mqttower.conf" ]] && continue
    if ! grep -qE '^[[:space:]]*listener[[:space:]]|^[[:space:]]*port[[:space:]]|^[[:space:]]*bind_address[[:space:]]' "$f" 2>/dev/null; then
      continue
    fi
    msg_info "Disabling default listener/port/bind lines in ${f} (MQTTower uses conf.d/mqttower.conf)."
    cp -a "$f" "${f}.bak-mqttower"
    sed -i \
      -e '/^[[:space:]]*#/b' \
      -e 's/^[[:space:]]*\(listener\)/#\1/' \
      -e 's/^[[:space:]]*\(port\)/#\1/' \
      -e 's/^[[:space:]]*\(bind_address\)/#\1/' \
      "$f"
  done
}

init_dynsec() {
  need_cmd mosquitto_ctrl
  local ds="/var/lib/mosquitto/dynamic-security.json"
  mkdir -p /var/lib/mosquitto
  chown mosquitto:mosquitto /var/lib/mosquitto 2>/dev/null || true
  if [[ -f "$ds" ]]; then
    msg_info "DynSec database already exists: $ds (skipping init)"
    chown mosquitto:mosquitto "$ds" 2>/dev/null || true
    return 0
  fi
  msg_info "Initializing Dynamic Security (DynSec) admin user ${MQTTOWER_MQTT_USER}"
  mosquitto_ctrl dynsec init "$ds" "${MQTTOWER_MQTT_USER}" "${MQTTOWER_MQTT_PASS}"
  chown mosquitto:mosquitto "$ds"
  msg_ok "DynSec initialized at ${ds}"
}

write_mosquitto_conf() {
  local port="$1"
  local conf="/etc/mosquitto/conf.d/mqttower.conf"
  if [[ -f "$conf" ]]; then
    msg_info "Mosquitto config already exists: $conf (skipping)"
    return 0
  fi
  local plugin
  plugin="$(dynsec_plugin_path)"
  mkdir -p /var/lib/mosquitto
  chown mosquitto:mosquitto /var/lib/mosquitto 2>/dev/null || true
  # Do not set persistence / persistence_location / log_dest here: Debian's
  # /etc/mosquitto/mosquitto.conf already defines them; duplicates abort with exit 3.
  cat <<EOF >"$conf"
per_listener_settings false
allow_anonymous false
listener ${port}
plugin ${plugin}
plugin_opt_config_file /var/lib/mosquitto/dynamic-security.json
EOF
  touch /var/log/mosquitto/mosquitto.log
  chown mosquitto:mosquitto /var/log/mosquitto/mosquitto.log 2>/dev/null || true
  msg_ok "Mosquitto config: $conf (listener ${port}, DynSec)"
}

write_agent_env() {
  local f="/etc/mqttower-agent/environment"
  mkdir -p /etc/mqttower-agent
  umask 077
  : "${MQTTOWER_MQTT_USER:=mqttower-admin}"
  : "${MQTTOWER_MQTT_PASS:=}"
  : "${MQTTOWER_AGENT_PORT:=5080}"
  : "${MQTTOWER_DASHBOARD_URL:=}"
  : "${MQTTOWER_REG_SECRET:=}"
  : "${MQTTOWER_API_KEY:=}"
  : "${MQTTOWER_PUBLIC_AGENT_URL:=}"
  if [[ -f "$f" ]]; then
    msg_info "Agent environment exists; merging new keys only."
    ensure_env_key "$f" "ASPNETCORE_ENVIRONMENT" "Production"
    ensure_env_key "$f" "MQTTower__MosquittoConfigPath" "/etc/mosquitto/conf.d/mqttower.conf"
    ensure_env_key "$f" "MQTTower__MosquittoLogPath" "/var/log/mosquitto/mosquitto.log"
    ensure_env_key "$f" "MQTTower__BrokerUsername" "${MQTTOWER_MQTT_USER}"
    ensure_env_key "$f" "MQTTower__BrokerPassword" "${MQTTOWER_MQTT_PASS}"
    ensure_env_key "$f" "Agent__HttpPort" "${MQTTOWER_AGENT_PORT}"
    ensure_env_key "$f" "Agent__HttpsPort" "0"
    ensure_env_key "$f" "Agent__DashboardUrl" "${MQTTOWER_DASHBOARD_URL}"
    ensure_env_key "$f" "Agent__RegistrationToken" "${MQTTOWER_REG_SECRET}"
    ensure_env_key "$f" "Agent__ApiKey" "${MQTTOWER_API_KEY}"
    ensure_env_key "$f" "Agent__AutoRegister" "true"
    ensure_env_key "$f" "Agent__PublicAgentUrl" "${MQTTOWER_PUBLIC_AGENT_URL}"
    ensure_env_key "$f" "Agent__RestartCommand" "systemctl reload mosquitto"
    ensure_env_key "$f" "Agent__WatcherNotifySecret" "${MQTTOWER_WATCHER_NOTIFY_SECRET:-${MQTTOWER_API_KEY:-}}"
    msg_ok "Updated ${f} (merge)"
  else
    cat <<EOF >"$f"
ASPNETCORE_ENVIRONMENT=Production
MQTTower__MosquittoConfigPath=/etc/mosquitto/conf.d/mqttower.conf
MQTTower__MosquittoLogPath=/var/log/mosquitto/mosquitto.log
MQTTower__BrokerUsername=${MQTTOWER_MQTT_USER}
MQTTower__BrokerPassword=${MQTTOWER_MQTT_PASS}
Agent__HttpPort=${MQTTOWER_AGENT_PORT}
Agent__HttpsPort=0
Agent__DashboardUrl=${MQTTOWER_DASHBOARD_URL}
Agent__RegistrationToken=${MQTTOWER_REG_SECRET}
Agent__ApiKey=${MQTTOWER_API_KEY}
Agent__AutoRegister=true
Agent__PublicAgentUrl=${MQTTOWER_PUBLIC_AGENT_URL}
Agent__RestartCommand=systemctl reload mosquitto
Agent__WatcherNotifySecret=${MQTTOWER_WATCHER_NOTIFY_SECRET:-${MQTTOWER_API_KEY:-}}
EOF
    msg_ok "Wrote ${f}"
  fi
  chmod 600 "$f"
}

write_agent_systemd() {
  cat <<'EOF' >/etc/systemd/system/mqttower-agent.service
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

migrate_legacy_version() {
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
}

# --- Broker-side: optional interactive prompts when env incomplete ---
all_broker_env_preset() {
  [[ -n "${MQTTOWER_DASHBOARD_URL:-}" && -n "${MQTTOWER_REG_SECRET:-}" && -n "${MQTTOWER_API_KEY:-}" \
    && -n "${MQTTOWER_MQTT_PORT:-}" && -n "${MQTTOWER_AGENT_PORT:-}" \
    && -n "${MQTTOWER_MQTT_USER:-}" && -n "${MQTTOWER_MQTT_PASS:-}" ]]
}

collect_broker_interactive() {
  if all_broker_env_preset; then
    msg_info "Using broker configuration from environment (non-interactive)."
    return 0
  fi
  msg_info "Interactive broker configuration (set MQTTOWER_* env vars to skip)."
  read -r -p "${TAB3}Dashboard base URL (e.g. http://192.168.1.10:8080): " MQTTOWER_DASHBOARD_URL || true
  read -r -p "${TAB3}Registration secret (MQTTower:RegistrationSecret): " MQTTOWER_REG_SECRET || true
  if [[ -z "${MQTTOWER_API_KEY:-}" ]]; then
    MQTTOWER_API_KEY="$(rand_hex)"
    msg_info "Generated API key: ${MQTTOWER_API_KEY}"
  fi
  : "${MQTTOWER_MQTT_PORT:=1883}"
  : "${MQTTOWER_AGENT_PORT:=5080}"
  read -r -p "${TAB3}MQTT listener port [${MQTTOWER_MQTT_PORT}]: " _mp || true
  [[ -n "${_mp:-}" ]] && MQTTOWER_MQTT_PORT="$_mp"
  read -r -p "${TAB3}Agent HTTP port [${MQTTOWER_AGENT_PORT}]: " _ap || true
  [[ -n "${_ap:-}" ]] && MQTTOWER_AGENT_PORT="$_ap"
  : "${MQTTOWER_MQTT_USER:=mqttower-admin}"
  if [[ -z "${MQTTOWER_MQTT_PASS:-}" ]]; then
    MQTTOWER_MQTT_PASS="$(rand_hex)"
    msg_info "Generated MQTT admin password (DynSec): ${MQTTOWER_MQTT_PASS}"
  fi
}

resolve_public_url() {
  if [[ -z "${MQTTOWER_PUBLIC_AGENT_URL:-}" ]]; then
    local ip
    ip="$(get_ipv4)"
    if [[ -n "$ip" ]]; then
      MQTTOWER_PUBLIC_AGENT_URL="http://${ip}:${MQTTOWER_AGENT_PORT}"
      msg_info "Public Agent URL (registration): ${MQTTOWER_PUBLIC_AGENT_URL}"
    else
      read -r -p "${TAB3}Public URL for this agent (as dashboard will call it) [http://127.0.0.1:${MQTTOWER_AGENT_PORT}]: " MQTTOWER_PUBLIC_AGENT_URL || true
      : "${MQTTOWER_PUBLIC_AGENT_URL:=http://127.0.0.1:${MQTTOWER_AGENT_PORT}}"
    fi
  fi
}

install_broker_stack() {
  export DEBIAN_FRONTEND=noninteractive
  msg_info "Installing Mosquitto"
  $STD apt-get install -y mosquitto mosquitto-clients
  msg_ok "Installed Mosquitto"

  mosquitto_disable_conflicting_listeners

  install_dotnet_runtime
  migrate_legacy_version

  if [[ -f /etc/mqttower-agent/environment ]]; then
    msg_info "Agent environment exists; skipping interactive prompts (merge path)."
  else
    collect_broker_interactive
    resolve_public_url
  fi

  init_dynsec

  fetch_and_deploy_gh_release "mqttower-agent" "${GITHUB_REPO_OWNER}/${GITHUB_REPO_NAME}" "prebuild" "latest" "${INSTALL_DIR_AGENT}" "${ASSET_AGENT}"

  write_mosquitto_conf "${MQTTOWER_MQTT_PORT}"
  write_agent_env
  write_agent_systemd

  systemctl_enable_now_best_effort mosquitto
  systemctl_enable_now_best_effort mqttower-agent
}

all_dashboard_env_preset() {
  [[ -n "${MQTTOWER_ADMIN_USER:-}" && -n "${MQTTOWER_ADMIN_PASS:-}" \
    && -n "${MQTTOWER_REG_SECRET:-}" && -n "${MQTTOWER_WEB_PORT:-}" ]]
}

collect_dashboard_interactive() {
  if all_dashboard_env_preset; then
    msg_info "Using dashboard configuration from environment (non-interactive)."
    return 0
  fi
  msg_info "Interactive dashboard configuration (set env vars to skip)."
  : "${MQTTOWER_ADMIN_USER:=admin}"
  read -r -p "${TAB3}Admin username [${MQTTOWER_ADMIN_USER}]: " _au || true
  [[ -n "${_au:-}" ]] && MQTTOWER_ADMIN_USER="$_au"
  if [[ -z "${MQTTOWER_ADMIN_PASS:-}" ]]; then
    MQTTOWER_ADMIN_PASS="$(rand_hex)"
    msg_info "Generated admin password: ${MQTTOWER_ADMIN_PASS}"
  fi
  read -r -p "${TAB3}Registration secret: " MQTTOWER_REG_SECRET || true
  : "${MQTTOWER_WEB_PORT:=8080}"
  read -r -p "${TAB3}Dashboard HTTP port [${MQTTOWER_WEB_PORT}]: " _wp || true
  [[ -n "${_wp:-}" ]] && MQTTOWER_WEB_PORT="$_wp"
}

prepare_data_dir() {
  mkdir -p "$DATA_DIR"
  chmod 755 "$DATA_DIR"
  msg_ok "Data directory: ${DATA_DIR}"
}

# Dashboard talks to brokers only via agents (HTTP). Optional: LocalAgentUrl / LocalAgentApiKey for the co-located agent.
write_web_env() {
  local f="/etc/mqttower/environment"
  mkdir -p /etc/mqttower
  umask 077
  : "${MQTTOWER_WEB_PORT:=8080}"
  if [[ -f "$f" ]]; then
    msg_info "Dashboard environment exists; merging new keys only."
    : "${MQTTOWER_ADMIN_USER:=admin}"
    : "${MQTTOWER_ADMIN_PASS:=changeme}"
    ensure_env_key "$f" "ASPNETCORE_ENVIRONMENT" "Production"
    ensure_env_key "$f" "ASPNETCORE_URLS" "http://+:${MQTTOWER_WEB_PORT}"
    ensure_env_key "$f" "ConnectionStrings__Default" "Data Source=${DATA_DIR}/mqttower.db"
    ensure_env_key "$f" "MQTTower__DatabasePath" "Data Source=${DATA_DIR}/mqttower.db"
    ensure_env_key "$f" "MQTTower__RegistrationSecret" "${MQTTOWER_REG_SECRET:-}"
    ensure_env_key "$f" "MQTTower__WatcherNotifySecret" "${MQTTOWER_WATCHER_NOTIFY_SECRET:-}"
    ensure_env_key "$f" "MQTTOWER_ADMIN_USER" "${MQTTOWER_ADMIN_USER}"
    ensure_env_key "$f" "MQTTOWER_ADMIN_PASS" "${MQTTOWER_ADMIN_PASS}"
    if [[ -n "${MQTTOWER_LOCAL_AGENT_URL:-}" ]]; then
      ensure_env_key "$f" "MQTTower__LocalAgentUrl" "${MQTTOWER_LOCAL_AGENT_URL}"
    fi
    if [[ -n "${MQTTOWER_LOCAL_AGENT_API_KEY:-}" ]]; then
      ensure_env_key "$f" "MQTTower__LocalAgentApiKey" "${MQTTOWER_LOCAL_AGENT_API_KEY}"
    fi
    msg_ok "Updated ${f} (merge)"
  else
    cat <<EOF >"$f"
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:${MQTTOWER_WEB_PORT}
ConnectionStrings__Default=Data Source=${DATA_DIR}/mqttower.db
MQTTower__DatabasePath=Data Source=${DATA_DIR}/mqttower.db
MQTTower__RegistrationSecret=${MQTTOWER_REG_SECRET}
MQTTower__WatcherNotifySecret=${MQTTOWER_WATCHER_NOTIFY_SECRET:-}
MQTTOWER_ADMIN_USER=${MQTTOWER_ADMIN_USER}
MQTTOWER_ADMIN_PASS=${MQTTOWER_ADMIN_PASS}
EOF
    if [[ -n "${MQTTOWER_LOCAL_AGENT_URL:-}" ]]; then
      printf 'MQTTower__LocalAgentUrl=%s\n' "${MQTTOWER_LOCAL_AGENT_URL}" >>"$f"
    fi
    if [[ -n "${MQTTOWER_LOCAL_AGENT_API_KEY:-}" ]]; then
      printf 'MQTTower__LocalAgentApiKey=%s\n' "${MQTTOWER_LOCAL_AGENT_API_KEY}" >>"$f"
    fi
    msg_ok "Wrote ${f}"
  fi
  chmod 600 "$f"
}

write_web_systemd() {
  cat <<'EOF' >/etc/systemd/system/mqttower.service
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

install_dashboard_stack() {
  install_dotnet_runtime
  migrate_legacy_version
  if [[ -f /etc/mqttower/environment ]]; then
    msg_info "Dashboard environment exists; skipping interactive prompts (merge path)."
  else
    collect_dashboard_interactive
  fi
  : "${MQTTOWER_WEB_PORT:=8080}"

  fetch_and_deploy_gh_release "mqttower-web" "${GITHUB_REPO_OWNER}/${GITHUB_REPO_NAME}" "prebuild" "latest" "${INSTALL_DIR_WEB}" "${ASSET_WEB}"

  prepare_data_dir
  write_web_env
  write_web_systemd
  systemctl daemon-reload 2>/dev/null || true
  systemctl_enable_now_best_effort mqttower
}

install_fullstack() {
  export DEBIAN_FRONTEND=noninteractive
  msg_info "Installing Mosquitto"
  $STD apt-get install -y mosquitto mosquitto-clients
  msg_ok "Installed Mosquitto"

  mosquitto_disable_conflicting_listeners

  install_dotnet_runtime
  migrate_legacy_version

  : "${MQTTOWER_MQTT_PORT:=1883}"
  : "${MQTTOWER_AGENT_PORT:=5080}"
  : "${MQTTOWER_WEB_PORT:=8080}"
  : "${MQTTOWER_BROKER_HOST:=127.0.0.1}"
  : "${MQTTOWER_BROKER_PORT:=1883}"
  : "${MQTTOWER_MQTT_USER:=mqttower-admin}"
  : "${MQTTOWER_ADMIN_USER:=admin}"
  MQTTOWER_DASHBOARD_URL="${MQTTOWER_DASHBOARD_URL:-http://127.0.0.1:${MQTTOWER_WEB_PORT}}"
  MQTTOWER_PUBLIC_AGENT_URL="${MQTTOWER_PUBLIC_AGENT_URL:-http://127.0.0.1:${MQTTOWER_AGENT_PORT}}"
  MQTTOWER_LOCAL_AGENT_URL="${MQTTOWER_LOCAL_AGENT_URL:-${MQTTOWER_PUBLIC_AGENT_URL}}"
  MQTTOWER_LOCAL_AGENT_API_KEY="${MQTTOWER_LOCAL_AGENT_API_KEY:-${MQTTOWER_API_KEY}}"
  MQTTOWER_WATCHER_NOTIFY_SECRET="${MQTTOWER_WATCHER_NOTIFY_SECRET:-${MQTTOWER_API_KEY}}"

  init_dynsec

  fetch_and_deploy_gh_release "mqttower-agent" "${GITHUB_REPO_OWNER}/${GITHUB_REPO_NAME}" "prebuild" "latest" "${INSTALL_DIR_AGENT}" "${ASSET_AGENT}"

  write_mosquitto_conf "${MQTTOWER_MQTT_PORT}"
  write_agent_env
  write_agent_systemd

  fetch_and_deploy_gh_release "mqttower-web" "${GITHUB_REPO_OWNER}/${GITHUB_REPO_NAME}" "prebuild" "latest" "${INSTALL_DIR_WEB}" "${ASSET_WEB}"

  prepare_data_dir
  write_web_env
  write_web_systemd

  systemctl daemon-reload 2>/dev/null || true
  systemctl_enable_now_best_effort mosquitto
  systemctl_enable_now_best_effort mqttower-agent
  systemctl_enable_now_best_effort mqttower
}

write_mqttower_update_command() {
  local url="${MQTTOWER_CT_URL:-https://raw.githubusercontent.com/FinalFactory/MQTTower/main/deploy/ct/mqttower.sh}"
  cat <<EOF >/usr/bin/update
#!/usr/bin/env bash
exec bash -c "\$(curl -fsSL '${url}')"
EOF
  chmod +x /usr/bin/update
}

main() {
  need_cmd curl
  need_cmd jq
  ensure_dependencies jq openssl

  case "${MQTTOWER_DEPLOY_MODE}" in
    broker)
      install_broker_stack
      ;;
    dashboard)
      install_dashboard_stack
      ;;
    fullstack)
      install_fullstack
      ;;
    *)
      msg_error "Unknown MQTTOWER_DEPLOY_MODE=${MQTTOWER_DEPLOY_MODE}"
      exit 1
      ;;
  esac

  motd_ssh
  customize
  write_mqttower_update_command
  cleanup_lxc
}

main "$@"
