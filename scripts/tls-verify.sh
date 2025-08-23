#!/bin/bash

# This script is triggered by OpenVPN's --tls-verify directive.
# It receives the certificate depth and common name (CN) of the client being verified.
# The script sends this information to the backend API for logging or auditing purposes.

# OpenVPN hook: tls-verify

set -e  # no -u !

API_BASE="http://${API_HOST:-127.0.0.1}:${API_PORT:-5010}"

depth="${1:-}"          # tls-verify depth (0 = peer cert)
subject="${2:-}"        # full subject, if provided

post_bg() {
  (
    curl -sS --max-time 2 -H "Content-Type: application/json" \
      -X POST "$API_BASE/api/vpnEvent/tlsverify" -d "$1" >/dev/null 2>&1
  ) &
}

sanitize() {
  printf "%s" "${1:-}" | tr '\n' ' ' | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'
}

# On depth != 0 do nothing but allow handshake
if [ "${depth:-}" != "0" ]; then
  exit 0
fi

# Extract CN safely (try env first, then parse)
cn="${X509_0_CN:-}"
[ -z "$cn" ] && cn="${tls_id_0#CN=}"
if [ -z "$cn" ] && [ -n "$subject" ]; then
  # subject like: /CN=client1/...
  cn="$(printf "%s" "$subject" | sed -n 's#.*/CN=\([^/]*\).*#\1#p')"
fi

real_addr="$(sanitize "${untrusted_ip:-}:${untrusted_port:-}")"
cn_s="$(sanitize "$cn")"

# Send event (non-blocking, even if CN empty — это просто телеметрия)
post_bg "$(cat <<JSON
{
  "EventType": "TlsVerified",
  "ScriptType": "tls-verify",
  "CommonName": "$cn_s",
  "RealAddress": "$real_addr"
}
JSON
)"

exit 0