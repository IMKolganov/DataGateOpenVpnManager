#!/bin/sh
# Watches OpenVPN log for AUTH_FAILED and posts events to backend.

set -eu

LOG_FILE="/var/log/openvpn.log"
API_BASE="http://127.0.0.1:__API_PORT__"

# JSON-safe sanitizer (escape quotes/backslashes, strip newlines)
sanitize() {
  # shellcheck disable=SC2001
  printf "%s" "${1:-}" \
    | tr '\n' ' ' \
    | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'
}

iso_now() {
  date -u +"%Y-%m-%dT%H:%M:%SZ"
}

post_bg() {
  (
    curl -sS --max-time 2 \
      -H "Content-Type: application/json" \
      -X POST "$API_BASE/$1" \
      -d "$2" >/dev/null 2>&1
  ) &
}

# Feature-detect tail -F (GNU) vs -f (BusyBox)
TAIL_F_OPT="-f"
if tail -F /dev/null >/dev/null 2>&1; then
  TAIL_F_OPT="-F"
fi

# Start from end of file to avoid replaying old lines
# Ensure file exists (create empty if missing)
[ -e "$LOG_FILE" ] || : > "$LOG_FILE"

tail -n0 "$TAIL_F_OPT" "$LOG_FILE" | while IFS= read -r line; do
  case "$line" in
    *AUTH_FAILED*)
      # Try to extract CN='...'; fallback patterns handled by sed
      cn="$(printf "%s" "$line" \
        | sed -n "s/.*CN='\([^']*\)'.*/\1/p")"

      # Fallback: sometimes format is CN=..., no quotes
      if [ -z "${cn:-}" ]; then
        cn="$(printf "%s" "$line" \
          | sed -n 's/.*CN=\([^ ,;]*\).*/\1/p')"
      fi

      s_line="$(sanitize "$line")"
      s_cn="$(sanitize "$cn")"

      post_bg "api/vpnEvent/fail" "$(cat <<JSON
{
  "CommonName":"$s_cn",
  "Message":"$s_line",
  "Timestamp":"$(iso_now)"
}
JSON
)"
      ;;
  esac
done
