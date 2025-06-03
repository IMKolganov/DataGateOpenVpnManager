#!/bin/bash

curl -X POST http://localhost:__API_PORT__/api/vpnEvent/disconnect \
  -H "Content-Type: application/json" \
  -d "{
    \"CommonName\": \"$common_name\",
    \"RealAddress\": \"$untrusted_ip:$untrusted_port\",
    \"VirtualAddress\": \"$ifconfig_pool_local\",
    \"ConnectedSince\": \"$(date -u +"%Y-%m-%dT%H:%M:%SZ")\"
  }" >/dev/null 2>&1 &
