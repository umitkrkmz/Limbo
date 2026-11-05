# Changelog

All notable changes to Limbo are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).
Versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Added
- **Complete license files in every release package.** The v1.1.0 ZIP listed only WinDivert in its notices, although it also redistributes Spectre.Console, BouncyCastle and (being self-contained) the .NET runtime, whose MIT licenses all require their notices to travel with the binaries. Release packages now contain `LICENSE.txt` (Limbo, GPLv3), `LICENSE-WinDivert.txt` (WinDivert 2.2.2's own license file, unchanged; now committed instead of downloaded from WinDivert's `master` at build time) and `THIRD-PARTY-NOTICES.txt` listing every bundled component with its copyright and permission notice
- `app.manifest` requesting Administrator: `Limbo.Console.exe` now raises the UAC prompt itself instead of relying on the user to choose "Run as administrator". Side effect: `dotnet run` from a non-elevated terminal now fails with "requires elevation" (see README)
- **Installer** (`installer/Limbo.iss`, Inno Setup 6, English/Turkish): installs to Program Files with a Start menu entry (desktop icon optional) and an Add/Remove Programs entry, shows the license during setup, and on uninstall asks whether to delete `%LOCALAPPDATA%\Limbo` (settings, profiles, radar blacklist). WinDivert's kernel driver stays loaded after the app exits and keeps `WinDivert64.sys` locked, so setup/uninstall first stop the `WinDivert` driver service, but only when it was loaded from Limbo's own install folder, leaving drivers loaded by other WinDivert tools (ZeroDivert, GoodbyeDPI) alone; if it still can't be stopped the file is replaced/deleted on reboot. Setup refuses to run while Limbo is open. The release workflow builds it alongside the portable ZIP
- **Interception lock:** only one Limbo copy can intercept packets at a time (engine `run`, calibration, live network scan); a second one refuses with a message instead of rewriting the same packets twice. Read-only commands aren't affected. Limbo also warns when ZeroDivert is running, since both rewrite Discord's TLS handshakes
- Publish profile `src/Limbo.Console/Properties/PublishProfiles/win-x64.pubxml` (self-contained, single-file, compressed, no `.pdb`s), so a local `dotnet publish -p:PublishProfile=win-x64` produces exactly the release package; the license files and WinDivert binaries are kept next to the exe via `ExcludeFromSingleFile`

