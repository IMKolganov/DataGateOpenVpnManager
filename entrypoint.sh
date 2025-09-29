#!/bin/bash
set -e

# ---------------------------
# Core environment defaults
# ---------------------------
API_PORT=${API_PORT:-5010}
API_HOST=${API_HOST:-127.0.0.1}
PORT=${PORT:-1194}
PROTO=${PROTO:-udp}
OpenVpnManagement__Port=${OpenVpnManagement__Port:-5092}
DATA_DIR=${DATA_DIR:-/mnt}
DNS1=${DNS1:-8.8.8.8}
DNS2=${DNS2:-8.8.4.4}
VPN_SUBNET=${VPN_SUBNET:-10.51.28.0}
VPN_NETMASK=${VPN_NETMASK:-255.255.255.0}
TUN_IF="${TUN_IF:-tun0}"
WAN_IF="${WAN_IF:-eth0}"

EASYRSA_DIR="$DATA_DIR/easy-rsa"
SCRIPT_SOURCE="/scripts"

# ---------------------------
# Template & config controls
# ---------------------------
SERVER_CONF_TEMPLATE=${SERVER_CONF_TEMPLATE:-/defaults/server.conf.template}
SERVER_CONF=${SERVER_CONF:-$DATA_DIR/server.conf}
OVERWRITE_SERVER_CONF=${OVERWRITE_SERVER_CONF:-false}

# ---------------------------
# Performance/security defaults
# ---------------------------
# DCO (enabled by default)
ENABLE_DCO=${ENABLE_DCO:-true}

# Buffers/MTU
AUTOBUF=${AUTOBUF:-on}          # on/off -> sndbuf/rcvbuf 0 + push + fast-io
TUN_MTU=${TUN_MTU:-1500}
MSSFIX=${MSSFIX:-1450}

# Ciphers (NCP)
NCP_LIST=${NCP_LIST:-AES-256-GCM:CHACHA20-POLY1305:AES-128-GCM}
NCP_FALLBACK=${NCP_FALLBACK:-AES-256-GCM}
AUTH_ALG=${AUTH_ALG:-SHA256}

# TLS
TLS_MIN=${TLS_MIN:-1.2}
TLS_GROUPS=${TLS_GROUPS:-prime256v1}   # used by easyrsa; in conf we keep tls-groups prime256v1 fixed to match

# Keepalive/verbosity
KEEPALIVE=${KEEPALIVE:-"15 120"}
VERB=${VERB:-4}
STATUS_INTERVAL=${STATUS_INTERVAL:-10}
SCRIPT_SECURITY=${SCRIPT_SECURITY:-2}

# IPv6
ENABLE_IPV6=${ENABLE_IPV6:-off}
SERVER_IPV6=${SERVER_IPV6:-fd42:1::/64}
DNS6_1=${DNS6_1:-2606:4700:4700::1111}
DNS6_2=${DNS6_2:-2606:4700:4700::1001}

# Routing toggles
PUSH_REDIRECT_TOGGLE=${PUSH_REDIRECT_TOGGLE:-on}  # on/off
CLIENT_TO_CLIENT_TOGGLE=${CLIENT_TO_CLIENT_TOGGLE:-off}  # on/off
EXTRA_ROUTES=${EXTRA_ROUTES:-}  # multi-line: e.g. 'push "route 192.168.10.0 255.255.255.0"\npush "route 10.20.0.0 255.255.0.0"'

# Management & run user
MGMT_ADDR=${MGMT_ADDR:-127.0.0.1}
RUN_USER=${RUN_USER:-nobody}
RUN_GROUP=${RUN_GROUP:-nogroup}

# Extra passthrough
OVPN_EXTRA=${OVPN_EXTRA:-}

# .NET port
if [ -n "$API_PORT" ]; then
  export ASPNETCORE_HTTP_PORTS="$API_PORT"
  echo "[entrypoint] Set ASPNETCORE_HTTP_PORTS to $ASPNETCORE_HTTP_PORTS"
