# 🌌 Limbo

<div align="center">

### DPI Bypass Engine with Per-Domain Profiles

> **Named after Inception's deepest dream layer — where DPI gets lost.**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![License](https://img.shields.io/badge/License-GPL%20v2-blue?style=for-the-badge)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?style=for-the-badge&logo=windows)](https://www.microsoft.com/windows)

</div>

---

## 🎯 What is Limbo?

Limbo is a next-generation DPI (Deep Packet Inspection) bypass engine featuring:

- **Per-Domain Profiles** — Different bypass strategies for different sites
- **7 Bypass Strategies** — TCP Fragmentation, Smart Fragmentation, Desync, Fake TTL, UDP Fake, ECH, QUIC Fake
- **🔐 ECH (Encrypted Client Hello)** — Encrypts SNI so DPI cannot see target domain
- **🛰 Radar System** — Auto-detects blocked domains and creates bypass profiles automatically
- **ISP Behavior Analysis** — Detects RST injection, silent drops, DNS poisoning
- **Automatic Calibration** — Finds the best bypass method per domain
- **SIMD Optimization** — AVX2-accelerated packet parsing
- **DNS over HTTPS** — Bypass ISP DNS blocking
- **Modern GUI** — Dark/Light theme WPF application with real-time stats
- **Full CLI** — All features available from the command line

---

## 🛰 Radar System

Limbo includes an automatic blocking detection system called **Radar**:

```
Packet Analysis
    ↓
IspBehaviorAnalyzer          ConnectionTracker
 (RST injection)      +       (3s silent drop)
    ↓                               ↓
         BlacklistManager (Auto-Blacklist.txt)
                    ↓
         Auto Adaptive profile created
                    ↓
         GUI / CLI reflects new domain
```

When Limbo detects that your ISP is blocking a domain, it automatically:
1. Adds it to the radar blacklist
2. Creates an **Adaptive** bypass profile for it
3. Starts applying bypass immediately

---

## 🔧 Bypass Strategies

| Strategy | Priority | Mechanism | Best For |
|----------|----------|-----------|----------|
| **ECH** | 200 | Encrypts SNI completely via HPKE | Cloudflare sites |
| **QuicFake** | 150 | Fake datagram injection for QUIC | HTTP/3, Discord Voice |
| **TcpFragmentation** | 100 | Splits TCP payload into small segments | General DPI |
| **SmartFragmentation** | 90 | Splits at exact SNI offset (SIMD) | Minimal packets |
| **Desync** | 80 | Sends packets out of order | State-based DPI |
| **FakeTtl** | 70 | Decoy packets with low TTL | Passive DPI |
| **UdpFake** | 60 | Fake packets for UDP/QUIC traffic | Voice/Video |
| **Adaptive** | — | Tests all strategies, saves the best | Unknown sites |

---

## 🏗 Architecture

```
Limbo/
├── src/
│   ├── Limbo.Console/          # CLI application
│   ├── Limbo.GUI/              # WPF Desktop Application
│   ├── Limbo.Core/             # Main business logic
│   │   ├── Analysis/           # Radar: ISP behavior detection, blacklist
│   │   ├── Bypass/             # 7 DPI bypass strategies
│   │   ├── Config/             # JSON configuration system
│   │   ├── Detection/          # SIMD packet inspection
│   │   ├── Dns/                # DNS over HTTPS + cache
│   │   ├── Ech/                # ECH (X25519 + AES-GCM)
│   │   ├── Engine/             # Orchestration + Calibration
│   │   ├── Logging/            # Thread-safe structured logging
│   │   ├── Monitoring/         # Real-time bandwidth monitor
│   │   └── Profiles/           # Per-domain wildcard profiles
│   └── Limbo.Driver/           # WinDivert P/Invoke wrapper
└── tests/
    └── Limbo.Tests/            # xUnit unit tests (48 tests)
```

---

## 📥 Downloads

