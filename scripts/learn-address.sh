#!/bin/bash

# This script is called by OpenVPN via the `learn-address` directive.
# It handles client connection *attempts*, including those rejected by tls-verify.
# Only the "add" action is used here to log an attempted assignment of a VPN IP.
# Parameters:
# - $1 (action): e.g., "add", "delete", "update"
# - $2 (address): the virtual VPN IP assigned (or attempted)
# - $3 (common_name): the client's certificate common name

action="$1"
address="$2"
common_name="$3"

if [ "$action" = "add" ]; then
  curl -X POST http://localhost:__API_PORT__/api/vpnEvent/attempt \
    -H "Content-Type: application/json" \
    -d "{
      \"CommonName\": \"$common_name\",
      \"VirtualAddress\": \"$address\"
    }" >/dev/null 2>&1 &
fi