fi

echo "===== STARTING OPENVPN CONTAINER ====="

# ----------------------------------------
# NAT/forward (keep your original logic)
# ----------------------------------------
iptables -P FORWARD ACCEPT
iptables -C FORWARD -i "$TUN_IF" -j ACCEPT 2>/dev/null || iptables -A FORWARD -i "$TUN_IF" -j ACCEPT
iptables -C FORWARD -o "$TUN_IF" -j ACCEPT 2>/dev/null || iptables -A FORWARD -o "$TUN_IF" -j ACCEPT
iptables -t nat -C POSTROUTING -s "$VPN_SUBNET/$VPN_NETMASK" -o "$WAN_IF" -j MASQUERADE 2>/dev/null \
  || iptables -t nat -A POSTROUTING -s "$VPN_SUBNET/$VPN_NETMASK" -o "$WAN_IF" -j MASQUERADE

# ----------------------------------------
# Copy OpenVPN hook scripts
# ----------------------------------------
echo "===== Copying OpenVPN hook scripts ====="
mkdir -p /etc/openvpn/scripts
for SCRIPT in client-connect.sh client-disconnect.sh learn-address.sh tls-verify.sh log-watcher.sh; do
  SRC="$SCRIPT_SOURCE/$SCRIPT"; DST="/etc/openvpn/scripts/$SCRIPT"
  if [ -f "$SRC" ]; then
    cp -f "$SRC" "$DST"
    chmod +x "$DST"
    echo "✅ $SCRIPT copied"
  else
    echo "⚠️ $SCRIPT not found in $SCRIPT_SOURCE"
  fi
done

echo "===== Checking contents of $DATA_DIR before starting... ====="
ls -l "$DATA_DIR" || true

