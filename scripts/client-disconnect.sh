#!/bin/bash

# This script is called by OpenVPN when a client disconnects.
# It sends a POST request to the backend API with disconnection details:
# - $common_name: the client's certificate common name
# - $untrusted_ip:$untrusted_port: the real IP/port of the client
# - $ifconfig_pool_local: the VPN IP address that was assigned
# - current UTC timestamp as the disconnection time

curl -X POST http://localhost:__API_PORT__/api/vpnEvent/disconnect \
  -H "Content-Type: application/json" \
  -d "{
    \"CommonName\": \"$common_name\",
    \"RealAddress\": \"$untrusted_ip:$untrusted_port\",
    \"VirtualAddress\": \"$ifconfig_pool_local\",
    \"ConnectedSince\": \"$(date -u +"%Y-%m-%dT%H:%M:%SZ")\"
  }" >/dev/null 2>&1 &
