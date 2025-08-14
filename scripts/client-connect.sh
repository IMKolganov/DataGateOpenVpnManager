#!/bin/bash

# This script is called by OpenVPN when a client successfully connects.
# It sends a POST request to the backend API with connection details:
# - $common_name: the client's certificate common name
# - $untrusted_ip:$untrusted_port: the real IP/port of the client
# - $ifconfig_pool_local: the assigned VPN IP address

set -eu

json_post_bg() {
  (curl -sS --max-time 2 -H "Content-Type: application/json" -X POST "http://localhost:__API_PORT__/api/vpnEvent/disconnect" -d "$1" >/dev/null 2>&1) &
}

json_post_envdump() {
  (curl -sS --max-time 2 -H "Content-Type: application/json" -X POST "http://localhost:__API_PORT__/api/vpnEvent/envdump" -d "$1" >/dev/null 2>&1) &
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

virtual_ip="${ifconfig_pool_remote_ip:-${ifconfig_pool_local:-}}"

json_post_bg "$(cat <<JSON
{
  "CommonName": "${common_name:-}",
  "RealAddress": "${untrusted_ip:-}:${untrusted_port:-}",
  "VirtualAddress": "${virtual_ip:-}",
  "DisconnectedAt": "$(iso_now)"
}
JSON
)"

json_post_envdump "$(cat <<JSON
{
  "Hook": "client-disconnect",
  "Timestamp": "$(iso_now)",
  "Args": [],
  "EnvB64": "$(b64_env)"
}
JSON
)"
