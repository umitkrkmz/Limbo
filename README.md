# 🌌 Limbo

<div align="center">

### DPI Bypass Engine with Per-Domain Profiles

> **Named after Inception's deepest dream layer — where DPI gets lost.**

[![Build & Test](https://github.com/umitkrkmz/Limbo/actions/workflows/build.yml/badge.svg)](https://github.com/umitkrkmz/Limbo/actions/workflows/build.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![License](https://img.shields.io/badge/License-GPL%20v3-blue?style=for-the-badge)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?style=for-the-badge&logo=windows)](https://www.microsoft.com/windows)

**🇬🇧 English** | [🇹🇷 Türkçe](README.tr.md)

</div>

---

## 🎯 What is Limbo?

Limbo is a next-generation DPI (Deep Packet Inspection) bypass engine featuring:

- **Per-Domain Profiles** — Different bypass strategies for different sites
- **7 Bypass Strategies** — TCP Fragmentation, Smart Fragmentation, Desync, Fake TTL, UDP Fake, ECH, QUIC Fake
- **🔐 ECH (Encrypted Client Hello)** — Encrypts SNI so DPI cannot see target domain
- **🛰 Radar System** — Auto-detects blocked domains and creates bypass profiles automatically
- **🌍 Global Mode** — GoodbyeDPI-style: bypass every HTTPS/HTTP connection, not just profiled domains (profiles still override; method `None` excludes a site)
- **📡 Network Analysis** — Identifies your ISP and runs a live scan reporting DNS tampering, RST injection, or silent dropping for a domain
- **ISP Behavior Analysis** — Detects RST injection, silent drops, DNS poisoning
- **Automatic Calibration** — Actually applies each strategy through a scoped live capture to find what really works per domain, not a simulation
- **SIMD Optimization** — AVX2-accelerated packet parsing
- **DNS over HTTPS** — Answers every app's DNS queries (IPv4 + IPv6) over encrypted DoH while the engine runs, defeating ISP DNS poisoning without changing system settings
- **Interactive Console** — Spectre.Console-powered menu with live traffic dashboard, plus a scriptable CLI

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
         Console reflects new domain
```

When Limbo detects that your ISP is blocking a domain, it automatically:
1. Adds it to the radar blacklist
2. Creates a bypass profile for it (method configurable — defaults to **Adaptive**)
3. Starts applying bypass immediately

### 🌍 Global Mode (GoodbyeDPI-style)

By default Limbo only touches domains that have a profile (or that Radar detected). **Global mode** works the way GoodbyeDPI does instead: every HTTPS/HTTP connection without a profile gets the **default method** (the same one Radar uses, set in **Adaptive Engine (Radar) → Change Default Method**; `Adaptive` behaves as TCP fragmentation).

- **Profiles still take priority**, so per-domain tuning keeps working.
- **Excluding a site that global mode breaks:** give it a profile with method `None`, e.g. `limbo add-profile *.mybank.example None`.
- Turn it on in **Adaptive Engine (Radar) → Turn On Global Mode**, set `"globalMode": true` in the config, or use it for a single run with `limbo run --global`.
- DNS-over-HTTPS works the same in both modes.

---

## 📡 Network Analysis

Answers "what is my ISP actually doing to this connection?" From the interactive menu's **Network Analysis** screen:

- **Who you're connected through** — public IP, ISP/organization (ASN), and rough location, looked up on demand (no local network setting reveals which ISP owns your WAN connection)
- **Local network info** — active interface, system-configured DNS servers
- **A live behavior scan for one domain** (requires Administrator, same as `run`) — opens a scoped, read-only capture of just that domain's traffic and reports:
  - **DNS tampering** — system DNS vs DNS-over-HTTPS mismatch
  - **RST injection** — an inbound reset that looks artificially inserted (window size, TTL, RST+ACK heuristics)
  - **Silent dropping** — the request goes out, nothing ever comes back
  - **Reachable** — no interference detected

Without Administrator, only the ISP/network identity is shown — the live scan is skipped with a clear note rather than failing silently.

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
│   ├── Limbo.Console/          # Interactive + scriptable CLI application
│   ├── Limbo.Core/             # Main business logic
│   │   ├── Analysis/           # Radar, ISP behavior detection, network analysis, blacklist
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
    └── Limbo.Tests/            # xUnit unit tests
```

---

## 📥 Downloads

Pre-built binaries are available on the [Releases](https://github.com/umitkrkmz/Limbo/releases) page, built and published automatically by [`release.yml`](.github/workflows/release.yml).

| Package | When to use |
|---------|-------------|
| `Limbo-v<version>-setup.exe` | **Recommended.** Installs to Program Files, adds a Start menu entry, and can be removed from Add/Remove Programs. Uninstalling asks whether to also delete your settings, profiles and radar blacklist (`%LOCALAPPDATA%\Limbo`). |
| `Limbo-Console-v<version>-win-x64.zip` | **Portable.** Extract anywhere and run `Limbo.Console.exe`; keep the files together in one folder. |

Both are self-contained (no .NET install needed) and have **WinDivert bundled**. `Limbo.Console.exe` asks for Administrator rights itself (UAC prompt), since WinDivert needs them. The license files (`LICENSE.txt`, `THIRD-PARTY-NOTICES.txt`, `LICENSE-WinDivert.txt`) are included in both. You only need to fetch WinDivert yourself when [building from source](#build-from-source).

> **One interceptor at a time:** only one Limbo copy can run the engine, a calibration or a network scan at once; a second one refuses with a message (read-only commands like `--list-profiles` still work alongside it). Limbo also warns if its Discord-only sister app [ZeroDivert](https://github.com/umitkrkmz/ZeroDivert) is running, since both rewrite Discord's TLS handshakes; run one or the other.

> **SHA-256 checksums:** each release has a `.sha256` file next to the ZIP. Compare it with the output of `Get-FileHash <file> -Algorithm SHA256` in PowerShell.

> **Not code-signed:** if SmartScreen says "Windows protected your PC" on first launch, choose **More info → Run anyway**. Windows 11's **Smart App Control** blocks unsigned apps outright and offers no per-app exception; if it's set to *On* (Windows Security → App & browser control), Limbo won't start. Turning that feature off affects every app, not just Limbo, and may require resetting Windows to turn back on — weigh that before doing it, or [build from source](#build-from-source) instead.

---

## 🚀 Quick Start

### Prerequisites

- Windows 10/11 64-bit
- **Administrator privileges required** — `Limbo.Console.exe` requests them itself on launch
- Installer / release ZIP: nothing else (the .NET runtime and WinDivert are bundled)
- Building from source: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and WinDivert 2.2.2 (see [Build from Source](#build-from-source))

### Build from Source

WinDivert is licensed separately (LGPL v3 / GPL v2) and its binaries aren't committed to this repository, so a source build needs them first. (The installer and the release ZIP already include them.)

1. Clone the repository:
   ```bash
   git clone https://github.com/umitkrkmz/Limbo.git
   cd Limbo
   ```
2. Download `WinDivert-2.2.2-A.zip` from the [WinDivert 2.2.2 release](https://github.com/basil00/WinDivert/releases/tag/v2.2.2) and extract it.
3. Copy the two **x64** files into the project - MSBuild copies them next to the exe on every build:
   ```
   WinDivert-2.2.2-A/x64/WinDivert.dll    ->  src/Limbo.Driver/runtimes/win-x64/native/
   WinDivert-2.2.2-A/x64/WinDivert64.sys  ->  src/Limbo.Driver/runtimes/win-x64/native/
   ```
4. Build:
   ```bash
   dotnet build -c Release
   ```

> **Why 2.2.2 specifically?** Limbo's P/Invoke declarations (`WinDivertNative.cs`) target the WinDivert 2.2 API. Older versions (1.x, 2.0) have different function signatures and **will not work**. Newer minor versions (2.2.x) should be compatible.

To produce the same self-contained package as a release (single `Limbo.Console.exe` with the runtime bundled, plus WinDivert and the license files), use the publish profile:

```bash
dotnet publish src/Limbo.Console -p:PublishProfile=win-x64
```

Output lands in `src/Limbo.Console/bin/publish/win-x64/`.

**Installer (optional):** with [Inno Setup 6](https://jrsoftware.org/isinfo.php) installed, run this after the publish command above. The setup file lands in `installer\Output\`:

```powershell
# Inno Setup installed for all users:
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" /DAppVersion=1.1.0 installer\Limbo.iss
# Installed for your user only (e.g. winget --scope user):
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" /DAppVersion=1.1.0 installer\Limbo.iss
```

**Releasing:** bump `<Version>` in the three csproj files, add the version heading to the CHANGELOG, then push a `v`-prefixed tag (`git tag v1.2.0 && git push origin v1.2.0`). The [release workflow](.github/workflows/release.yml) checks the tag matches the version, builds the installer and the portable ZIP with their SHA-256s, and creates a **draft** GitHub Release; editing the notes and pressing **Publish** is up to you. Suffixed tags like `v1.2.0-rc.1` become pre-releases.

### Run

```powershell
# From an Administrator terminal - launches the interactive menu
dotnet run --project src/Limbo.Console

# Or use it as a scriptable CLI
dotnet run --project src/Limbo.Console -- --help
```

> `Limbo.Console.exe` carries a manifest that requires elevation, so `dotnet run` from a **non**-elevated terminal fails with "The requested operation requires elevation". Run the terminal as Administrator, or run the built DLL through the `dotnet` host (`dotnet src/Limbo.Console/bin/Debug/net10.0/Limbo.Console.dll --help`) for commands that don't touch WinDivert.

---

## 🖥 CLI Usage

```
Usage: limbo [command] [options]

Commands:
  run [--global]               Start DPI bypass engine (--global: bypass all traffic this run)
  calibrate <domain> [--save]  Find best bypass method for a domain (--save: store without asking)
  test <domain>                Test DNS, DoH, TCP connectivity
  add-profile <domain> <method> [opts]   Add a domain profile
  remove-profile <domain>      Remove a domain profile
  radar                        Show auto-detected blocked domains
  radar-import <file>          Import a radar blacklist
  radar-export <file>          Export radar blacklist
  radar-clear [--yes]          Clear all radar entries (--yes: skip confirmation)

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

## 🖥 Interactive Menu

Running `limbo` with no arguments opens a full interactive dashboard built on Spectre.Console. The status panel (mode, admin, DoH, network/DNS, profiles/Radar) sits beside the logo and the menu is grouped into **Engine / Configuration / Tools / Limbo**, so the whole screen fits a default 30-row console window:

- **Start Engine** — shows the mode it will start in; live-updating traffic table (packets, matched/modified, bandwidth, DNS answered via DoH) while the bypass engine runs
- **Switch Mode** — one step between *profiles only* and *global* (GoodbyeDPI-style) mode
- **How to Use** — built-in guide: quick start, modes, profiles and exclusions, DNS over HTTPS, and a troubleshooting checklist
- **Profile Management** — list / add / edit / delete profiles through a wizard that only asks for the parameters relevant to the chosen method
- **DNS Settings** — toggle DoH, pick a provider (Cloudflare/Google/Quad9/custom), see the DNS servers the system actually uses (IPv4 and IPv6 separately, with a warning when IPv6 DNS servers are active but DoH is off - an IPv4-only DNS setting doesn't cover them), test resolution live
- **Adaptive Engine (Radar)** — turn auto-detection on/off, turn **global mode** on/off, choose the default bypass method (and parameters) used for detected domains and in global mode, list/import/export/clear detections
- **Network Analysis** — ISP/public IP identity plus an on-demand live scan for DNS tampering, RST injection, or silent dropping against a domain
- **Calibration** and **Connection Test** — same checks as the CLI commands, with a progress bar / results table
- **General Settings** — verbose logging, log file path, **language** (English / Turkish), and the update check
- **About** — version, license, source link, bundled components, and update status (with a shortcut to the download page when a newer release exists)

**Update check:** when the interactive menu opens, Limbo makes one anonymous request to the GitHub API (`api.github.com`) for the latest release. If a newer one exists, the status panel and the About screen say so; downloading and installing is always up to you, Limbo never updates itself. It's never made by scripted CLI commands, and can be turned off in **General Settings**. Beyond this, Limbo only contacts services as part of features you use: your configured DoH resolver, `ipinfo.io` when you open Network Analysis, and the domains you test or calibrate.

All of this is also available as scriptable, non-interactive commands (`limbo run`, `limbo add-profile ...`, etc.) — see [CLI Usage](#-cli-usage) below.

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
  "language": "English",
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

Hiding the SNI isn't enough when the ISP also **poisons DNS**: a blocked domain then resolves to a block-page address (Türk Telekom answers `discord.com` with `195.175.254.2`), and the connection goes to the wrong server no matter how the handshake is disguised.

With DoH on (the default for new configs), while the engine runs Limbo catches **every app's plain DNS query** (UDP 53, over **IPv4 and IPv6**) and answers it over encrypted DNS-over-HTTPS instead of letting it reach the network:

- **No system settings are touched**, so there's nothing to undo when Limbo stops or is uninstalled.
- **IPv6 is covered too.** A common trap: Windows' "encrypted DNS" set for IPv4 only, while the router hands out its own IPv6 DNS server, and queries quietly go through that one, poisoned. Limbo answers both.
- **Works where plain DNS to public resolvers is blocked.** Some ISPs drop UDP 53 to `1.1.1.1`; DoH runs over HTTPS (port 443).
- **Fails safe:** if the DoH server can't be reached, the original query goes out unchanged, so DNS keeps working, just unprotected. The live dashboard shows how many queries were answered via DoH and how many fell back to plain DNS.

Supported servers (any RFC 8484 server works; HTTP/2 is used when offered):
- Cloudflare: `https://1.1.1.1/dns-query` (default; an IP address, so it needs no DNS lookup of its own)
- Google: `https://dns.google/dns-query`
- Quad9: `https://dns.quad9.net/dns-query`

> This needs the engine's WinDivert filter to include UDP (the default `tcp or udp` does). DoH-enabled browsers that already encrypt their own DNS aren't affected either way.

---

## 📊 Compared to GoodbyeDPI

| Feature | GoodbyeDPI | Limbo |
|---------|------------|-------|
| Target | All traffic, global options | Per-domain profiles, or all traffic in global mode |
| ECH Support | ⚠️ ECH grease only (fake packets) | ✅ Full X25519 HPKE (real ECH) |
| Profile System | No (global flags only) | ✅ Wildcard patterns per domain |
| Radar (auto-detect) | No | ✅ Yes |
| ISP Behavior Analysis | No | ✅ RST injection, silent drops |
| Automatic Calibration | No | ✅ Per domain |
| Bandwidth Monitoring | No | ✅ Real-time |
| DNS Bypass | ✅ Redirects UDP DNS to another resolver/port (`--dns-addr`, unencrypted) | ✅ Answers all apps' DNS (IPv4 + IPv6) over encrypted DoH |
| SIMD Acceleration | No | ✅ AVX2 |
| CLI | ✅ Yes | ✅ Interactive + scriptable |
| Language | C (C99) | C# (.NET 10) |
| Platform | Windows | Windows |
| License | Apache 2.0 | GPL v3 (or later) |

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
- [x] Real-time bandwidth monitoring
- [x] DNS over HTTPS (parallel A + AAAA)
- [x] SIMD (AVX2) packet parsing
- [x] Interactive console (Spectre.Console) with live traffic dashboard
- [x] English / Turkish language switch
- [x] Network Analysis — ISP identification + live DNS tampering/RST injection/silent drop scan
- [x] Calibration actually applies each strategy live (scoped capture), not simulated
- [x] Configurable default method for Radar auto-detected domains
- [x] Full CLI (run, calibrate, test, radar, add/remove-profile)
- [x] Unit test suite + CI (build & test on every push/PR)


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

Copyright (C) 2025-2026 Umit Korkmaz

This project is licensed under the **GNU General Public License v3.0 or later** (`GPL-3.0-or-later`): you can redistribute and/or modify it under the terms of version 3 of the GPL as published by the Free Software Foundation, or (at your option) any later version. It is distributed in the hope that it will be useful, but **without any warranty**; see the license text for details.

> Limbo uses [WinDivert](https://github.com/basil00/WinDivert), which is dual-licensed under LGPL v3 / GPL v2; Limbo uses it under the **LGPL v3** terms, which are compatible with GPL v3.

> **Earlier versions:** v1.0.0 and v1.1.0 were released under **GPL v2**, and copies of those versions remain available under GPL v2. The switch to GPL v3 applies from the next release on.

See [LICENSE](LICENSE) for the full license text. The third-party components bundled in release packages (WinDivert, Spectre.Console, BouncyCastle, the .NET runtime) and their licenses are listed in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt), which is also included in every release ZIP.

---

<div align="center">

**⭐ Star this repo if you find it useful!**

*Designed & Engineered by [Umit Korkmaz](https://github.com/umitkrkmz)*

</div>
