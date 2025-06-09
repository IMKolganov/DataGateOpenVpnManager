#!/bin/bash

# This script is triggered by OpenVPN's --tls-verify directive.
# It receives the certificate depth and common name (CN) of the client being verified.
# The script sends this information to the backend API for logging or auditing purposes.

cert_depth="$1"
common_name="$2"

# Get the current UTC timestamp in ISO 8601 format
timestamp=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

# Send verification info to the backend API
curl -X POST http://localhost:__API_PORT__/api/vpnEvent/tlsverify \
  -H "Content-Type: application/json" \
  -d "{
    \"CommonName\": \"$common_name\",
    \"Depth\": \"$cert_depth\",
    \"Timestamp\": \"$timestamp\"
  }" >/dev/null 2>&1

exit 0
