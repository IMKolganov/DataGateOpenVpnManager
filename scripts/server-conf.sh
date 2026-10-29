#!/bin/bash
# server.conf rendering, sourced by entrypoint.sh (and by tests).
#
# render_server_conf prints server.conf to stdout from variables entrypoint.sh has already resolved
# (PORT, PROTO, DEV_LINES, DCO_OPTION, CIPHER_LINES, MSSFIX_LINE, ...).
#
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
server $VPN_SUBNET $VPN_NETMASK
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

}
