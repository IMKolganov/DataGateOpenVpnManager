#!/bin/bash

# Watch OpenVPN log file for any FAIL/ERROR entries and send them to backend.
set -eu

LOG_FILE="/var/log/openvpn.log"
API_URL="http://localhost:__API_PORT__/api/vpnEvent/authfail"

post_bg() {
  (curl -sS --max-time 2 \
    -H "Content-Type: application/json" \
    -X POST "$API_URL" \
    -d "$1" >/dev/null 2>&1) &
}

iso_now() {
  date -u +"%Y-%m-%dT%H:%M:%SZ"
}

tail -F "$LOG_FILE" | while IFS= read -r line; do
  # Match FAIL or ERROR in any case
  if echo "$line" | grep -qiE "fail|error"; then
    cn="$(echo "$line" | grep -oP "CN='[^']+'" | cut -d"'" -f2 || true)"
    post_bg "$(cat <<JSON
{
  "VpnServerId": __VPN_SERVER_ID__,
  "EventType": "AuthFailed",
  "CommonName": "${cn:-}",
  "Message": "$line",
  "EventTimeUtc": "$(iso_now)"
}
JSON
)"
  fi
done
