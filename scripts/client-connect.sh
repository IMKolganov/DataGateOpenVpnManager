#!/bin/bash

# This script is called by OpenVPN when a client successfully connects.
# It sends a POST request to the backend API with connection details:
# - $common_name: the client's certificate common name
# - $untrusted_ip:$untrusted_port: the real IP/port of the client
# - $ifconfig_pool_local: the assigned VPN IP address

curl -X POST http://localhost:__API_PORT__/api/vpnEvent/connect \
  -H "Content-Type: application/json" \
  -d "{
    \"CommonName\": \"$common_name\",
    \"RealAddress\": \"$untrusted_ip:$untrusted_port\",
    \"VirtualAddress\": \"$ifconfig_pool_local\"
  }" >/dev/null 2>&1 &
