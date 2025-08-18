#!/bin/sh
# Watch OpenVPN log for AUTH_FAILED and post /authfail

set -eu

LOG_FILE="${OVPN_LOG_FILE:-/var/log/openvpn.log}"
API_BASE="http://127.0.0.1:${API_PORT:-5010}"

sanitize() {
  printf "%s" "${1:-}" | tr '\n' ' ' | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'
}

post_bg() {
  (
    curl -sS --max-time 2 -H "Content-Type: application/json" \
      -X POST "$API_BASE/api/vpnEvent/authfail" \
      -d "$1" >/dev/null 2>&1
  ) &
}

TAIL_OPT="-f"; tail -F /dev/null >/dev/null 2>&1 && TAIL_OPT="-F"
[ -e "$LOG_FILE" ] || : > "$LOG_FILE"

tail -n0 "$TAIL_OPT" "$LOG_FILE" | while IFS= read -r line; do
  case "$line" in
    *AUTH_FAILED*)
      cn="$(printf "%s" "$line" | sed -n "s/.*CN='\([^']*\)'.*/\1/p")"
      [ -z "${cn:-}" ] && cn="$(printf "%s" "$line" | sed -n 's/.*CN=\([^ ,;]*\).*/\1/p')"
      s_line="$(sanitize "$line")"
      s_cn="$(sanitize "$cn")"
      post_bg "$(cat <<JSON
{
  "VpnServerId": ${VPN_SERVER_ID:-0},
  "EventType": "AuthFailed",
  "ScriptType": "log-watcher",
  "CommonName": "$s_cn",
  "Message": "$s_line"
}
JSON
)"
      ;;
  esac
done
