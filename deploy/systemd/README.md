# Debian systemd deployment

The laboratory server is an unauthenticated trusted-network application. Keep the default loopback bind unless access is deliberately restricted by a trusted firewall or reverse proxy.

Run the following from the repository root on Debian with the .NET 10 runtime installed:

```sh
sudo useradd --system --home-dir /opt/rowles-morphogenesis --shell /usr/sbin/nologin morphogenesis
sudo install -d -o morphogenesis -g morphogenesis /opt/rowles-morphogenesis
sudo install -d -o morphogenesis -g morphogenesis /var/lib/rowles-morphogenesis
sudo install -d -o morphogenesis -g morphogenesis /var/log/rowles-morphogenesis

dotnet publish tools/Rowles.Morphogenesis.Server/Rowles.Morphogenesis.Server.csproj \
  --configuration Release \
  --output ./artifacts/rowles-morphogenesis-server
sudo cp -a ./artifacts/rowles-morphogenesis-server/. /opt/rowles-morphogenesis/
sudo chown -R morphogenesis:morphogenesis /opt/rowles-morphogenesis

sudo install -m 0644 deploy/systemd/rowles-morphogenesis.service \
  /etc/systemd/system/rowles-morphogenesis.service
sudo systemctl daemon-reload
sudo systemctl enable rowles-morphogenesis
sudo systemctl start rowles-morphogenesis
sudo systemctl status rowles-morphogenesis --no-pager
sudo journalctl -u rowles-morphogenesis -n 100 --no-pager
```

To bind to a trusted LAN address, create a systemd override with `sudo systemctl edit rowles-morphogenesis` and set, for example:

```ini
[Service]
Environment=Laboratory__BindUrl=http://192.168.1.10:5080
```

Replace the address with the server's trusted LAN address, then run `sudo systemctl daemon-reload` and `sudo systemctl restart rowles-morphogenesis`. The server has no authentication; do not expose it to an untrusted network or the public internet.
