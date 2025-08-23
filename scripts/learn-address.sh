#!/bin/bash

# learn-address hook
# This script is called by OpenVPN via the `learn-address` directive.
# It handles client connection *attempts*, including those rejected by tls-verify.
# Only the "add" action is used here to log an attempted assignment of a VPN IP.
# Parameters:
#   $1 action: add|update|delete
#   $2 address: client's virtual VPN IP
#   $3 common_name: client's certificate CN

# OpenVPN hook: learn-address

set -eu

API_BASE="http://${API_HOST:-127.0.0.1}:${API_PORT:-5010}"

action="${1:-}"
address="${2:-}"
cnp="${3:-}"

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

[ "$action" = "add" ] || exit 0

CN="$(sanitize "${cnp:-${common_name:-}}")"
REAL_ADDR="$(sanitize "${untrusted_ip:-}:${untrusted_port:-}")"
VIRT_IP="$(sanitize "${address:-${ifconfig_pool_remote_ip:-}}")"
SCRIPT_TYPE="learn-address"
CS_ISO=""
[ -n "${time_unix:-}" ] && CS_ISO="$(unix_to_iso "${time_unix}")"

post_bg() {
  (
    curl -sS --max-time 2 -H "Content-Type: application/json" \
      -X POST "$API_BASE/api/vpnEvent/attempt" \
      -d "$(cat <<JSON
{
  "EventType": "LearnAdd",
  "ScriptType": "$SCRIPT_TYPE",
  "Action": "add",
  "CommonName": "$CN",
  "RealAddress": "$REAL_ADDR",
  "VirtualAddress": "$VIRT_IP",
  "ConnectedSince": "${CS_ISO}"
}
JSON
)" >/dev/null 2>&1
  ) &
}

post_bg
exit 0
