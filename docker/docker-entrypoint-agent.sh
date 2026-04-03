#!/bin/sh
set -e
CONF="${MQTTower__MosquittoConfigPath:-/mosquitto/config/mosquitto.conf}"
if [ ! -f "$CONF" ]; then
  echo "Missing mosquitto config: $CONF" >&2
  exit 1
fi
mosquitto -c "$CONF" -d
exec dotnet MQTTower.Agent.dll "$@"
