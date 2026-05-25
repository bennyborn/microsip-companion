# MicroSIP Companion

Answer and hang up MicroSIP calls from your phone. No app, no pairing, no nonsense.

<img src="logo.png" alt="MicroSIP Companion tray UI" width="250">

MicroSIP is solid until a call rings while you're soldering or across the room. This sits in your tray, starts a tiny web server on your LAN, and lets you answer or hang up from any browser on your phone. Right-click the tray icon for a QR code.

No app to install on the phone, no account, no cloud. Works on any modern Windows (.NET 4.8 ships with it).

## Download

Latest build on the [Releases](../../releases) page. Extract anywhere, run `MicroSIPCompanion.exe`.

## Usage

1. Start MicroSIP as usual.
2. Run `MicroSIPCompanion.exe`. A balloon notification shows the URL, e.g. `http://192.168.1.42:8765/`.
3. Open that URL on your phone, or right-click the tray icon → **URL / QR Code…** to scan it.
4. Tap **Answer** or **Hang Up**. Page updates instantly via push.

### Configuration

`MicroSIPCompanion.ini` is created on first run next to the exe:

```ini
[Server]
Port=8765

[MicroSIP]
; Leave empty for auto-detection
ExePath=
```

Change `Port` if 8765 is taken. Restart to apply.

## How It Works

**Call detection:** polls MicroSIP every 500 ms via `EnumWindows`. Window titles with *"Incoming"* or *"Ringing"* mean incoming; a colon (the timer `00:12`) or *"Calling"* means active. No hooks, no DLL injection.

**HTTP server:** raw `TcpListener`, endpoints:

| Endpoint | Method | |
|---|---|---|
| `/` | GET | Mobile web UI |
| `/events` | GET | Server-Sent Events stream — pushes state changes to the browser |
| `/state` | GET | Current call state as JSON (for polling / debugging) |
| `/answer` | POST | Triggers `microsip.exe /answer` |
| `/hangupincoming` | POST | Triggers `microsip.exe /hangupincoming` (decline ringing call) |
| `/hangupall` | POST | Triggers `microsip.exe /hangupall` (end confirmed/active call) |

**Push updates:** the browser opens a persistent SSE connection to `/events`. State changes are pushed immediately; no client-side polling. The browser reconnects automatically if the connection drops.

**Control:** via MicroSIP's own CLI args. The running process is auto-detected; no path config needed in most cases.

## Build

Requires the [.NET SDK](https://dotnet.microsoft.com/download) (any recent version, targets .NET Framework 4.8).

```bat
build.bat
```

Or: `dotnet build MicroSIPCompanion.csproj -c Release`. Output in `bin\Release\net48\`.