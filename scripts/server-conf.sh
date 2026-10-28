#!/bin/bash
# server.conf rendering, sourced by entrypoint.sh (and by tests).
#
# render_server_conf prints server.conf to stdout from variables entrypoint.sh has already resolved
# (PORT, PROTO, DEV_LINES, DCO_OPTION, CIPHER_LINES, MSSFIX_LINE, ...).
#
# Optional client-config-dir (off by default — output is byte-identical to the legacy heredoc):
#   CLIENT_CONFIG_DIR  empty = off; "true"/"1"/"yes" = $DATA_DIR/ccd; otherwise an absolute path.
#   CCD_EXCLUSIVE      true|1|yes adds "ccd-exclusive" (only CNs with a ccd file may connect).
#   STATIC_IP_RESERVED with ccd on: how many addresses at the top of VPN_SUBNET are kept out of the dynamic pool
#                      for ifconfig-push (default 32, 0 = off). OpenVPN's pool does not skip ccd addresses, so
#                      without this a dynamic client can be handed a connected static client's IP.

# Prints the effective ccd path (empty when disabled).
resolve_client_config_dir() {
  case "${CLIENT_CONFIG_DIR:-}" in
    "") echo "" ;;
    true|TRUE|True|1|yes|YES|Yes) echo "${DATA_DIR%/}/ccd" ;;
    *) echo "${CLIENT_CONFIG_DIR%/}" ;;
  esac
}

is_truthy() {
  case "${1:-}" in
    true|TRUE|True|1|yes|YES|Yes) return 0 ;;
    *) return 1 ;;
  esac
}

ip_to_int() {
  local a b c d
  IFS=. read -r a b c d <<< "$1"
  echo $(( (a << 24) | (b << 16) | (c << 8) | d ))
}

int_to_ip() {
  echo "$(( ($1 >> 24) & 255 )).$(( ($1 >> 16) & 255 )).$(( ($1 >> 8) & 255 )).$(( $1 & 255 ))"
}

# Prints the effective number of reserved static addresses (0 = none). Same rules as
# LocalIssuanceOptions.StaticIpReserved: ccd on, default 32, and the dynamic pool must keep at least one address.
static_ip_reserved_count() {
  [ -n "$(resolve_client_config_dir)" ] || { echo 0; return; }
  local wanted="${STATIC_IP_RESERVED:-32}"
  [[ "$wanted" =~ ^[0-9]+$ ]] || { echo "server-conf: STATIC_IP_RESERVED='$wanted' is not a number; not reserving" >&2; echo 0; return; }
  local net mask bcast pool_size
  mask=$(ip_to_int "$VPN_NETMASK")
  net=$(( $(ip_to_int "$VPN_SUBNET") & mask ))
  bcast=$(( net | (~mask & 0xFFFFFFFF) ))
  pool_size=$(( bcast - 1 - (net + 2) + 1 ))
  if [ "$wanted" -ge "$pool_size" ]; then
    echo "server-conf: STATIC_IP_RESERVED=$wanted leaves no dynamic pool in $VPN_SUBNET/$VPN_NETMASK; not reserving" >&2
    echo 0; return
  fi
  echo "$wanted"
}

# "server" line; with a static reservation the pool is declared explicitly without the top addresses.
render_server_lines() {
  local reserved net mask bcast
  reserved=$(static_ip_reserved_count)
  if [ "$reserved" -eq 0 ]; then
    echo "server $VPN_SUBNET $VPN_NETMASK"
    return
  fi
  mask=$(ip_to_int "$VPN_NETMASK")
  net=$(( $(ip_to_int "$VPN_SUBNET") & mask ))
  bcast=$(( net | (~mask & 0xFFFFFFFF) ))
  echo "server $VPN_SUBNET $VPN_NETMASK nopool"
  echo "ifconfig-pool $(int_to_ip $(( net + 2 ))) $(int_to_ip $(( bcast - 1 - reserved ))) $VPN_NETMASK"
}

render_server_conf() {
cat <<EOF
port $PORT
proto $PROTO
$DEV_LINES
$DCO_OPTION

ca /etc/openvpn/ca.crt
cert /etc/openvpn/server.crt
key /etc/openvpn/server.key
dh none
tls-groups prime256v1

topology subnet
$(render_server_lines)
ifconfig-pool-persist /etc/openvpn/ipp.txt

push "dhcp-option DNS $DNS1"
push "dhcp-option DNS $DNS2"
push "block-outside-dns"
push "redirect-gateway def1"
${MSSFIX_LINE}

keepalive 15 120

remote-cert-tls client
tls-version-min $TLS_VERSION_MIN
tls-crypt /etc/openvpn/ta.key

$CIPHER_LINES
auth $AUTH

user nobody
group nogroup

persist-key
persist-tun

crl-verify $EASYRSA_DIR/pki/crl.pem

status $DATA_DIR/openvpn-status.log
status-version 3
log $DATA_DIR/openvpn.log
log-append $DATA_DIR/openvpn.log
syslog

management 127.0.0.1 $OpenVpnManagement__Port

script-security 2
setenv API_PORT $API_PORT
setenv API_HOST $API_HOST
client-connect /etc/openvpn/scripts/client-connect.sh
client-disconnect /etc/openvpn/scripts/client-disconnect.sh
learn-address /etc/openvpn/scripts/learn-address.sh
tls-verify /etc/openvpn/scripts/tls-verify.sh

verb 4
EOF

  local ccd_dir
  ccd_dir="$(resolve_client_config_dir)"
  if [ -n "$ccd_dir" ]; then
    printf '\nclient-config-dir %s\n' "$ccd_dir"
    if is_truthy "${CCD_EXCLUSIVE:-}"; then
      printf 'ccd-exclusive\n'
    fi
  fi
}
