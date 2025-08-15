#!/bin/bash
# Watch OpenVPN logs for errors/failures and post classified events to backend

set -eu

# ---- config ----
API_HOST="${API_HOST:-127.0.0.1}"
API_PORT="${API_PORT:-5010}"
API_URL="http://${API_HOST}:${API_PORT}/api/vpnEvent/error"
VPN_SERVER_ID="${VPN_SERVER_ID:-0}"
LOG_FILE="${LOG_FILE:-/var/log/openvpn.log}"

# Dedup window (seconds) to avoid spamming repeated errors
DEDUP_SEC="${DEDUP_SEC:-5}"

declare -A LAST_SEEN  # key -> epoch

sanitize() { printf "%s" "${1:-}" | tr '\n' ' ' | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'; }
iso_now()   { date -u +"%Y-%m-%dT%H:%M:%SZ"; }
now_epoch() { date +%s; }

post_bg() {
  (curl -sS --max-time 2 \
     -H "Content-Type: application/json" \
     -X POST "$API_URL" \
     -d "$1" >/dev/null 2>&1) &
}

classify() {
  local lcase="$1"
  local et="Error"
  if   grep -qEi "AUTH[_ ]?FAIL(ED)?" <<<"$lcase";             then et="AuthFailed"
  elif grep -qE  "VERIFY ERROR"        <<<"$1";                 then et="VerifyError"
  elif grep -qE  "TLS Error"           <<<"$1";                 then et="TlsError"
  elif grep -qiE "tls-crypt unwrap|tls-crypt unwrapp|packet authentication failed" <<<"$lcase"; then et="TlsCryptError"
  fi
  echo "$et"
}

extract_cn() {
  local line="$1" cn=""
  cn="$(sed -n "s/.*CN='\([^']*\)'.*/\1/p" <<<"$line")" || true
  [[ -z "$cn" ]] && cn="$(sed -n 's/.*CN=\([^ ,;]*\).*/\1/p' <<<"$line")" || true
  echo "$cn"
}

extract_peer() {
  local line="$1" peer=""
  # from [AF_INET]1.2.3.4:5678
  peer="$(sed -n 's/.*\[AF_INET6\]\([^ ]*\).*/\1/p' <<<"$line")" || true
  [[ -z "$peer" ]] && peer="$(sed -n 's/.*\[AF_INET\]\([^ ]*\).*/\1/p' <<<"$line")" || true
  # or "... from 1.2.3.4:5678"
  [[ -z "$peer" ]] && peer="$(sed -n 's/.* from \([0-9a-fA-F:\.]\+:[0-9]\+\).*/\1/p' <<<"$line")" || true
  echo "$peer"
}

should_send() {
  local key="$1"
  local now
  now="$(now_epoch)"
  local last="${LAST_SEEN[$key]:-0}"
  if (( now - last < DEDUP_SEC )); then
    return 1
  fi
  LAST_SEEN["$key"]="$now"
  return 0
}

# follow log (handle rotation if possible)
TAIL_OPT="-f"; tail -F /dev/null >/dev/null 2>&1 && TAIL_OPT="-F"
[ -e "$LOG_FILE" ] || : > "$LOG_FILE"

tail -n0 "$TAIL_OPT" "$LOG_FILE" | while IFS= read -r line; do
  # fast filter
  if ! echo "$line" | grep -qiE "fail|error"; then
    continue
  fi

  lcase="$(tr 'A-Z' 'a-z' <<<"$line")"
  et="$(classify "$lcase")"
  cn="$(extract_cn "$line")"
  peer="$(extract_peer "$line")"

  # dedupe key: server + type + peer + CN (на 5 сек)
  key="${VPN_SERVER_ID}|${et}|${peer}|${cn}"
  if ! should_send "$key"; then
    continue
  fi

  payload="$(cat <<JSON
{
  "VpnServerId": ${VPN_SERVER_ID},
  "EventType": "${et}",
  "ScriptType": "log-watcher",
  "CommonName": "$(sanitize "$cn")",
  "RealAddress": "$(sanitize "$peer")",
  "Message": "$(sanitize "$line")",
  "EventTimeUtc": "$(iso_now)"
}
JSON
)"
  post_bg "$payload"
done
