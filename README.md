<h1 align="left">
  <img src="https://raw.githubusercontent.com/IMKolganov/DataGateMonitorFrontend/main/public/favicon.svg" width="32" height="32" alt="" />
  DataGateOpenVpnManager + OpenVPN Docker Image
</h1>

OpenVPN sidecar for [DataGate Monitor](https://dash.datagateapp.com/). In the monorepo this is the `openvpn/` submodule ([DataGateMonitor](https://github.com/IMKolganov/DataGateMonitor)). Standalone clone: [DataGateOpenVpnManager](https://github.com/IMKolganov/DataGateOpenVpnManager).

**Links**

| Resource | Link |
|----------|------|
| <img src="https://raw.githubusercontent.com/IMKolganov/DataGateMonitorFrontend/main/public/favicon.svg" width="16" height="16" alt="" /> **DataGate** | [datagateapp.com](https://datagateapp.com/) |
| <img src="https://cdn.simpleicons.org/googleplay/414141" width="16" height="16" alt="" /> **Download** | [datagateapp.com/download](https://datagateapp.com/download) |
| <img src="https://cdn.simpleicons.org/telegram/26A5E4" width="16" height="16" alt="" /> **Telegram channel** | [@datagateapp](https://t.me/datagateapp) |

This project builds a Docker image containing:

* A .NET 9 application: **DataGateOpenVpnManager**
* A fully functional **OpenVPN server**
* Integrated **Easy-RSA** certificate management

It uses a custom entrypoint to initialize PKI, generate default configuration, and run both OpenVPN and the .NET app in the same container.

---

## 📥 Clone the Repository

```bash
git clone https://github.com/IMKolganov/DataGateOpenVpnManager.git
cd DataGateOpenVpnManager
```

---

## 🛠 Build

```bash
docker build --build-arg TARGETARCH=$(uname -m) -t datagate-openvpn .
```

You must pass `TARGETARCH`. Common values:

* `x64` or `amd64`
* `arm64`

Optional:

```bash
docker build --build-arg TARGETARCH=amd64 --build-arg BUILD_CONFIGURATION=Debug -t datagate-openvpn .
```

---

## 🚀 Run

```bash
docker run \
  --cap-add=NET_ADMIN \
  -e PORT=1194 \
  -e PROTO=udp \
  -e DATA_DIR=/mnt \
  -v /path/to/data:/mnt \
  -p 1194:1194/udp \
  datagate-openvpn
```

Environment variables:

| Variable                    | Description                        | Default              |
|-----------------------------|------------------------------------|----------------------|
| `PORT`                      | OpenVPN port                       | `1194`               |
| `PROTO`                     | Protocol (`udp` or `tcp`)          | `udp`                |
| `DATA_DIR`                  | Data directory for config/logs/pki | `/mnt`               |
| `DNS1`, `DNS2`              | Pushed DNS servers                 | `8.8.8.8`, `8.8.4.4` |
| `CIPHER`                    | Data-channel cipher; rewritten into issued `.ovpn` and `/api/info` | `AES-128-GCM` if DCO, else `AES-256-CBC` |
| `DATA_CIPHERS`              | OpenVPN `data-ciphers` list; rewritten into issued `.ovpn` / `/api/info` | GCM/ChaCha list if DCO, else unset |
| `DCO`                       | Enable OpenVPN DCO (`true`/`1`/`yes`); exposed on `/api/info` | `false` |
| `AUTH`                      | HMAC digest (`auth`); server.conf + issued `.ovpn` / `/api/info` | `SHA256` |
| `TLS_VERSION_MIN`           | `tls-version-min` on server and issued `.ovpn` / `/api/info` | `1.2` |
| `CLIENT_VERB`               | Suggested client `.ovpn` `verb` (not server log verb) / `/api/info` | `3` |
| `MSSFIX`                    | Optional `push "mssfix N"` to clients; exposed on `/api/info` (not duplicated in `.ovpn`) | _(unset)_ |
| `TUN_DEV`                   | Fixed tun device name (stable ufw/NAT) | _(unset → `dev tun`) |
| `TUN_IF`                    | Extra FORWARD by iface (optional)   | _(unset)_ |
| `WAN_IF`                    | WAN iface for MASQUERADE            | `eth0` (auto if missing) |
| `VPN_SUBNET`, `VPN_NETMASK` | VPN subnet config                  | `10.51.28.0/24`      |
| `OpenVpnManagement__Port`   | OpenVPN management interface port  | `5092`               |
| `OpenVpnProxy__ByteDebug`   | Compare proxy vs management bytes (WSS debug) | `false` |
| `OpenVpnProxy__ByteDebugIntervalSeconds` | Periodic byte comparison while connected (`0` = on disconnect only) | `0` |
| `OpenVpnProxy__CloseZombieAfterMissingSeconds` | Close WSS when OpenVPN peer missing from management (`0` = off) | `0` |
| `OpenVpnProxy__ZombieCheckIntervalSeconds` | How often to check for zombie proxy sessions | `30` |
| `OpenVpnProxy__TlsLogEnrichmentEnabled` | Tag tls-crypt errors as external probe vs app client (for Wazuh) | `true` |
| `PROXY_BYTE_DEBUG`          | Legacy alias for `OpenVpnProxy__ByteDebug` (`1` / `true`) | _(unset)_ |
| `PiHole__Enabled`             | Collect VPN DNS queries from Pi-hole API                  | `false` |
| `PiHole__BaseUrl`             | Pi-hole FTL API base URL (same network namespace)         | `http://127.0.0.1:8080` |
| `PiHole__AppPassword`         | Pi-hole application password for `/api/auth`              | _(empty)_ |
| `PiHole__PollIntervalSeconds` | How often to poll Pi-hole query log                       | `60` |
| `PiHole__BatchSize`           | Max queries per poll                                      | `200` |
| `PIHOLE_ENABLED`              | Legacy env alias for `PiHole__Enabled`                    | _(unset)_ |
| `PIHOLE_BASE_URL`             | Legacy env alias for `PiHole__BaseUrl`                    | _(unset)_ |
| `PIHOLE_APP_PASSWORD`         | Legacy env alias for `PiHole__AppPassword`                | _(unset)_ |
| `PIHOLE_POLL_INTERVAL_SEC`    | Legacy env alias for `PiHole__PollIntervalSeconds`        | _(unset)_ |

**Pi-hole config priority (highest wins):** `PIHOLE_*` / `PiHole__*` env vars → dashboard **Save & apply** (`$DATA_DIR/pihole-runtime-config.json`) → `appsettings` defaults. Env overrides only the fields that are set.

### Multi-stack host (fixed tun + UFW by subnet)

Do **not** rely on `tunN` names in UFW — they change on every OpenVPN recreate. Use:

1. **Image ≥ 1.2.5.94** with `TUN_DEV=ovpn-udp` (unique per stack) — entrypoint writes `dev ovpn-udp` / `dev-type tun`, deletes orphans that still hold this subnet’s `.1`, and installs FORWARD/NAT by **CIDR**.
2. **Host UFW once per subnet** (survives reboot and tun renames):

```bash
sudo chmod +x scripts/host-ufw-vpn-subnet.sh
# UDP pool → internet + DNS via Pi-hole on TCP tun (10.51.30.1):
sudo ./scripts/host-ufw-vpn-subnet.sh 10.51.32.0/24 10.51.30.1
```

3. Compose env for UDP WSS:

```yaml
TUN_DEV: ovpn-udp
VPN_SUBNET: "10.51.32.0"
DNS1: "10.51.30.1"
DNS2: "10.51.30.1"
DCO: "true"
CIPHER: "AES-128-GCM"
```

Keep DNS push on Pi-hole (`10.51.30.1` or this stack’s own `.1`), not public `1.1.1.1`.

#### TCP stack + shared Pi-hole (host netns)

Same host usually has **one Pi-hole** in `network_mode: "container:openvpn-tcp-wss"` (host netns). OpenVPN TCP uses `dev tun` unless `TUN_DEV` is set — after reboot/recreate the kernel picks `tun0`/`tun1`, while UFW still allows `53` on the **old** name. Symptom: dashboard Pi-hole pipeline green (HTTP `:8080`), VPN connected, **no internet** (`block-outside-dns` + DNS timeout). UDP often still works if its iface name is stable (`TUN_DEV=ovpn-udp`) or UFW allows that subnet → Pi-hole `.1`.

**Do this:**

1. Pin TCP: `TUN_DEV: tun-tcp` and `TUN_IF: tun-tcp` (do **not** turn DCO off if it already worked).
2. Prefer UFW **by destination**, not iface name (survives `tun0` → `tun-tcp`):

```bash
sudo ufw allow in on tun-tcp proto udp to any port 53 comment 'Pi-hole DNS tun-tcp'
sudo ufw allow in on tun-tcp proto tcp to any port 53 comment 'Pi-hole DNS tun-tcp'
# even better, already used on some hosts:
sudo ufw allow from 10.51.15.0/24 to 10.51.15.1 port 53
sudo ufw allow from 10.51.16.0/24 to 10.51.16.1 port 53
```

3. Pi-hole FTL v6: `dns.interface` is **one** name. `BIND` + `tun-tcp,ovpn-udp,br-…` (comma list) or a custom `entrypoint` wrapping `start.sh` can leave `:53` open with **no answers** (`Recv-Q` growing, no `listening on … port 53` in FTL log). Working pattern:

```yaml
# pi-hole compose — no custom entrypoint
FTLCONF_dns_listeningMode: 'BIND'
FTLCONF_dns_interface: 'tun-tcp'   # TCP .1 only
```

Extra listen IPs (UDP `.1`, docker-bridge for Xray) via `/etc/dnsmasq.d/` **after** FTL is healthy:

```bash
printf '%s\n' 'listen-address=10.51.16.1' 'listen-address=172.20.0.1' \
  > ~/pi-hole/etc-dnsmasq.d/99-extra-listen.conf
docker exec datagate-pihole pihole-FTL --config misc.etc_dnsmasq_d true
docker restart datagate-pihole
# log must show: listening on 10.51.16.1 and 172.20.0.1
```

4. `network_mode: container:openvpn-tcp-wss` stores the **container ID**. Recreate OpenVPN → Pi-hole stays `Exited` (`No such container: <old id>`). Recreate Pi-hole **after** TCP is Up. Optional systemd oneshot (`After=docker.service`) that waits for `openvpn-tcp-wss` + `openvpn-udp-wss` then `docker compose up -d --force-recreate` in `~/pi-hole`.
5. Host reboot does **not** fix a wedged FTL. Check `docker logs datagate-pihole | grep 'listening on'`. Dashboard step 4 (API `:8080`) ≠ DNS `:53`.

Compose env for TCP WSS:

```yaml
TUN_DEV: tun-tcp
TUN_IF: tun-tcp
VPN_SUBNET: "10.51.15.0"   # example
DNS1: "10.51.15.1"
DNS2: "10.51.15.1"
```

---

## 📁 Directory Layout Inside Container

| Path                         | Purpose                               |
|------------------------------|---------------------------------------|
| `/etc/openvpn`               | Final OpenVPN config/cert destination |
| `/usr/share/easy-rsa`        | EasyRSA installation                  |
| `$DATA_DIR` (default `/mnt`) | Mounted volume for logs, config, PKI  |

---

## 📦 What This Does

1. **Copies and initializes Easy-RSA** in `$DATA_DIR/easy-rsa`
2. **Generates CA**, server cert/key, and `ta.key` if missing
3. Creates `server.conf` if missing
4. Configures `iptables` to allow VPN traffic
5. Runs OpenVPN and .NET app in parallel

---

## 📝 Logs

Log files in `$DATA_DIR`:

* `openvpn.log`
* `openvpn-status.log`
* `pihole-query-cursor.txt` — last Pi-hole query log cursor (pre-created by entrypoint, mode `644`)
* `pihole-runtime-config.json` — Pi-hole collector config from dashboard **Save & apply** (survives container restart; mode `600`, re-applied on each container start)

Raw `tls-crypt unwrapping failed` lines from internet scanners are **filtered out of Docker stdout**; the .NET app re-emits them with an origin tag:

| Log prefix | Meaning | Suggested Wazuh level |
|------------|---------|------------------------|
| `[OpenVpnTlsExternalProbe]` | Direct probe to OpenVPN port (not our WSS app) | ignore / low |
| `[OpenVpnTlsAppClient]` | WSS proxy path (`127.0.0.1:localPort`) with `clientRef` / `User-Agent` | alert |
| `[OpenVpnTlsLocalUnknown]` | Loopback without matched proxy session | investigate |

Wazuh rule **100913** should match `[OpenVpnTlsAppClient]` instead of the raw `TLS Error: tls-crypt` string.

---

## 🔐 Local profile issuance (service profiles)

Issue profiles **on the node itself** (no dashboard request) — e.g. a home hub handing service profiles to
servers that must reach a host inside the VPN at boot. The node reports every local issue/revoke to the
backend, so the dashboard lists them (labels *Issued locally* / *Service*, static IP) and can revoke them.

All of it is **off by default**; a node without these variables renders the same `server.conf` as before.

| Variable | Default | Meaning |
|---|---|---|
| `LOCAL_ISSUANCE_ENABLED` | `false` | Admin socket, node key, event outbox + delivery to the backend. |
| `CLIENT_CONFIG_DIR` | empty (off) | Adds `client-config-dir`; `true` = `$DATA_DIR/ccd`, or an absolute path. Needed for `--static-ip`. |
| `CCD_EXCLUSIVE` | `false` | Adds `ccd-exclusive` (only CNs with a ccd file connect; every issued profile then gets one). |
| `LOCAL_ISSUANCE_DIR` | `$DATA_DIR/local-issuance` | Private dir (0700): `admin.sock`, `node-identity.key`, `outbox/`, `profiles.json`, optional `client-template.ovpn`. |

```bash
docker exec openvpn-udp datagate-profile create upcloud-db1 --service --static-ip 10.50.28.201 > upcloud-db1.ovpn
docker exec openvpn-udp datagate-profile list
docker exec openvpn-udp datagate-profile revoke upcloud-db1
docker exec openvpn-udp datagate-profile node-id     # key fingerprint to compare in the dashboard
```

* `--service` adds `pull-filter ignore "redirect-gateway" | "dhcp-option" | "block-outside-dns"`: only VPN-internal
  traffic uses the tunnel. `--static-ip` writes `ifconfig-push <ip> <netmask>` to ccd (must be inside `VPN_SUBNET`,
  not network/broadcast/server address, not used by another CN in ccd, `ipp.txt` or by a client connected right
  now per the management interface). Prefer addresses at the top of the subnet — the dynamic pool hands out from
  the bottom and does not know about ccd addresses, so a later dynamic client can still be given the same one.
* `revoke`/`get` go by serial number, not CN (easy-rsa allows the same CN several times in `index.txt`): a profile
  already revoked from the dashboard is only recorded, and if the CN was re-issued since, the newer certificate
  is never touched.
* **Channel:** a Unix socket in the 0700 root-owned dir — not loopback, because with `network_mode: host` every
  process reaches 127.0.0.1 (and with `OPENVPN_WSS_UDP_PROXY=rust` all public traffic reaches Kestrel from
  loopback). `/api/local-admin/*` answers 404 on the TCP port, with or without a JWT.
* **Backend trust:** the node signs events with its own ECDSA P-256 key (`node-identity.key`, never leaves the
  node). The public key is sent with the discovery announce and becomes trusted when an admin approves the node
  (the review page shows its fingerprint). For a node registered earlier, the backend fetches the key via
  `GET /api/node-identity` with its JWT; if the node is unreachable (NAT), trust the pending key via
  `POST /api/open-vpn-servers/{id}/node-key/trust {"fingerprint": "..."}` after comparing with `node-id`.
* **Delivery:** events are fsync'ed to `outbox/` and retried with backoff until the backend acknowledges them;
  container restarts lose nothing, and the backend is idempotent by certificate serial.

## ⚠ Notes

* Requires `--cap-add=NET_ADMIN` to allow iptables setup
* Container exposes OpenVPN on configured port/protocol
* Designed to be used with external volume (`-v`) for persistence

---

## 🧪 Example `docker-compose.yml`

```yaml
services:
  openvpn_udp:
    image: imkolganov/datagate-monitor-openvpn:latest
    pull_policy: always
    container_name: openvpn_udp
    restart: unless-stopped
    networks:
      - backend_network
    cap_add:
      - NET_ADMIN
    devices:
      - "/dev/net/tun:/dev/net/tun"
    volumes:
      - openvpn_data_udp:/openvpn-udp
    ports:
      - "1194:1194/udp"
      - "5010:5010"
    environment:
      EASY_RSA_PATH: "/openvpn-udp/easy-rsa"
      DATA_DIR: /openvpn-udp
      PORT: "1194"
      API_PORT: 5010
      PROTO: udp
      # OPENVPN_WSS_UDP_PROXY: rust   # optional: native UDP WSS datapath (default: dotnet)
      OpenVpnManagement__Port: "5092"
      OpenVpnManagement__Host: "localhost"
      BACKEND__BASEURL: "http://backend:5581/"
```

---

## 👤 Maintainer

Created by **Ivan Kolganov** based on Kyle Manna’s OpenVPN concepts.

| Contact | Link |
|---------|------|
| <picture><source media="(prefers-color-scheme: dark)" srcset="https://cdn.simpleicons.org/github/ffffff"><img src="https://cdn.simpleicons.org/github/181717" width="16" height="16" alt=""></picture> **Repository** | [IMKolganov/DataGateOpenVpnManager](https://github.com/IMKolganov/DataGateOpenVpnManager) |
| <img src="https://api.iconify.design/simple-icons/linkedin.svg?color=%230A66C2" width="16" height="16" alt="" /> **LinkedIn** | [linkedin.com/in/imkolganov](https://www.linkedin.com/in/imkolganov/?locale=en) |
| <img src="https://cdn.simpleicons.org/telegram/26A5E4" width="16" height="16" alt="" /> **Telegram** | [@KolganovIvan](https://t.me/KolganovIvan) |
| <img src="https://cdn.simpleicons.org/buymeacoffee/FFDD00" width="16" height="16" alt="" /> **Buy Me a Coffee** | [buymeacoffee.com/imkolganov](https://buymeacoffee.com/imkolganov) |

---

## 📄 License

MIT License
.