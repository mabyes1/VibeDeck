# VibeDeck Remote Access

VibeDeck can accept clients outside the Host PC's local network. The Host already binds to `0.0.0.0`; remote access adds a password-protected session for the web UI, API, WebSocket display stream, and PC input controls.

## Enable the password

Set the password before starting the Host. Environment variables are recommended:

```powershell
$env:VIBEDECK_REMOTE_PASSWORD = "use-a-long-unique-password"
scripts\dev-run.ps1
```

The equivalent JSON setting is `RemoteAccess:Password` in `appsettings.json`. Do not commit a real password. Passwords are converted to a salted PBKDF2 verifier in memory; the plain password is not persisted.

## Connect from another network

Use one of these transport paths:

- Tailscale, ZeroTier, or another private VPN (recommended).
- A reverse proxy or tunnel that forwards to the Host HTTPS port `5443`.
- Router port forwarding to `5443`, only with a firewall rule and a trusted certificate / domain.

Open the Host URL from the remote phone. The page asks for the remote password. Remote login is rejected over plain HTTP so the password and session cookie are not sent unencrypted.

For Tailscale, install and sign in on **both** the Host PC and the phone, then open the PC's `100.x.y.z` Tailscale address. Installing Tailscale only on the phone is not enough unless another Tailscale device is configured as a subnet router for the PC's LAN. VibeDeck accepts Tailscale's `100.64.0.0/10` address range for the local pairing approval flow.

Local-network devices use the HTTPS QR → phone request → PC approval flow. A password-authenticated remote session can use the same protected API and display WebSockets without device pairing.

## Notes

- The bundled local certificate is intended for trusted personal networks. For Internet exposure, use a VPN or a reverse proxy with a publicly trusted certificate.
- Do not expose port `5000` to the Internet; it is the plain HTTP bootstrap listener.
- A failed-password address is temporarily locked after five failed attempts.