# ----------------------------------------
# Easy-RSA bootstrap (keep your logic)
# ----------------------------------------
if [ ! -x "$EASYRSA_DIR/easyrsa" ]; then
    echo "Copying Easy-RSA to $EASYRSA_DIR..."
    mkdir -p "$EASYRSA_DIR"
    cp -r /usr/share/easy-rsa/* "$EASYRSA_DIR"
    chmod +x "$EASYRSA_DIR/easyrsa"
fi

if [ ! -f "$EASYRSA_DIR/easyrsa" ] || [ ! -x "$EASYRSA_DIR/easyrsa" ]; then
    echo "ERROR: Easy-RSA script not found or not executable at $EASYRSA_DIR/easyrsa"
    echo "Please ensure Easy-RSA v3 is installed and placed correctly."
    exit 1
fi

if [ ! -d "$EASYRSA_DIR/pki" ]; then
    echo "PKI not found. Initializing..."
    cd "$EASYRSA_DIR"

    export EASYRSA_BATCH=1
    export EASYRSA_REQ_CN="OpenVPN-Server"
    export EASYRSA_PKI="$EASYRSA_DIR/pki"

    ./easyrsa --batch init-pki
    ./easyrsa --batch build-ca nopass
    ./easyrsa --batch gen-req server nopass
    ./easyrsa --batch sign-req server server
    
    unset EASYRSA_REQ_CN
fi

# Ensure ta.key exists
if [ ! -f "$EASYRSA_DIR/pki/ta.key" ]; then
    echo "===== Generating new ta.key (tls-crypt)..."
    openvpn --genkey secret "$EASYRSA_DIR/pki/ta.key"
else
    echo "ta.key already exists."
fi

# Copy certs/keys
echo "===== Copying necessary certs and keys to /etc/openvpn... ====="
declare -A FILES_TO_COPY=(
  ["$EASYRSA_DIR/pki/ca.crt"]="/etc/openvpn/ca.crt"
  ["$EASYRSA_DIR/pki/issued/server.crt"]="/etc/openvpn/server.crt"
  ["$EASYRSA_DIR/pki/private/server.key"]="/etc/openvpn/server.key"
  ["$EASYRSA_DIR/pki/ta.key"]="/etc/openvpn/ta.key"
)
for SRC in "${!FILES_TO_COPY[@]}"; do
  DEST=${FILES_TO_COPY[$SRC]}
  if [ -f "$SRC" ]; then
    cp "$SRC" "$DEST"
  else
    echo "ERROR: Required file $SRC not found, exiting."
    exit 1
  fi
done

# ----------------------------------------
# Build optional blocks for the template
# ----------------------------------------
# redirect-gateway toggle
if [ "${PUSH_REDIRECT_TOGGLE,,}" = "on" ]; then
  PUSH_REDIRECT='push "redirect-gateway def1"'
else
  PUSH_REDIRECT=''
fi

# client-to-client toggle
if [ "${CLIENT_TO_CLIENT_TOGGLE,,}" = "on" ]; then
  CLIENT_TO_CLIENT='client-to-client'
else
  CLIENT_TO_CLIENT=''
fi

# autobuffer/fast-io block
if [ "${AUTOBUF,,}" = "on" ]; then
  AUTOBUF_BLOCK=$'sndbuf 0\nrcvbuf 0\npush "sndbuf 0"\npush "rcvbuf 0"\nfast-io'
else
  AUTOBUF_BLOCK=''
fi

# DCO block (enabled by default)
if [ "${ENABLE_DCO,,}" = "true" ]; then
  DCO_BLOCK=$'enable-dco\ndev-type ovpn-dco\ndev ovpn-dco'
else
  DCO_BLOCK=''
fi

# IPv6 block
if [ "${ENABLE_IPV6,,}" = "on" ]; then
  IPV6_BLOCK=$'tun-ipv6\nserver-ipv6 '"${SERVER_IPV6}"$'\npush "dhcp-option DNS6 '"${DNS6_1}"$'"\npush "dhcp-option DNS6 '"${DNS6_2}"$'"'
else
  IPV6_BLOCK=''
fi

# ----------------------------------------
# Generate server.conf from template
# ----------------------------------------
generate_from_template() {
  echo "Generating server.conf from template: $SERVER_CONF_TEMPLATE -> $SERVER_CONF"

  # Export all used variables for envsubst
  export PORT PROTO OpenVpnManagement__Port DATA_DIR EASYRSA_DIR API_PORT API_HOST \
         DNS1 DNS2 VPN_SUBNET VPN_NETMASK TUN_MTU MSSFIX \
         ENABLE_DCO AUTOBUF PUSH_REDIRECT_TOGGLE CLIENT_TO_CLIENT_TOGGLE \
         NCP_LIST NCP_FALLBACK AUTH_ALG TLS_MIN TLS_GROUPS KEEPALIVE \
         VERB STATUS_INTERVAL SCRIPT_SECURITY ENABLE_IPV6 SERVER_IPV6 DNS6_1 DNS6_2 \
         MGMT_ADDR RUN_USER RUN_GROUP OVPN_EXTRA EXTRA_ROUTES \
         PUSH_REDIRECT CLIENT_TO_CLIENT AUTOBUF_BLOCK DCO_BLOCK IPV6_BLOCK

  # Strict: require envsubst and template
  if ! command -v envsubst >/dev/null 2>&1; then
    echo "❌ ERROR: envsubst is not available. Install gettext-base in the image."
    exit 1
  fi
  if [ ! -f "$SERVER_CONF_TEMPLATE" ]; then
    echo "❌ ERROR: Template not found at $SERVER_CONF_TEMPLATE"
    exit 1
  fi

  envsubst < "$SERVER_CONF_TEMPLATE" > "$SERVER_CONF"
}

if [ ! -f "$SERVER_CONF" ]; then
  generate_from_template
else
  if [ "${OVERWRITE_SERVER_CONF,,}" = "true" ]; then
    echo "OVERWRITE_SERVER_CONF=true -> regenerating $SERVER_CONF from template"
    generate_from_template
  else
    echo "server.conf already exists, keeping existing file."
  fi
fi

# ----------------------------------------
# Logs & CRL
# ----------------------------------------
echo "===== Clearing logs... ====="
truncate -s 0 "$DATA_DIR/openvpn.log" || touch "$DATA_DIR/openvpn.log"
truncate -s 0 "$DATA_DIR/openvpn-status.log" || touch "$DATA_DIR/openvpn-status.log"
chmod 777 "$DATA_DIR/openvpn.log" "$DATA_DIR/openvpn-status.log"

if [ ! -f "$EASYRSA_DIR/pki/crl.pem" ]; then
    echo "Generating crl.pem (empty revocation list)..."
    cd "$EASYRSA_DIR"
    export EASYRSA_PKI="$EASYRSA_DIR/pki"
    ./easyrsa gen-crl
else
    echo "crl.pem already exists."
fi
chmod 644 "$EASYRSA_DIR/pki/crl.pem"

echo "===== Setting permissions for $DATA_DIR recursively... ====="
chmod -R a+rX "$DATA_DIR"

echo "===== Fixing permissions to allow OpenVPN (user: nobody) read crl.pem ====="
chmod 644 "$EASYRSA_DIR/pki/crl.pem" || echo "❌ Failed to chmod crl.pem"
chmod o+rx "$DATA_DIR" || echo "❌ Failed to chmod $DATA_DIR"
chmod o+rx "$EASYRSA_DIR" || echo "❌ Failed to chmod $EASYRSA_DIR"
chmod o+rx "$EASYRSA_DIR/pki" || echo "❌ Failed to chmod $EASYRSA_DIR/pki"
echo "✅ crl.pem permission fix complete"

echo "===== FINAL CHECK BEFORE STARTING OPENVPN ====="
ls -l "$DATA_DIR" || true
[ -d "$EASYRSA_DIR/pki" ] && ls -l "$EASYRSA_DIR/pki" || true

echo "===== server.conf contents ====="
cat "$SERVER_CONF" || echo "server.conf not found!"

echo "===== Starting OpenVPN in background..."
openvpn --config "$SERVER_CONF" &
OPENVPN_PID=$!

# Stream OpenVPN logs to stdout
echo "===== Attaching OpenVPN log to stdout... ====="
tail -F "$DATA_DIR/openvpn.log" &
TAIL_PID=$!

echo "[entrypoint] Starting .NET application..."

# Wait for .NET files
echo "⏳ Waiting for DataGateCertManager.dll and dependencies to appear..."
timeout=10
elapsed=0
while [ ! -f /app/DataGateCertManager.dll ] || [ ! -f /app/DataGateCertManager.runtimeconfig.json ]; do
    if [ "$elapsed" -ge "$timeout" ]; then
        echo "❌ ERROR: .NET files not found after ${timeout}s, exiting"
        echo "🔍 /app contents:"
        ls -la /app
        exit 1
    fi
    echo "  ...still waiting (${elapsed}s)"
    sleep 1
    elapsed=$((elapsed + 1))
done

runuser -u "$RUN_USER" -- cat "$EASYRSA_DIR/pki/crl.pem" >/dev/null \
  && echo "✅ $RUN_USER can read crl.pem" \
  || echo "❌ $RUN_USER CANNOT read crl.pem"

echo "✅ Found required .NET files"
cd /app

dotnet DataGateCertManager.dll &
DOTNET_PID=$!

# Wait for OpenVPN and .NET
wait $OPENVPN_PID
OPENVPN_EXIT_CODE=$?
wait $DOTNET_PID
DOTNET_EXIT_CODE=$?

# Clean shutdown
kill $TAIL_PID 2>/dev/null || true

if [ $OPENVPN_EXIT_CODE -ne 0 ]; then
  echo "OpenVPN exited with code $OPENVPN_EXIT_CODE"
  exit $OPENVPN_EXIT_CODE
fi

if [ $DOTNET_EXIT_CODE -ne 0 ]; then
  echo ".NET app exited with code $DOTNET_EXIT_CODE"
  exit $DOTNET_EXIT_CODE
fi