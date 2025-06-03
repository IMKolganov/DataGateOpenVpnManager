#!/bin/bash

cert_depth="$1"
common_name="$2"

timestamp=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

curl -X POST http://localhost:__API_PORT__/api/vpnEvent/tlsverify \
  -H "Content-Type: application/json" \
  -d "{
    \"CommonName\": \"$common_name\",
    \"Depth\": \"$cert_depth\",
    \"Timestamp\": \"$timestamp\"
  }" >/dev/null 2>&1

exit 0
