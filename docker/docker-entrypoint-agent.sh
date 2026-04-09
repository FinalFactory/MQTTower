#!/bin/sh
set -e
CONF="${MQTTower__MosquittoConfigPath:-/mosquitto/config/mosquitto.conf}"
DS="${MQTTOWER_DYNSEC_JSON_PATH:-/mosquitto/data/dynamic-security.json}"
MQTT_USER="${MQTTOWER_MQTT_USER:-mqttower-admin}"
MQTT_PASS="${MQTTOWER_MQTT_PASS:-changeme-mqtt-dev}"

if [ ! -f "$CONF" ]; then
  echo "Missing mosquitto config: $CONF" >&2
  exit 1
fi

mkdir -p "$(dirname "$DS")"
if [ ! -f "$DS" ]; then
  echo "Initializing DynSec at $DS (user=$MQTT_USER)..."
  mosquitto_ctrl dynsec init "$DS" "$MQTT_USER" "$MQTT_PASS"
fi

# Stock dynsec init does not grant publishClientSend on # for admin; agent needs it for app publishes.
# Optional template role mqttower-app: full MQTT except DynSec control API topics.
if command -v mosquitto_ctrl >/dev/null 2>&1 && [ -f "$DS" ]; then
  mosquitto_ctrl -f "$DS" dynsec addRoleACL admin publishClientSend '#' allow 2>/dev/null || true
  if ! mosquitto_ctrl -f "$DS" dynsec getRole mqttower-app >/dev/null 2>&1; then
    if mosquitto_ctrl -f "$DS" dynsec createRole mqttower-app >/dev/null 2>&1; then
      ctl='$CONTROL/dynamic-security/#'
      mosquitto_ctrl -f "$DS" dynsec addRoleACL mqttower-app publishClientSend "$ctl" deny 100 2>/dev/null || true
      mosquitto_ctrl -f "$DS" dynsec addRoleACL mqttower-app subscribePattern "$ctl" deny 100 2>/dev/null || true
      mosquitto_ctrl -f "$DS" dynsec addRoleACL mqttower-app publishClientReceive "$ctl" deny 100 2>/dev/null || true
      mosquitto_ctrl -f "$DS" dynsec addRoleACL mqttower-app publishClientSend '#' allow 0 2>/dev/null || true
      mosquitto_ctrl -f "$DS" dynsec addRoleACL mqttower-app subscribePattern '#' allow 0 2>/dev/null || true
      mosquitto_ctrl -f "$DS" dynsec addRoleACL mqttower-app publishClientReceive '#' allow 0 2>/dev/null || true
      mosquitto_ctrl -f "$DS" dynsec addRoleACL mqttower-app unsubscribePattern '#' allow 0 2>/dev/null || true
    fi
  fi
fi

mosquitto -c "$CONF" -d
exec dotnet MQTTower.Agent.dll "$@"
