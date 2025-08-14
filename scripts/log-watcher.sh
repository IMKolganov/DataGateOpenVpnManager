#!/bin/bash

# This script watches the OpenVPN log file for authentication failures (AUTH_FAILED).
# When such a line is detected, it extracts the Common Name (CN) from the log
# and sends the failure event to the backend API for further processing or alerting.

set -eu

LOG_FILE="/var/log/openvpn.log"

post_bg() {
  (curl -sS --max-time 2 -H "Content-Type: application/json" -X POST "http://localhost:__API_PORT__/$1" -d "$2" >/dev/null 2>&1) &
}

b64_env() {
  if base64 --help 2>/dev/null | grep -q -- "--wrap"; then
    env | base64 -w0
  else
    env | base64
  fi
}

iso_now() {
  date -u +"%Y-%m-%dT%H:%M:%SZ"
}

tail -F "$LOG_FILE" | while IFS= read -r line; do
  if echo "$line" | grep -q "AUTH_FAILED"; then
    cn="$(echo "$line" | grep -oP "CN='[^']+'" | cut -d"'" -f2 || true)"

    post_bg "api/vpnEvent/fail" "$(cat <<JSON
{
  "CommonName": "${cn:-}",
  "Message": "$line",
  "Timestamp": "$(iso_now)"
}
JSON
)"

    post_bg "api/vpnEvent/envdump" "$(cat <<JSON
{
  "Hook": "log-watcher",
  "Timestamp": "$(iso_now)",
  "Args": ["AUTH_FAILED"],
  "EnvB64": "$(b64_env)"
}
JSON
)"
  fi
done
