#!/bin/bash

# This script is called by OpenVPN when a client disconnects.
# It sends a POST request to the backend API with disconnection details:
# - $common_name: the client's certificate common name
# - $untrusted_ip:$untrusted_port: the real IP/port of the client
# - $ifconfig_pool_local: the VPN IP address that was assigned
# - current UTC timestamp as the disconnection time

set -eu

# Helpers
json_post_bg() {
  # $1: endpoint path, $2: json payload
  (curl -sS --max-time 2 -H "Content-Type: application/json" -X POST "http://localhost:__API_PORT__/$1" -d "$2" >/dev/null 2>&1) &
}

b64_env() {
  # BusyBox base64 has no -w; GNU has -w0. Try GNU first, fallback to BusyBox.
  if base64 --help 2>/dev/null | grep -q -- "--wrap"; then
    env | base64 -w0
  else
    env | base64
  fi
}

iso_now() {
  date -u +"%Y-%m-%dT%H:%M:%SZ"
}

# Prefer the actual client virtual IP in subnet topology
virtual_ip="${ifconfig_pool_remote_ip:-${ifconfig_pool_local:-}}"

# Send main event
json_post_bg "api/vpnEvent/connect" "$(cat <<JSON
{
  "CommonName": "${common_name:-}",
  "RealAddress": "${untrusted_ip:-}:${untrusted_port:-}",
  "VirtualAddress": "${virtual_ip:-}",
  "Timestamp": "$(iso_now)"
}
JSON
)"

# Send environment dump
json_post_bg "api/vpnEvent/envdump" "$(cat <<JSON
{
  "Hook": "client-connect",
  "Timestamp": "$(iso_now)",
  "Args": [],
  "EnvB64": "$(b64_env)"
}
JSON
)"
