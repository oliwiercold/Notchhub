# KDE Connect protocol, as implemented by `PhoneService`

These notes follow kdeconnect-kde master (`core/backends/lan/lanlinkprovider.cpp`, `core/deviceinfo.h`, `core/device.cpp`) and protocol version 8.

## Packets
* One JSON object per line, ended by `\n`: `{"id": <ms>, "type": "kdeconnect.<x>", "body": {...}}`.
  A packet with a payload also has `"payloadSize": N, "payloadTransferInfo": {"port": P}`.
* Values are read leniently, because the phone sometimes sends numbers and booleans as strings (`time`, `silent`, `id`).

## Identity
* `deviceId` must match `^[a-zA-Z0-9_-]{32,38}$`. Ours is a GUID with no dashes (32 hex characters), stored in `kdeconnect/identity.json`.
* `deviceName` has `"',;:.!?()[]<>` removed and is cut to 32 characters. It must not be empty in a full identity.
* Full identity (sent over TLS): `deviceId, deviceName, deviceType="desktop", protocolVersion=8, incomingCapabilities, outgoingCapabilities`.
* UDP announce: the full identity plus `tcpPort`.
* Plain-text TCP packet ("connection packet"): the full identity plus `targetDeviceId` and `targetProtocolVersion`. kdeconnect-kde master requires these two fields.
* Capabilities:
  * Incoming: battery, notification, connectivity_report, clipboard, clipboard.connect, ping.
  * Outgoing: battery.request, notification.request/.reply/.action, findmyphone.request, ping, clipboard, connectivity_report.request.

## Discovery and links
1. We listen on UDP 1716 with address reuse. If the bind fails, discovery still works through our own broadcasts.
2. We listen on TCP at the first free port in 1716–1764.
3. Every 2 minutes, after a network change (debounced by 3 s) and on `RefreshAsync`, we broadcast our UDP identity:
   * from every IPv4 interface to 255.255.255.255 and to the subnet broadcast address;
   * by unicast to the last known address of each paired device that is offline.
4. When a device's UDP identity arrives (protocol 8 or higher, `tcpPort` in 1716–1764), we connect to that port. **The side that opened the TCP connection sends its identity in plain text and then becomes the TLS SERVER. The accepting side is the TLS CLIENT.**
   If the connection fails, we send a UDP identity back so the other side connects to us instead.
5. Both sides send a certificate. For an unpaired device we accept any certificate. For a paired device we require the exact DER we stored.
   After TLS, each side sends its full identity again. The `deviceId` and `protocolVersion` must not change, and the certificate CN must equal the `deviceId`.
6. Each device has one active link. A new link replaces the old one.
   TCP keepalive is set to 30 s idle, 10 s interval and 3 probes. A failed send or read closes the link.
7. Payloads (notification icons): we connect to `phoneIP:port` and act as the TLS **client**, then read exactly `payloadSize` bytes. Payloads are capped at 2 MB and time out after 15 s.

## Certificates
* Self-signed RSA-2048 with SHA-256. Subject: `CN=<deviceId>, O=KDE, OU=Kde connect`. Valid from 1 year in the past to 10 years ahead.
* Stored in `kdeconnect/certificate.pfx` and re-imported from the PFX with a persisted key, which SChannel needs.

## Pairing (`kdeconnect.pair`)
* Request: `{"pair": true, "timestamp": <unix s>}`. Accept: `{"pair": true}`. Reject or unpair: `{"pair": false}`.
* We reject an incoming request whose timestamp differs from our clock by more than 1800 s.
* A request that is not answered within 30 s is cancelled, and we send `{"pair": false}`.
* If an unpaired device sends any packet other than a pair packet, we reply with `{"pair": false}`. If a device we consider paired sends `pair: true`, it has forgotten us: we drop the stored trust and treat it as a new request.
* Verification key: SHA-256 over the two SubjectPublicKeyInfo DERs, with the lexicographically larger one first, followed by the timestamp as ASCII decimal. We show the first 8 hex characters in uppercase.
  * Source of this rule: the current `pairinghandler.cpp` was not reachable (404), so the rule follows the known upstream code and was not checked against it.
* Paired devices are stored in `kdeconnect/devices.json`: id, name, type, certificate DER, last address and protocol version.

## Plugins used
| packet | direction | handling |
|---|---|---|
| `kdeconnect.battery` `{currentCharge, isCharging, thresholdEvent}` | in | `Battery`. Low when `thresholdEvent == 1`, or when not charging and at 15 % or less |
| `kdeconnect.battery.request {request:true}` | out | sent on connect and on refresh |
| `kdeconnect.connectivity_report {signalStrengths:{sub:{networkType, signalStrength}}}` | in | `Signal` from the first subscription in key order |
| `kdeconnect.notification` | in | add or replace by `id`. `isCancel` removes it. The icon payload goes to `IconPng`. `NotificationPosted` fires for new notifications that are not silent |
| `kdeconnect.notification.request` `{request:true}` / `{cancel:id}` | out | request all notifications / dismiss one |
| `kdeconnect.notification.reply {requestReplyId, message}`, `.action {key, action}` | out | reply / invoke an action |
| `kdeconnect.findmyphone.request {}`, `kdeconnect.ping {message?}` | out | ring / ping |
| `kdeconnect.clipboard {content}`, `kdeconnect.clipboard.connect {content, timestamp}` | in/out | `ClipboardReceived`. A `clipboard.connect` with timestamp 0 is ignored |
