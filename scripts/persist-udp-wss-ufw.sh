#!/usr/bin/env bash
# Persist udp-wss UFW rules on dg-telegrambot-style hosts.
# Run: sudo ./scripts/persist-udp-wss-ufw.sh

set -euo pipefail

UFW_BEFORE="/etc/ufw/before.rules"
INPUT_MARKER="# datagate-udp-wss-tun1-input"
FWD_MARKER="# datagate-udp-wss-tun1-forward"

if [ "$(id -u)" -ne 0 ]; then
  echo "Run as root: sudo $0"
  exit 1
fi

cp -a "$UFW_BEFORE" "${UFW_BEFORE}.bak.$(date +%Y%m%d-%H%M%S)"

python3 <<'PY'
from pathlib import Path

path = Path("/etc/ufw/before.rules")
text = path.read_text()
input_marker = "# datagate-udp-wss-tun1-input"
fwd_marker = "# datagate-udp-wss-tun1-forward"

fwd_rules = [
    fwd_marker,
    "-A ufw-before-forward -i tun1 -o tun0 -j ACCEPT",
    "-A ufw-before-forward -i tun0 -o tun1 -j ACCEPT",
    "-A ufw-before-forward -i tun1 -o eth0 -j ACCEPT",
    "-A ufw-before-forward -i eth0 -o tun1 -j ACCEPT",
]

input_rules = [
    input_marker,
    "-A ufw-before-input -i tun1 -p udp --dport 53 -j ACCEPT",
    "-A ufw-before-input -i tun1 -p tcp --dport 53 -j ACCEPT",
]

# Remove mistaken forward rules from *nat block
lines = text.splitlines(keepends=True)
out, in_nat = [], False
for line in lines:
    if line.startswith("*nat"):
        in_nat = True
    if in_nat and line.strip() == "COMMIT":
        in_nat = False
    if in_nat and "ufw-before-forward" in line:
        continue
    if in_nat and fwd_marker in line:
        continue
    out.append(line)
text = "".join(out)

if input_marker not in text:
    needle = "-A ufw-before-input -j ufw-not-local\n"
    block = "".join(r + "\n" for r in input_rules) + "\n"
    if needle not in text:
        raise SystemExit("Could not find ufw-not-local anchor in before.rules")
    text = text.replace(needle, block + needle, 1)

lines = text.splitlines(keepends=True)
out, in_filter, inserted_fwd = [], False, fwd_marker in text
for line in lines:
    if line.startswith("*filter"):
        in_filter = True
    if in_filter and not inserted_fwd and line.strip() == "COMMIT":
        for rule in fwd_rules:
            out.append(rule + "\n")
        out.append("\n")
        inserted_fwd = True
        in_filter = False
    out.append(line)

path.write_text("".join(out))
print("OK: before.rules updated")
PY

ufw reload
echo "=== verify ==="
grep -nE 'datagate-udp-wss|tun1.*53' "$UFW_BEFORE"
iptables -L ufw-before-input -v -n | grep -E 'tun1|dpt:53' || true
iptables -L ufw-before-forward -v -n | grep tun1 || true
