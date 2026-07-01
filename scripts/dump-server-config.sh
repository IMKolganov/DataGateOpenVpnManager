#!/usr/bin/env bash
# Dump nginx + VPN stack configs for cross-server diff.
# Usage on each host: bash dump-server-config.sh [output-file]
set -euo pipefail

out="${1:-}"
if [[ -n "$out" ]]; then
  exec >"$out"
fi

section() { echo; echo "===== $1 ====="; }

section "HOST"
echo "hostname: $(hostname -f 2>/dev/null || hostname)"
echo "date: $(date -Is)"
echo "user: $(whoami)"

section "DOCKER PS (nginx/openvpn/xray)"
docker ps --format 'table {{.Names}}\t{{.Image}}\t{{.Ports}}' 2>/dev/null \
  | grep -E 'NAMES|nginx|openvpn|xray|telegram' || echo "(no matching containers)"

for dir in "$HOME/nginx-docker" "$HOME/openvpn-tcp-wss" "$HOME/openvpn-udp-wss" "$HOME/datagate-monitor-xray"; do
  [[ -d "$dir" ]] || continue
  base="$(basename "$dir")"
  section "$base/docker-compose.yml"
  if [[ -f "$dir/docker-compose.yml" ]]; then
    cat "$dir/docker-compose.yml"
  else
    echo "(missing)"
  fi
  if [[ -f "$dir/.env" ]]; then
    section "$base/.env"
    cat "$dir/.env"
  fi
done

if [[ -d "$HOME/nginx-docker/nginx/conf.d" ]]; then
  section "nginx/conf.d"
  for f in "$HOME/nginx-docker/nginx/conf.d"/*; do
    [[ -f "$f" ]] || continue
    echo "--- file: $(basename "$f") ---"
    cat "$f"
  done
fi

section "PROXY_PASS SUMMARY"
grep -rn 'proxy_pass\|server_name\|network_mode\|5011\|5010\|5009' \
  "$HOME/nginx-docker" 2>/dev/null \
  | grep -v '/certbot/conf/archive' \
  | grep -v '/certbot/conf/live' \
  | grep -v '/certbot/conf/accounts' \
  || echo "(none)"

section "OPENVPN TCP-WSS PORT MAP"
if [[ -f "$HOME/openvpn-tcp-wss/docker-compose.yml" ]]; then
  grep -nE 'ports:|API_PORT|5011|1297|network_mode' "$HOME/openvpn-tcp-wss/docker-compose.yml" || true
fi

section "OPENVPN UDP-WSS PORT MAP"
if [[ -f "$HOME/openvpn-udp-wss/docker-compose.yml" ]]; then
  grep -nE 'ports:|API_PORT|network_mode' "$HOME/openvpn-udp-wss/docker-compose.yml" || true
fi
