# AGENTS.md

Context for AI coding agents working in this repository.

## Project

Limbo is a Windows DPI (Deep Packet Inspection) bypass engine written in C# / .NET 10. It intercepts and rewrites live network packets via [WinDivert](https://github.com/basil00/WinDivert) to evade SNI-based traffic blocking, using per-domain bypass profiles.

```
src/
  Limbo.Core/      Business logic: bypass strategies, ECH/HPKE, radar (auto-detection),
                    DNS-over-HTTPS, calibration, network analysis, profiles, config
  Limbo.Driver/     WinDivert P/Invoke wrapper (IPacketDriver / WinDivertDriver)
  Limbo.Console/    Interactive (Spectre.Console) + scriptable CLI - the only front end
tests/
  Limbo.Tests/      xUnit
```

There is no GUI project - it was removed in favor of the console's interactive menu.

## Build & test

```bash
dotnet build Limbo.slnx
dotnet test tests/Limbo.Tests/Limbo.Tests.csproj
```

**WinDivert binaries are not in this repo** (separately licensed, LGPL v3/GPL v2 - see README; only its license text is, at `src/Limbo.Driver/LICENSE-WinDivert.txt`). The release workflow downloads the official 2.2.2 binaries at build time. `Limbo.Driver.csproj` copies `WinDivert.dll`/`WinDivert64.sys` from `src/Limbo.Driver/runtimes/win-x64/native/` to the output directory as a build step, and the build **fails** if those files are missing. To build/test without the real driver (which you don't need for compiling or running the test suite - only for actually opening a capture handle), create empty placeholders first:

```bash
mkdir -p src/Limbo.Driver/runtimes/win-x64/native
touch src/Limbo.Driver/runtimes/win-x64/native/WinDivert.dll
touch src/Limbo.Driver/runtimes/win-x64/native/WinDivert64.sys
```

These are gitignored; never commit real WinDivert binaries.

## Things that require Administrator / can't be verified in a sandbox

`Limbo.Console.exe` has an `app.manifest` with `requireAdministrator`, so launching the exe (including via `dotnet run`) from a non-elevated shell fails with "The requested operation requires elevation" - which is what an agent's shell almost always is. To exercise the console from a non-elevated shell, run the built DLL through the host instead: `dotnet src/Limbo.Console/bin/Debug/net10.0/Limbo.Console.dll <args>`.

WinDivert requires elevation to open a capture handle. `run`, calibration's real bypass-strategy testing, and Network Analysis's live behavior scan all degrade gracefully without admin (they say so explicitly) rather than failing silently - preserve that pattern in anything new that touches `IPacketDriver`. Live-traffic behavior (does a bypass strategy actually work, does RST-injection detection fire correctly) can only be verified by a human running the built console as Administrator against real traffic - say so explicitly rather than claiming it's verified from a non-elevated/sandboxed run.

## Conventions

- **No comments unless the WHY is non-obvious.** Never explain what code does; identifiers should do that.
- **English only** in source code, CLI/scripting output (`Commands/*.cs`, `--help`), and doc comments. The interactive console (`Interactive/*.cs`) is the one bilingual surface: strings go through `Loc.T("key")` in `src/Limbo.Console/Interactive/Loc.cs` (English is the base language; Turkish is a runtime setting). Don't hardcode literal English/Turkish strings in `Interactive/*.cs`.
- Don't add abstractions, config knobs, or error handling for scenarios that can't happen. Match the scope of a change to what was actually asked.
- Test data isolation: never touch real `%LOCALAPPDATA%\Limbo\*` in a test. `ProfileManager`/`BlacklistManager` both accept a directory/path override for exactly this - see the `GetTempProfileDir()` pattern in `ProfileManagerTests.cs`/`LimboEngineTests.cs`.
- **License: GPL-3.0-or-later.** Release packages are self-contained, so every NuGet package that ends up in `Limbo.Console.exe` is redistributed: adding, removing or upgrading one means updating `THIRD-PARTY-NOTICES.txt` (name, version, license, copyright notice). Version numbers live in each csproj's `<Version>`; the release workflow refuses a tag that doesn't match `Limbo.Console.csproj`, and `--version` reads the assembly version (`AppInfo.Version`) - don't hardcode it anywhere.
- Tests that call a real external service (DoH, ipinfo.io) are acceptable but must tolerate no network access - see `DoHResolverTests.cs`/`IspIdentifierTests.cs`: assert on structure/validity when a result comes back, don't fail the suite if it's `null`/empty.

## Sharp edges found the hard way this project

- **`ValueTask.AsTask()` must only be called once.** Calling it twice (e.g. once inside `Task.WhenAny(x.AsTask(), ...)`, then again to compare against the winner) throws `InvalidOperationException` - assign it to a variable first and reuse that.
- When a WinDivert filter is scoped to a specific resolved IP (`ip.DstAddr == X`), any connection you make for that same test **must connect to that exact IP**, not re-resolve the hostname via `TcpClient.ConnectAsync(host, port)`. Domains with multiple A records (most CDN-fronted sites) can resolve differently between two separate lookups, and the filter then silently watches a different address than the traffic actually uses. See `NetworkProbe.TestConnectionAsync`'s `IPAddress targetIp` parameter.
- `System.Net.Dns` gets shadowed by the sibling `Limbo.Core.Dns` namespace from anything under `Limbo.Core.Engine`/`Limbo.Core.Analysis` - fully qualify as `System.Net.Dns.GetHostAddressesAsync(...)`.
- Shutting down a live WinDivert capture loop: cancel the token **and** call `driver.Dispose()` to unblock the pending (blocking) `WinDivertRecv` call. Cancelling the token alone does not unblock it.
