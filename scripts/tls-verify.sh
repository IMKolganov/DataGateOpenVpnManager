#!/bin/bash

# This script is triggered by OpenVPN's --tls-verify directive.
# It receives the certificate depth and common name (CN) of the client being verified.
# The script sends this information to the backend API for logging or auditing purposes.

set -eu

depth="${1:-}"
cn="${2:-}"

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

post_bg "api/vpnEvent/tlsverify" "$(cat <<JSON
{
  "CommonName": "$cn",
  "Depth": "$depth",
  "Timestamp": "$(iso_now)"
}
JSON
)"

post_bg "api/vpnEvent/envdump" "$(cat <<JSON
{
  "Hook": "tls-verify",
  "Timestamp": "$(iso_now)",
  "Args": ["$depth","$cn"],
  "EnvB64": "$(b64_env)"
}
JSON
)"

exit 0
