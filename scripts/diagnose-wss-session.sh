#!/usr/bin/env bash
# One-shot investigation for udp-wss / tcp-wss proxy + OpenVPN + Pi-hole.
# Run on the VPN host (e.g. dg-telegrambot):
#   ./scripts/diagnose-wss-session.sh
#   ./scripts/diagnose-wss-session.sh openvpn-udp-wss 5078 5012 datagate-pihole

set -u

CONTAINER="${1:-openvpn-udp-wss}"
MGMT_PORT="${2:-5078}"
API_PORT="${3:-5012}"
PIHOLE="${4:-datagate-pihole}"

section() {
  echo
  echo "===== $1 ====="
}

mgmt_status3() {
  ( printf 'status 3\n'; sleep 1; printf 'quit\n' ) | nc -q2 127.0.0.1 "$MGMT_PORT" 2>/dev/null
}

section "1 APP VERSION"
if curl -sf "http://127.0.0.1:${API_PORT}/api/info" >/tmp/diag-info.json 2>/dev/null; then
  grep -oE '"version":"[^"]*"|"Version":"[^"]*"' /tmp/diag-info.json | head -3 || cat /tmp/diag-info.json
else
  echo "FAIL: API http://127.0.0.1:${API_PORT}/api/info"
fi

section "2 OPENVPN MANAGEMENT + PROXY STATE"
echo "  curl -s http://127.0.0.1:${API_PORT}/api/diagnostics/proxy-sessions | python3 -m json.tool"
echo "  curl -s 'http://127.0.0.1:${API_PORT}/api/diagnostics/proxy-audit?limit=50' | python3 -m json.tool"
if curl -sf "http://127.0.0.1:${API_PORT}/api/diagnostics/proxy-sessions" | head -c 2000; then
  echo
else
  echo "proxy-sessions API unavailable (401 until image >=1.2.5.65; use docker logs ProxyAudit below)"
fi
echo
echo "--- last audit events ---"
if curl -sf "http://127.0.0.1:${API_PORT}/api/diagnostics/proxy-audit?limit=20" | head -c 4000; then
  echo
else
  echo "proxy-audit API unavailable — fallback:"
  docker logs "$CONTAINER" 2>&1 | grep '\[ProxyAudit\]' | tail -20 || true
fi

section "3 PROXY LOGS (last 20 ProxyAudit / ProxyByteDebug / ProxyZombie)"
docker logs "$CONTAINER" 2>&1 | grep -E '\[ProxyAudit\]|ProxyByteDebug|ProxyZombie' | tail -20 || true

section "4 OPENVPN LOG — recent disconnect / timeout / ping"
docker exec "$CONTAINER" sh -c \
  'grep -iE "Inactivity timeout|ping-restart|SIGUSR1|Connection reset|MULTI|exit|AUTH FAILED|TLS Error" /openvpn-udp-wss/openvpn.log 2>/dev/null | tail -25' \
  || echo "(no openvpn.log lines)"

section "5 DNS — host → Pi-hole @10.51.15.1"
for name in youtube.com googlevideo.com connectivitycheck.gstatic.com; do
  result="$(dig @"10.51.15.1" "$name" +time=2 +tries=1 +short 2>/dev/null | head -2 | tr '\n' ' ')"
  if [ -n "$result" ]; then
    echo "OK  $name -> $result"
  else
    echo "FAIL $name (no answer from 10.51.15.1 within 2s)"
  fi
done

section "6 L3 — tun1 gateway → tun0 Pi-hole IP"
if ip -4 addr show dev tun1 2>/dev/null | grep -q 'inet '; then
  TUN1_GW="$(ip -4 addr show dev tun1 | awk '/inet / {print $2}' | cut -d/ -f1 | head -1)"
  ping -c 2 -W 1 -I "$TUN1_GW" 10.51.15.1 2>&1 | tail -3
else
  echo "SKIP: tun1 has no IPv4 on host"
fi

