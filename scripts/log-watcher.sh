#!/bin/bash

# This script watches the OpenVPN log file for authentication failures (AUTH_FAILED).
# When such a line is detected, it extracts the Common Name (CN) from the log
# and sends the failure event to the backend API for further processing or alerting.

tail -F /var/log/openvpn.log | while read line; do
  if echo "$line" | grep -q "AUTH_FAILED"; then
    # Extract Common Name (CN='...') from log line
    cn=$(echo "$line" | grep -oP "CN='[^']+'" | cut -d"'" -f2)

    # Send failure event to backend API
    curl -X POST http://localhost:__API_PORT__/api/vpnEvent/fail \
      -H "Content-Type: application/json" \
      -d "{\"CommonName\": \"$cn\", \"Message\": \"$line\"}" >/dev/null 2>&1 &
  fi
done
