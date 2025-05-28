#!/bin/bash

action="$1"
address="$2"
common_name="$3"

# Мы фиксируем только ADD, потому что DEL бывает при clean-up
if [ "$action" = "add" ]; then
  curl -X POST http://localhost:5000/api/vpnEvent/attempt \
    -H "Content-Type: application/json" \
    -d "{
      \"CommonName\": \"$common_name\",
      \"VirtualAddress\": \"$address\"
    }" >/dev/null 2>&1 &
fi