Pre-built binaries are available on the [Releases](https://github.com/umitkrkmz/Limbo/releases) page.

| Package | Description |
|---------|-------------|
| `Limbo-Console-v1.0-win-x64.zip` | CLI application (self-contained, no .NET install needed) |
| `Limbo-GUI-v1.0-win-x64.zip` | WPF desktop application (self-contained, no .NET install needed) |

> **After downloading:** Place `WinDivert.dll` and `WinDivert64.sys` next to the executable before running. See [WinDivert Setup](#%EF%B8%8F-windivert-setup-required) below.

> **SHA-256 checksums** are listed in each release's description for verification.

---

## 🚀 Quick Start

### Prerequisites

- Windows 10/11 64-bit
- .NET 10.0 Runtime
- **Administrator privileges required**
- WinDivert 2.2.2 (see below)

### ⚠️ WinDivert Setup (Required)

Limbo uses WinDivert for low-level packet interception via P/Invoke. WinDivert is licensed separately (LGPL v3 / GPL v2) and is **not included** in this repository or release ZIP — you must obtain and place the files manually in both cases.

> **Building from source?** Place `WinDivert.dll` and `WinDivert64.sys` into `src/Limbo.Driver/runtimes/win-x64/native/` before building. MSBuild will automatically copy them to the output directory.

**Step 1 — Download WinDivert 2.2.2:**

> https://github.com/basil00/WinDivert/releases/tag/v2.2.2

Download `WinDivert-2.2.2-A.zip` and extract it.

**Step 2 — Copy the files next to the executable:**

From the extracted ZIP, copy the `x64` variants:

```
WinDivert-2.2.2-A/
└── x64/
    ├── WinDivert.dll      ← copy this
    └── WinDivert64.sys    ← copy this
```

Place both files in the same folder as `Limbo.Console.exe` or `Limbo.GUI.exe`:

```
# Console (extracted ZIP):
Limbo.Console.exe
WinDivert.dll        ← here
WinDivert64.sys      ← here

# GUI (extracted ZIP):
Limbo.GUI.exe
WinDivert.dll        ← here
WinDivert64.sys      ← here
```

> **Why 2.2.2 specifically?** Limbo's P/Invoke declarations (`WinDivertNative.cs`) target the WinDivert 2.2 API. Older versions (1.x, 2.0) have different function signatures and **will not work**. Newer minor versions (2.2.x) should be compatible.

### Build from Source

```bash
git clone https://github.com/umitkrkmz/Limbo.git
cd Limbo
dotnet build -c Release
```

### Run GUI (Recommended)

```powershell
# Run as Administrator
dotnet run --project src/Limbo.GUI
```

### Run CLI

```powershell
# Run as Administrator
dotnet run --project src/Limbo.Console -- --help
```

---

## 🖥 CLI Usage

```
Usage: limbo [command] [options]

Commands:
  run                          Start DPI bypass engine
  calibrate <domain>           Find best bypass method for a domain
  test <domain>                Test DNS, DoH, TCP connectivity
  add-profile <domain> <method> [opts]   Add a domain profile
  remove-profile <domain>      Remove a domain profile
  radar                        Show auto-detected blocked domains
  radar-import <file>          Import a radar blacklist
  radar-export <file>          Export radar blacklist
  radar-clear                  Clear all radar entries

Options for add-profile:
  --frag <n>       Fragment size (TcpFragmentation / SmartFragmentation)
  --ttl <n>        Fake TTL value (FakeTtl / UdpFake)
  --split <n>      Split position (Desync)
  --offset <n>     SNI split offset (SmartFragmentation)
  --ech-name <s>   ECH public name (Ech)
  --quic-size <n>  Fake packet size (QuicFake)

Global options:
  -v, --verbose    Verbose logging
  -c, --config     Custom config file path
  --list-profiles  List all profiles
  --list-methods   List bypass methods with priorities
  --show-config    Show current configuration
  --version        Version info
```

**Examples:**

```powershell
# Start bypass engine with verbose output
limbo run -v

# Find best method for a domain
limbo calibrate discord.com

# Add a profile manually
limbo add-profile "*.discord.com" TcpFragmentation --frag 2

# Test if a domain is blocked
limbo test twitter.com

# Show radar-detected blocked domains
limbo radar

# Import a community blocklist
limbo radar-import blocked.txt
```

---

## 🖥 GUI Features

### Profile Management
- **Add Profile** — Domain pattern + bypass method + all parameters
- **Edit Profile** — Right-click or double-click to modify
- **Delete Profile** — X button or right-click context menu
- **Import/Export** — JSON profile sharing
- **Radar Import/Export** — Share auto-detected blocked domain lists

### Real-time Statistics
- Packets captured / matched / modified
- Data processed
- Live download / upload rate + peak rates

### Activity Log
- Color-coded live log (green = success, red = error)
- Radar events (auto-detected blocks, silent drops)

### Settings
- DNS over HTTPS toggle + server URL
- Theme: System / Light / Dark
- System tray: minimize to tray, close to tray, start minimized

---

## ⚙️ Configuration

Config file: `%LOCALAPPDATA%\Limbo\config.json`

```json
{
  "filter": "tcp or udp",
  "verbose": false,
  "dns": {
    "useDoH": true,
    "doHServer": "https://1.1.1.1/dns-query"
  },
  "theme": "System",
  "minimizeToTray": true,
  "closeToTray": true,
  "profiles": [
    {
      "domain": "*.discord.com",
      "method": "TcpFragmentation",
      "fragmentSize": 2
    },
    {
      "domain": "*.twitter.com",
      "method": "Ech"
    }
  ]
}
```

**Data files:**
- Profiles: `%LOCALAPPDATA%\Limbo\profiles\*.json`
- Radar blacklist: `%LOCALAPPDATA%\Limbo\Auto-Blacklist.txt`

---

## 🌐 DNS over HTTPS

Supported servers:
- Cloudflare: `https://1.1.1.1/dns-query`
- Google: `https://dns.google/dns-query`
- Quad9: `https://dns.quad9.net/dns-query`

Limbo runs parallel A + AAAA queries and prioritizes IPv6 addresses.

---

## 📊 Compared to GoodbyeDPI

| Feature | GoodbyeDPI | Limbo |
|---------|------------|-------|
| Target | All traffic, global options | Per-domain profiles |
| ECH Support | ⚠️ ECH grease only (fake packets) | ✅ Full X25519 HPKE (real ECH) |
| Profile System | No (global flags only) | ✅ Wildcard patterns per domain |
| Radar (auto-detect) | No | ✅ Yes |
| ISP Behavior Analysis | No | ✅ RST injection, silent drops |
| Automatic Calibration | No | ✅ Per domain |
| Bandwidth Monitoring | No | ✅ Real-time |
| DNS Bypass | ⚠️ UDP redirect only (no DoH) | ✅ DNS over HTTPS (DoH) |
| SIMD Acceleration | No | ✅ AVX2 |
| GUI | No | ✅ Dark/Light WPF |
| CLI | ✅ Yes | ✅ Yes |
| Language | C (C99) | C# (.NET 10) |
| Platform | Windows | Windows |
| License | Apache 2.0 | GPL v2 |

Limbo is **inspired by GoodbyeDPI** but approaches the problem differently: instead of one global configuration, it uses per-domain strategies with automatic detection and adaptive calibration.

---

## 🗺 Roadmap

### ✅ Completed (v1.0)
- [x] 7 bypass strategies + Adaptive mode
- [x] ECH (Encrypted Client Hello) — full X25519 HPKE
- [x] QUIC / HTTP3 bypass
- [x] Radar system — auto-detect + auto-blacklist
- [x] ISP behavior analysis (RST injection, silent drops)
- [x] Per-domain profiles with wildcard support
- [x] Automatic calibration per domain
- [x] Profile import / export (JSON)
- [x] System tray support
- [x] Real-time bandwidth monitoring
- [x] DNS over HTTPS (parallel A + AAAA)
- [x] SIMD (AVX2) packet parsing
- [x] Dark / Light / System theme
- [x] Full CLI (run, calibrate, test, radar, add/remove-profile)
- [x] 48 unit tests


---

## 🤝 Contributing

Contributions are welcome! Here's how:

1. **Fork** this repository
2. **Create a branch** — `git checkout -b feature/my-feature`
3. **Commit your changes** — `git commit -m "Add: my feature"`
4. **Push** — `git push origin feature/my-feature`
5. **Open a Pull Request**

**Good first issues:**
- ISP fingerprint database entries
- New bypass strategy ideas
- Test coverage improvements
- Documentation / translations

Please open an **Issue** before working on large changes so we can discuss the approach first.

---

## ⚠️ Disclaimer

> This software is for **educational and research purposes only**.
> Usage is at your own risk. Check your local laws before use.
> The authors are not responsible for any misuse.

---

## 🙏 Acknowledgements

- **[basil00/WinDivert](https://github.com/basil00/WinDivert)** — Windows network packet capture and modification driver. Limbo uses WinDivert for low-level packet interception. Licensed under LGPL v3 / GPL v2.
- **[ValdikSS/GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI)** — Original inspiration for DPI bypass techniques. Limbo takes a different architectural approach (per-domain adaptive profiles, Radar system, ECH) but the foundational ideas come from GoodbyeDPI's excellent work. Licensed under Apache 2.0.

---

## 💬 Community

Have a question, idea, or want to share how you're using Limbo?

- **[GitHub Discussions](https://github.com/umitkrkmz/Limbo/discussions)** — Questions, ideas, show & tell
- **[GitHub Issues](https://github.com/umitkrkmz/Limbo/issues)** — Bug reports and feature requests

---

## 📄 License

This project is licensed under the **GNU General Public License v2.0**.

> Because Limbo uses [WinDivert](https://github.com/basil00/WinDivert) which is dual-licensed under LGPL v3 / GPL v2, Limbo is distributed under **GPL v2** to maintain license compatibility.

See [LICENSE](LICENSE) for the full license text.

---

<div align="center">

**⭐ Star this repo if you find it useful!**

*Designed & Engineered by [Umit Korkmaz](https://github.com/umitkrkmz)*

</div>
