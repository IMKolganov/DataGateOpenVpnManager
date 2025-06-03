#!/bin/bash

tail -F /var/log/openvpn.log | while read line; do
  if echo "$line" | grep -q "AUTH_FAILED"; then
    cn=$(echo "$line" | grep -oP "CN='[^']+'" | cut -d"'" -f2)

    curl -X POST http://localhost:__API_PORT__/api/vpnEvent/fail \
      -H "Content-Type: application/json" \
      -d "{\"CommonName\": \"$cn\", \"Message\": \"$line\"}" >/dev/null 2>&1 &
  fi
done
