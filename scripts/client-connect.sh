#!/bin/bash

# This script is called by OpenVPN when a client successfully connects.
# It sends a POST request to the backend API with connection details:
# - $common_name: the client's certificate common name
# - $untrusted_ip:$untrusted_port: the real IP/port of the client
# - $ifconfig_pool_local: the assigned VPN IP address

# OpenVPN hook: client-connect

set -eu

API_BASE="http://${API_HOST:-127.0.0.1}:${API_PORT:-5010}"

sanitize() {
  printf "%s" "${1:-}" | tr '\n' ' ' | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'
}

# time_unix is "session start" (seconds since epoch)
unix_to_iso() {
  # Prefer date -d @.. (GNU/BusyBox new); fallback to time_ascii -> ISO
  if date -u -d "@$1" +"%Y-%m-%dT%H:%M:%SZ" >/dev/null 2>&1; then
    date -u -d "@$1" +"%Y-%m-%dT%H:%M:%SZ"
  else
    # time_ascii like "2025-08-14 12:52:29"
    printf "%sZ" "$(printf "%s" "${time_ascii:-}" | sed 's/ /T/')"
  fi
}

CN="$(sanitize "${common_name:-}")"
REAL_ADDR="$(sanitize "${untrusted_ip:-}:${untrusted_port:-}")"
VIRT_IP="$(sanitize "${ifconfig_pool_remote_ip:-}")"
SCRIPT_TYPE="$(sanitize "${script_type:-client-connect}")"
IV_VER_S="$(sanitize "${IV_VER:-}")"
IV_GUI_VER_S="$(sanitize "${IV_GUI_VER:-}")"
IV_PLAT_S="$(sanitize "${IV_PLAT:-}")"

CS_ISO=""
if [ -n "${time_unix:-}" ]; then
  CS_ISO="$(unix_to_iso "${time_unix}")"
fi

post_bg() {
  (
    curl -sS --max-time 2 -H "Content-Type: application/json" \
      -X POST "$API_BASE/api/vpn-events/connect" \
      -d "$(cat <<JSON
{
  "EventType": "ClientConnect",
  "ScriptType": "$SCRIPT_TYPE",
  "CommonName": "$CN",
  "RealAddress": "$REAL_ADDR",
  "VirtualAddress": "$VIRT_IP",
  "ConnectedSince": "${CS_ISO}",
  "IvVer": "$IV_VER_S",
  "IvGuiVer": "$IV_GUI_VER_S",
  "IvPlat": "$IV_PLAT_S"
}
JSON
)" >/dev/null 2>&1
  ) &
}

post_bg
exit 0