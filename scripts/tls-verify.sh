#!/bin/bash

# This script is triggered by OpenVPN's --tls-verify directive.
# It receives the certificate depth and common name (CN) of the client being verified.
# The script sends this information to the backend API for logging or auditing purposes.

# OpenVPN hook: tls-verify

set -eu

API_BASE="http://127.0.0.1:${API_PORT:-5010}"

# JSON-safe sanitizer
sanitize() {
  printf "%s" "${1:-}" | tr '\n' ' ' | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'
}

# Try to get CN from tls_id_0 or X509_0_CN
CN="${tls_id_0#CN=}"
[ -n "${X509_0_CN:-}" ] && CN="${X509_0_CN}"

REAL_ADDR="$(sanitize "${untrusted_ip:-}:${untrusted_port:-}")"
COMMON_NAME="$(sanitize "${CN:-}")"
SCRIPT_TYPE="$(sanitize "${script_type:-tls-verify}")"
MSG="$(sanitize "tls-verify ok")"

post_bg() {
  (
    curl -sS --max-time 2 -H "Content-Type: application/json" \
      -X POST "$API_BASE/api/vpnEvent/tlsverify" \
      -d "{\"VpnServerId\":${VPN_SERVER_ID:-0},\"EventType\":\"TlsVerified\",\"ScriptType\":\"$SCRIPT_TYPE\",\"CommonName\":\"$COMMON_NAME\",\"RealAddress\":\"$REAL_ADDR\",\"Message\":\"$MSG\"}" >/dev/null 2>&1
  ) &
}

post_bg
exit 0