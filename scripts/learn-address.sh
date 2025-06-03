#!/bin/bash

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
