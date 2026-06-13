# DataGateOpenVpnManager + OpenVPN Docker Image

OpenVPN sidecar for [DataGate Monitor](https://dash.datagateapp.com/). In the monorepo this is the `openvpn/` submodule ([DataGateMonitor](https://github.com/IMKolganov/DataGateMonitor)). Standalone clone: [DataGateOpenVpnManager](https://github.com/IMKolganov/DataGateOpenVpnManager).

**Links:** [DataGate](https://datagateapp.com/) · [Download](https://datagateapp.com/download) · [Telegram @datagateapp](https://t.me/datagateapp)

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
| `VPN_SUBNET`, `VPN_NETMASK` | VPN subnet config                  | `10.51.28.0/24`      |
| `OpenVpnManagement__Port`   | OpenVPN management interface port  | `5092`               |

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

---

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
      OpenVpnManagement__Port: "5092"
      OpenVpnManagement__Host: "localhost"
      BACKEND__BASEURL: "http://backend:5581/"
```

---

## 👤 Maintainer

Created by **Ivan Kolganov** based on Kyle Manna’s OpenVPN concepts.

- GitHub: [IMKolganov/DataGateOpenVpnManager](https://github.com/IMKolganov/DataGateOpenVpnManager)
- [LinkedIn](https://www.linkedin.com/in/imkolganov/?locale=en) · [Telegram](https://t.me/KolganovIvan) · [Buy Me a Coffee](https://buymeacoffee.com/imkolganov)

---

## 📄 License

MIT License
.