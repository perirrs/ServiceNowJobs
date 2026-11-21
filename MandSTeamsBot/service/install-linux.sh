#!/usr/bin/env bash
# Installs or updates the bot on an Ubuntu server as a systemd service.
# Copy package.json, package-lock.json, dist/, .env and service/ to /opt/mandsteamsbot first, then run:
#   sudo bash /opt/mandsteamsbot/service/install-linux.sh
set -euo pipefail
APP=/opt/mandsteamsbot

if ! command -v node >/dev/null || [ "$(node -p 'process.versions.node.split(".")[0]')" -lt 24 ]; then
  curl -fsSL https://deb.nodesource.com/setup_24.x | bash -
  apt-get install -y nodejs
fi

mkdir -p "$APP/data"
chown -R mands:mands "$APP"
chmod 600 "$APP/.env"
cd "$APP"
sudo -u mands npm ci --omit=dev --no-audit --no-fund

# Rotate the log when it passes 5 MB.
cat > /etc/logrotate.d/mandsteamsbot <<'EOF'
/opt/mandsteamsbot/data/bot.log {
  size 5M
  rotate 3
  copytruncate
  missingok
  notifempty
}
EOF

cp "$APP/service/mandsteamsbot.service" /etc/systemd/system/mandsteamsbot.service
systemctl daemon-reload
systemctl enable mandsteamsbot
systemctl restart mandsteamsbot
systemctl --no-pager status mandsteamsbot | head -5
