#!/bin/bash

curl -X POST http://localhost:5000/api/vpnEvent/connect \
  -H "Content-Type: application/json" \
  -d "{
    \"CommonName\": \"$common_name\",
    \"RealAddress\": \"$untrusted_ip:$untrusted_port\",
    \"VirtualAddress\": \"$ifconfig_pool_local\"
  }" >/dev/null 2>&1 &