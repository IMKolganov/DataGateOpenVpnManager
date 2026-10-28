#!/bin/bash
# Node-local OpenVPN profile issuance (LOCAL_ISSUANCE_ENABLED=true). Talks to DataGateOpenVpnManager over its
# root-only admin Unix socket; run inside the container:
#   docker exec <container> datagate-profile create upcloud-db1 --service --static-ip 10.50.28.201 > db1.ovpn
set -euo pipefail

SOCK="${LOCAL_ADMIN_SOCKET:-${LOCAL_ISSUANCE_DIR:-${DATA_DIR:-/mnt}/local-issuance}/admin.sock}"
BASE="http://localhost/api/local-admin"

usage() {
  cat >&2 <<USAGE
Usage:
  datagate-profile create <CN> [--service] [--static-ip IP] [--remote HOST] [--port N] [--days N] [-o FILE]
      Issue a profile and print the .ovpn to stdout (or write it to FILE, mode 0600).
      --service    only VPN-internal traffic in the tunnel (pull-filter redirect-gateway/dhcp-option/block-outside-dns)
      --static-ip  permanent tunnel address (needs CLIENT_CONFIG_DIR on the node)
  datagate-profile get <CN> [-o FILE]     Print the .ovpn of an active locally issued profile.
  datagate-profile revoke <CN>            Revoke a locally issued profile.
  datagate-profile list [--json]          All certificates on this node with origin (local/dashboard).
  datagate-profile node-id                Node key fingerprint (compare with the dashboard before approving).
USAGE
  exit 2
}

die() { echo "datagate-profile: $*" >&2; exit 1; }

json_string() {
  local value="$1"
  if [[ "$value" =~ [[:cntrl:]] ]]; then die "control characters are not allowed: $value"; fi
  value="${value//\\/\\\\}"
  value="${value//\"/\\\"}"
  printf '"%s"' "$value"
}

# request METHOD PATH [JSON_BODY] -> body on stdout; exits non-zero on HTTP error
request() {
  local method="$1" path="$2" body="${3:-}" out code
  [ -S "$SOCK" ] || die "admin socket $SOCK not found (is LOCAL_ISSUANCE_ENABLED=true and are you root in the container?)"
  out="$(mktemp)"
  trap 'rm -f "$out"' RETURN
  if [ -n "$body" ]; then
    code="$(curl -sS --unix-socket "$SOCK" -o "$out" -w '%{http_code}' -X "$method" \
      -H 'Content-Type: application/json' --data "$body" "$BASE$path")" || die "request to $SOCK failed"
  else
    code="$(curl -sS --unix-socket "$SOCK" -o "$out" -w '%{http_code}' -X "$method" "$BASE$path")" \
      || die "request to $SOCK failed"
  fi
  if [ "$code" -lt 200 ] || [ "$code" -ge 300 ]; then
    echo "datagate-profile: HTTP $code" >&2
    cat "$out" >&2; echo >&2
    exit 1
  fi
  cat "$out"
}

emit() {
  local content="$1" file="$2"
  if [ -n "$file" ]; then
    (umask 077; printf '%s\n' "$content" > "$file")
    echo "datagate-profile: wrote $file" >&2
  else
    printf '%s\n' "$content"
  fi
}

[ $# -ge 1 ] || usage
cmd="$1"; shift

case "$cmd" in
  create)
    [ $# -ge 1 ] || usage
    cn="$1"; shift
    service=false; static_ip=""; remote=""; port=""; days=""; file=""
    while [ $# -gt 0 ]; do
      case "$1" in
        --service) service=true ;;
        --static-ip) static_ip="${2:?}"; shift ;;
        --remote) remote="${2:?}"; shift ;;
        --port) port="${2:?}"; shift ;;
        --days) days="${2:?}"; shift ;;
        -o|--output) file="${2:?}"; shift ;;
        *) usage ;;
      esac
      shift
    done
    [[ -z "$port" || "$port" =~ ^[0-9]+$ ]] || die "--port must be a number"
    [[ -z "$days" || "$days" =~ ^[0-9]+$ ]] || die "--days must be a number"
    body="{\"CommonName\":$(json_string "$cn"),\"IsServiceProfile\":$service"
    [ -n "$static_ip" ] && body+=",\"StaticIp\":$(json_string "$static_ip")"
    [ -n "$remote" ] && body+=",\"Remote\":$(json_string "$remote")"
    [ -n "$port" ] && body+=",\"Port\":$port"
    [ -n "$days" ] && body+=",\"ExpireDays\":$days"
    body+="}"
    request POST /profiles "$body" >&2; echo >&2
    # request exits inside $(...) only end the subshell: keep its status, never write an empty profile.
    content="$(request GET "/profiles/$cn/ovpn")" || exit 1
    emit "$content" "$file"
    ;;
  get)
    [ $# -ge 1 ] || usage
    cn="$1"; shift; file=""
    if [ "${1:-}" = "-o" ] || [ "${1:-}" = "--output" ]; then file="${2:?}"; fi
    content="$(request GET "/profiles/$cn/ovpn")" || exit 1
    emit "$content" "$file"
    ;;
  revoke)
    [ $# -eq 1 ] || usage
    request POST "/profiles/$1/revoke"; echo
    ;;
  list)
    if [ "${1:-}" = "--json" ]; then request GET /profiles; echo; else request GET "/profiles?format=text"; fi
    ;;
  node-id)
    request GET /node-identity; echo
    ;;
  *) usage ;;
esac
