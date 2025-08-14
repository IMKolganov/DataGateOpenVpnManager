#!/bin/bash

# learn-address hook
# This script is called by OpenVPN via the `learn-address` directive.
# It handles client connection *attempts*, including those rejected by tls-verify.
# Only the "add" action is used here to log an attempted assignment of a VPN IP.
# Parameters:
#   $1 action: add|update|delete
#   $2 address: client's virtual VPN IP
#   $3 common_name: client's certificate CN

set -eu

action="${1:-}"
address="${2:-}"
cn="${3:-}"

# Minimal JSON-safe sanitizer (strip newlines/quotes)
sanitize() {
  # shellcheck disable=SC2001
  echo "${1:-}" | tr '\n' ' ' | sed 's/"/\\"/g'
}

# Feature test for base64 -w0 (GNU) vs BusyBox
b64() {
  if printf x | base64 -w0 >/dev/null 2>&1; then
    env | base64 -w0
  else
    env | base64
  fi
}

iso_now() {
  date -u +"%Y-%m-%dT%H:%M:%SZ"
}

post_bg() {
  (
    curl -sS --max-time 2 \
      -H "Content-Type: application/json" \
      -X POST "http://127.0.0.1:__API_PORT__/$1" \
      -d "$2" >/dev/null 2>&1
  ) &
}

# Send event only for 'add' (route assigned)
if [ "$action" = "add" ]; then
  s_cn=$(sanitize "$cn")
  s_addr=$(sanitize "$address")
  post_bg "api/vpnEvent/attempt" "$(cat <<JSON
{
  "CommonName":"$s_cn",
  "VirtualAddress":"$s_addr",
  "Timestamp":"$(iso_now)"
}
JSON
)"
fi

# Always send env dump for diagnostics
post_bg "api/vpnEvent/envdump" "$(cat <<JSON
{
  "Hook":"learn-address",
  "Timestamp":"$(iso_now)",
  "Args":["$(sanitize "$action")","$(sanitize "$address")","$(sanitize "$cn")"],
  "EnvB64":"$(b64)"
}
JSON
)"