section "7 PI-HOLE — container health + recent 10.51.16 queries"
if docker ps --format '{{.Names}}' | grep -qx "$PIHOLE"; then
  docker inspect "$PIHOLE" --format 'status={{.State.Status}} health={{if .State.Health}}{{.State.Health.Status}}{{else}}n/a{{end}}'
  docker logs "$PIHOLE" 2>&1 | grep -E '10\.51\.16\.' | tail -8 || \
    docker exec "$PIHOLE" sh -c 'grep "10.51.16" /var/log/pihole/pihole.log 2>/dev/null | tail -8' || \
    echo "(no 10.51.16 queries in recent pihole logs)"
else
  echo "FAIL: container $PIHOLE not running"
fi

section "8 ROUTING / NAT (udp-wss container view)"
docker exec "$CONTAINER" sh -c 'grep -E "dhcp-option DNS|mssfix" /openvpn-udp-wss/server.conf; echo ---; ip route | grep tun; iptables -t nat -S POSTROUTING | grep 10.51.16' 2>/dev/null

section "9 ZOMBIE CHECK — proxy local ports vs management RealAddress"
CLIENT_LINES="$(mgmt_status3 | grep -E '^CLIENT_LIST,' || true)"
MGMT_PORTS="$(echo "$CLIENT_LINES" | awk -F'\t' '{print $3}' | grep -oE '[0-9]+$' | sort -u)"
PROXY_PORTS="$(docker logs "$CONTAINER" 2>&1 | grep -E '\[ProxyAudit\].*proxy\.connected|\[ProxyAudit\].*local=' \
  | grep -oE 'local=127\.0\.0\.1:[0-9]+' | tail -5 | sed 's/local=127.0.0.1://' | sort -u)"
if [ -z "$PROXY_PORTS" ]; then
  PROXY_PORTS="$(docker logs "$CONTAINER" 2>&1 | grep ProxyByteDebug | grep -oE 'local=127\.0\.0\.1:[0-9]+' \
    | tail -5 | sed 's/local=127.0.0.1://' | sort -u)"
fi

if [ -z "$PROXY_PORTS" ]; then
  echo "No proxy local ports in recent logs"
else
  for p in $PROXY_PORTS; do
    if echo "$MGMT_PORTS" | grep -qx "$p"; then
      echo "LIVE   proxy port $p — present in management"
    else
      echo "ZOMBIE proxy port $p — NOT in management (WSS likely up, OpenVPN dead)"
    fi
  done
fi

section "10 VERDICT"
LAST_AUDIT="$(docker logs "$CONTAINER" 2>&1 | grep '\[ProxyAudit\]' | tail -1)"
LAST_BYTE="$(docker logs "$CONTAINER" 2>&1 | grep ProxyByteDebug | tail -1)"
echo "Last ProxyAudit: ${LAST_AUDIT:-<none>}"
echo "Last ProxyByteDebug: ${LAST_BYTE:-<none>}"

if echo "$LAST_AUDIT" | grep -q 'proxy.terminated'; then
  echo ">>> SESSION ENDED: proxy terminated — reconnect client."
elif echo "$LAST_AUDIT $LAST_BYTE" | grep -q 'client not in management'; then
  echo ">>> ZOMBIE SESSION: OpenVPN peer gone, WSS proxy still registered."
elif echo "$LAST_AUDIT" | grep -q 'mgmtClients=.*127.0.0.1'; then
  echo ">>> LIVE SESSION on server: OpenVPN + proxy registered. If client has no internet,"
  echo ">>> run sections 5–7 while connected (DNS from 10.51.16.x, Pi-hole queries, tun counters)."
elif echo "$LAST_BYTE" | grep -q 'mgmtRecv='; then
  echo ">>> LIVE SESSION: proxy bytes match management. If user still reports hangs, check sections 5–7 (DNS/Pi-hole)."
else
  echo ">>> No recent proxy audit — connect VPN and re-run."
fi

if docker logs "$CONTAINER" 2>&1 | grep -q ProxyZombie; then
  echo ">>> ProxyZombie killer is active in this image."
else
  echo ">>> No ProxyZombie logs yet — either no zombie >=90s or image without zombie killer."
fi
