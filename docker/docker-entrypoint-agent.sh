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

mosquitto -c "$CONF" -d
exec dotnet MQTTower.Agent.dll "$@"
