#!/bin/bash

# This script is called by OpenVPN via the `learn-address` directive.
# It handles client connection *attempts*, including those rejected by tls-verify.
# Only the "add" action is used here to log an attempted assignment of a VPN IP.
# Parameters:
# - $1 (action): e.g., "add", "delete", "update"
# - $2 (address): the virtual VPN IP assigned (or attempted)
# - $3 (common_name): the client's certificate common name
set -eu

action="${1:-}"
address="${2:-}"
cn="${3:-}"

json_post_bg() {
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

# Main event for 'add' only
if [ "$action" = "add" ]; then
  json_post_bg "api/vpnEvent/attempt" "$(cat <<JSON
{
  "CommonName": "$cn",
  "VirtualAddress": "$address",
  "Timestamp": "$(iso_now)"
}
JSON
)"
fi

# Always send env dump for diagnostics
json_post_bg "api/vpnEvent/envdump" "$(cat <<JSON
{
  "Hook": "learn-address",
  "Timestamp": "$(iso_now)",
  "Args": ["$action","$address","$cn"],
  "EnvB64": "$(b64_env)"
}
JSON
)"
