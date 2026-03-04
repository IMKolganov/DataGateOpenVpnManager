#!/bin/bash

# This script is called by OpenVPN when a client disconnects.
# It sends a POST request to the backend API with disconnection details:
# - $common_name: the client's certificate common name
# - $untrusted_ip:$untrusted_port: the real IP/port of the client
# - $ifconfig_pool_local: the VPN IP address that was assigned
# - current UTC timestamp as the disconnection time

# OpenVPN hook: client-disconnect

set -eu

API_BASE="http://${API_HOST:-127.0.0.1}:${API_PORT:-5010}"

sanitize() {
  printf "%s" "${1:-}" | tr '\n' ' ' | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'
}

unix_to_iso() {
  if date -u -d "@$1" +"%Y-%m-%dT%H:%M:%SZ" >/dev/null 2>&1; then
    date -u -d "@$1" +"%Y-%m-%dT%H:%M:%SZ"
  else
    printf "%sZ" "$(printf "%s" "${time_ascii:-}" | sed 's/ /T/')"
  fi
}

CN="$(sanitize "${common_name:-}")"
REAL_ADDR="$(sanitize "${untrusted_ip:-}:${untrusted_port:-}")"
VIRT_IP="$(sanitize "${ifconfig_pool_remote_ip:-}")"
SCRIPT_TYPE="$(sanitize "${script_type:-client-disconnect}")"

BYTES_IN="${bytes_received:-0}"
BYTES_OUT="${bytes_sent:-0}"
DUR="${time_duration:-0}"
CS_ISO=""
DSC_ISO=""

if [ -n "${time_unix:-}" ]; then
  CS_ISO="$(unix_to_iso "${time_unix}")"
  if [ -n "$DUR" ] 2>/dev/null; then
    disc=$(( ${time_unix} + ${DUR:-0} ))
    DSC_ISO="$(unix_to_iso "$disc")"
  fi
fi

post_bg() {
  (
    curl -sS --max-time 3 -H "Content-Type: application/json" \
      -X POST "$API_BASE/api/vpn-events/disconnect" \
      -d "$(cat <<JSON
{
  "EventType": "ClientDisconnect",
  "ScriptType": "$SCRIPT_TYPE",
  "CommonName": "$CN",
  "RealAddress": "$REAL_ADDR",
  "VirtualAddress": "$VIRT_IP",
  "ConnectedSince": "${CS_ISO}",
  "DurationSec": ${DUR:-0},
  "DisconnectedAt": "${DSC_ISO}",
  "BytesReceived": ${BYTES_IN:-0},
  "BytesSent": ${BYTES_OUT:-0}
}
JSON
)" >/dev/null 2>&1
  ) &
}

post_bg
exit 0