### Changed
- **License changed from GPL v2 to GPL v3 or later (`GPL-3.0-or-later`).** The previous rationale ("GPL v2 for compatibility with WinDivert") never required v2: WinDivert is dual-licensed LGPL v3 / GPL v2, and its LGPL v3 option is compatible with GPL v3; Spectre.Console and BouncyCastle (MIT) are compatible with both. GPL v3 adds an explicit patent license, a 30-day cure period after a violation, and simpler source-distribution rules. v1.0.0 and v1.1.0 remain available under GPL v2. `LICENSE` now holds the verbatim GPL v3 text from gnu.org
- **Release workflow:** checks that the pushed tag matches `<Version>` in `Limbo.Console.csproj`, publishes via the new profile, fails if any expected file is missing from the package, attaches a `.sha256` file next to the ZIP, and creates a **draft** release to review before publishing (`-rc`-style tags become pre-releases). `actions/checkout` and `actions/setup-dotnet` bumped to v5 in both workflows (v4 ran on the deprecated Node 20)
- Project version bumped to 1.1.0 in all csproj files (they still said 1.0.0; v1.1.0's exe only got its version from a workflow override) and copyright updated to 2025-2026

### Fixed
- **`calibrate` and `radar-clear` crashed when scripted.** Both always showed a confirmation prompt, which throws in a non-interactive session (redirected output, scripts, scheduled tasks). `calibrate` now takes `--save` to store the result without asking, and otherwise only asks in an interactive terminal; it also exits with 1 when calibration fails instead of always 0. `radar-clear` takes `--yes`/`-y` (its existing skip-confirmation path was unreachable from the CLI) and refuses to clear without it in a non-interactive session
- `--version` printed a hardcoded `0.3.0` (including in the v1.1.0 release); it now reads the assembly version and also shows the license

## [1.1.0] - 2026-08-31

### Added
- Interactive console UI (Spectre.Console) — main menu, profile wizard, DNS/Radar/general settings screens, live traffic dashboard for `run`
- English / Turkish language switch for the interactive console (`LimboConfig.Language`)
- Configurable default bypass method (+ manual parameters) for Radar auto-detected domains (`LimboConfig.DefaultMethodProfile`), replacing the previously hardcoded `Adaptive`
- Turkish translation of the README (`README.tr.md`)
- **Network Analysis** — `IspIdentifier` (public IP/ISP/ASN lookup) and `NetworkAnalyzer` (a live, read-only scan of one domain's traffic reporting DNS tampering, RST injection, or silent dropping), surfaced as a new interactive menu screen
- GitHub Actions CI (`.github/workflows/build.yml`) — build + test on every push/PR
- GitHub Actions release workflow (`.github/workflows/release.yml`) — on a `v*` tag, publishes a self-contained single-file `win-x64` build, downloads the official WinDivert 2.2.2 binaries and bundles them (with WinDivert's license) into the release ZIP, and creates/updates the GitHub Release with a SHA-256 checksum
- `AGENTS.md` — build/test commands, code conventions, and known sharp edges for AI coding agents working in this repo
- Tests: `HpkeTests` (validated against the official RFC 9180 Appendix A.1.1 test vector), `EchClientHelloInnerTests` (full round-trip HPKE decrypt), `LimboEngineTests`, `IspIdentifierTests`, plus structural tests for `EchClientHelloBuilder`

### Changed
- **Calibration now actually applies each bypass strategy** through a scoped, per-domain WinDivert interception instead of simulating every strategy as an identical plain connectivity check
- Calibration's TLS ClientHello probe rebuilt to be realistic (modern ECDHE/AES-GCM/ChaCha20 cipher suites, proper extensions) instead of offering a single obsolete static-RSA suite that most modern servers refuse outright
- `LimboEngine` reworked to be the single owner of the packet capture loop: takes an `IPacketDriver` directly (replacing an unused 3-delegate `Start()` signature), integrates `ConnectionTracker`/`BandwidthMonitor`, and respects `EnableRadar`/`DefaultMethodProfile`; `RunCommand` is now a thin wrapper around it instead of duplicating the loop
- `NetworkProbe` (shared by calibration and network analysis) now connects to a pre-resolved IP instead of re-resolving the hostname a second time, so a WinDivert filter scoped to one IP can no longer end up watching different traffic than the actual connection uses (this affects any domain with multiple DNS A records, i.e. most CDN-fronted sites)
- Connectivity probes now report *why* an attempt failed (exception, timeout, no IPv4) instead of only pass/fail

### Fixed
- **HPKE implementation was not RFC 9180 compliant** (`HpkeWrapper`/`HpkePrimitives`) — the HKDF construction had HMAC key/message roles reversed and skipped HPKE's labeled extract/expand scheme, so ECH could never have decrypted correctly against a real server
- **ECH's `ClientHelloInner` was a stub** (raw SNI bytes + padding, not a real TLS message) — rebuilt per draft-ietf-tls-esni to preserve the real client's ClientHello content byte-for-byte (random, key_share, cipher suites, other extensions) with only `legacy_session_id` zeroed and correct §6.1.3 padding applied; the previous version would have desynced the real client's TLS session keys from the server's even once the transport worked
- **The entire `Profiles` module (`DomainProfile`, `ProfileManager`) was missing from the repository** — an unanchored `profiles/` rule in `.gitignore` was swallowing `src/Limbo.Core/Profiles/` on commit, so the project did not compile from a clean clone
- `ValueTask.AsTask()` was being called twice in the connectivity probe, throwing `InvalidOperationException` on every single calibration/network-analysis check regardless of whether the connection actually worked
- Radar's RST-injection detection (`IspBehaviorAnalyzer.AnalyzeRstPacket`) was never wired to any real inbound packet anywhere in the codebase — Network Analysis is the first code path that actually calls it
- Removed a stray leftover debug line in `LimboEngine`'s constructor that unconditionally blacklisted a fake test domain on every instantiation

### Removed
- **`Limbo.GUI` (WPF desktop application)** — removed in favor of the console's interactive menu. GUI-only config fields (`Theme`, `MinimizeToTray`, `CloseToTray`, `StartMinimized`) were also removed from `LimboConfig`.

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
