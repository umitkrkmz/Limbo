# Changelog

All notable changes to Limbo are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).
Versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.0] - 2025-02-17

### Added

**Core**
- `BandwidthMonitor` — Real-time sliding-window bandwidth tracking (download/upload rates, peak rates, totals)
- `BlacklistManager` — Persistent auto-blacklist with import/export, event notifications, IP address filtering
- `IspBehaviorAnalyzer` — Detects RST injection, silent drops (3s timeout), DNS poisoning; fires `OnBlockingDetected` event
- `ConnectionTracker` — Tracks inbound/outbound TCP connections, detects silent drops

**Radar System**
- Auto-detects blocked domains via `IspBehaviorAnalyzer` + `ConnectionTracker`
- Automatically adds blocked domains to blacklist and creates `Adaptive` bypass profile
- Persists to `%LOCALAPPDATA%\Limbo\Auto-Blacklist.txt`

**CLI**
- `radar` command — Lists all auto-detected blocked domains with timestamps
- `radar-import <file>` — Imports a radar blacklist from file
- `radar-export <file>` — Exports current radar blacklist to file
- `radar-clear` — Clears all radar entries
- `add-profile` now supports all bypass parameters:
  - `--frag <n>` — Fragment size (TcpFragmentation / SmartFragmentation)
  - `--ttl <n>` — Fake TTL value (FakeTtl / UdpFake)
  - `--split <n>` — Split position (Desync)
  - `--offset <n>` — SNI split offset (SmartFragmentation)
  - `--ech-name <s>` — ECH public name (Ech)
  - `--quic-size <n>` — Fake packet size (QuicFake)
- `test` command enhanced: latency measurement, IP address list, DNS tampering check
- `run` command: live bandwidth stats line (↓/↑ rates updated every second)
- `list-profiles` shows all profile parameters
- `--show-config` shows all configuration fields

**GUI**
- Live bandwidth stats in status bar (↓/↑ rates + peak)
- Radar panel with auto-detected blocked domains list
- Radar import/export buttons
- System tray: minimize to tray, close to tray, start minimized options
- Dark / Light / System theme toggle

**ECH**
- Full X25519 HPKE implementation (X25519 key exchange + AES-128-GCM encryption)
- Encrypts ClientHello SNI so DPI cannot read target domain

**DNS**
- Parallel A + AAAA queries via DoH
- Configurable DoH server URL (Cloudflare, Google, Quad9)
- Response caching with TTL

**Testing**
- 48 unit tests (up from 24)
- `BlacklistManagerTests` — 8 tests covering add, duplicate prevention, events, IP filtering, import/export, persistence
- `BandwidthMonitorTests` — 8 tests covering totals, rates, peak, Reset, FormatRate
- `IspBehaviorAnalyzerTests` — 8 tests covering RST detection, timeout analysis, events, DNS poisoning

**Project**
- GPL v2 license
- `.gitignore` for .NET / WPF / WinDivert / user data
- `CONTRIBUTING.md`, `CHANGELOG.md`, `SECURITY.md`
- `REPORT.md` — Comprehensive architecture and implementation report

### Changed

- `BlacklistManager` constructor now accepts optional full path (enables isolated test instances)
- README rewritten: WinDivert setup instructions, GoodbyeDPI comparison, Contributing section, Acknowledgements

### Fixed

- Culture-sensitive `FormatRate` output (decimal separator varies by locale; tests now use `Contains` not `Equal`)

---

## [0.2.0] - 2025-01

### Added

- Per-domain wildcard profile system (`*.discord.com`, `*.twitter.com`)
- JSON configuration (`%LOCALAPPDATA%\Limbo\config.json`)
- Profile import / export
- 7 bypass strategies: TcpFragmentation, SmartFragmentation, Desync, FakeTtl, UdpFake, QuicFake, ECH
- Adaptive mode — tests all strategies, saves the best
- Automatic calibration per domain
- SIMD (AVX2) packet parsing for SNI offset detection
- WPF GUI with dark/light theme
- Basic CLI (run, calibrate, test, add-profile, remove-profile, list-profiles)
- DNS over HTTPS (Cloudflare 1.1.1.1 default)
- 24 unit tests

---

## [0.1.0] - 2025-01

### Added

- Initial proof of concept
- WinDivert P/Invoke wrapper (`Limbo.Driver`)
- Basic TCP fragmentation bypass
- Single global configuration
