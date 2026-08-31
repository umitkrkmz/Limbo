# Contributing to Limbo

Thank you for considering a contribution to Limbo! This document explains the process and conventions we follow.

---

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [How to Contribute](#how-to-contribute)
- [Development Setup](#development-setup)
- [Branch Naming](#branch-naming)
- [Commit Messages](#commit-messages)
- [Pull Request Process](#pull-request-process)
- [Code Style](#code-style)
- [Testing](#testing)
- [Good First Issues](#good-first-issues)

---

## Code of Conduct

Be respectful and constructive. Harassment, discrimination, or hostile behavior will not be tolerated.

---

## How to Contribute

### Reporting Bugs

1. Search [existing issues](https://github.com/umitkrkmz/Limbo/issues) first
2. Open a new issue with:
   - Windows version and build
   - .NET runtime version (`dotnet --version`)
   - Steps to reproduce
   - Expected vs actual behavior
   - Relevant log output (use `--verbose`)

### Suggesting Features

Open an issue with the `enhancement` label. Describe:
- The use case / problem it solves
- Proposed behavior
- Any alternatives you considered

**For large changes**, open an issue first and discuss before writing code. This prevents wasted effort.

### Submitting Code

See [Pull Request Process](#pull-request-process) below.

---

## Development Setup

### Requirements

- Windows 10/11 64-bit
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Administrator privileges (required for WinDivert)
- [WinDivert 2.2.2](https://github.com/basil00/WinDivert/releases/tag/v2.2.2) — place `WinDivert.dll` and `WinDivert64.sys` into `src/Limbo.Driver/runtimes/win-x64/native/` before building

### Clone and Build

```bash
git clone https://github.com/umitkrkmz/Limbo.git
cd Limbo
dotnet build
```

### Run Tests

```bash
dotnet test tests/Limbo.Tests
```

All tests must pass before submitting a PR.

### Project Structure

```
src/
  Limbo.Core/      # Business logic — bypass strategies, radar, DNS, ECH
  Limbo.Driver/    # WinDivert P/Invoke wrapper
  Limbo.Console/   # Interactive + scriptable CLI application
tests/
  Limbo.Tests/     # xUnit unit tests
```

---

## Branch Naming

| Type | Pattern | Example |
|------|---------|---------|
| Feature | `feature/<short-name>` | `feature/quic-v2` |
| Bug fix | `fix/<issue-or-desc>` | `fix/rst-detection` |
| Docs | `docs/<topic>` | `docs/api-reference` |
| Refactor | `refactor/<area>` | `refactor/bandwidth-monitor` |

---

## Commit Messages

Use imperative mood, present tense:

```
Add: ECH fallback for non-Cloudflare hosts
Fix: RST injection false positive on UDP
Update: bandwidth monitor sliding window to 5s
Remove: unused ConnectionTracker overload
```

Prefix with one of: `Add`, `Fix`, `Update`, `Remove`, `Refactor`, `Docs`, `Test`, `Build`

---

## Pull Request Process

1. Fork the repository
2. Create a branch from `main`
3. Make your changes
4. Run `dotnet test` — all tests must pass
5. Update relevant documentation if needed
6. Open a PR with:
   - Clear title describing the change
   - Description of what and why (not just how)
   - Reference to any related issues (`Closes #123`)

PRs will be reviewed within a few days. You may be asked to make changes before merging.

---

## Code Style

- Follow standard C# conventions (PascalCase for types/methods, camelCase for locals)
- Nullable reference types enabled — handle nulls explicitly
- No unused imports or variables
- Prefer `var` where the type is obvious
- Keep methods focused — single responsibility
- Thread safety: use `Interlocked` or `lock` for shared state (see `BandwidthMonitor` as reference)

### No External Dependencies Without Discussion

Adding new NuGet packages requires prior discussion in an issue. The goal is to keep the dependency footprint minimal.

---

## Testing

- Write xUnit tests for all new public APIs in `Limbo.Core`
- Tests must be deterministic — no Thread.Sleep for timing, use injectable time or sufficient data
- Use `Assert.Contains` instead of `Assert.Equal` for culture-sensitive string comparisons (e.g. formatted rates)
- For file-system-dependent tests, use a unique temp directory and clean up in `IDisposable.Dispose`

---

## Good First Issues

Looking for a place to start? These areas are well-suited for first contributions:

- **ISP fingerprint database** — Add known ISP blocking signatures to `IspBehaviorAnalyzer`
- **Test coverage** — Improve edge case coverage for bypass strategies
- **Documentation** — Translations, usage examples, wiki pages
- **CLI improvements** — Output formatting, new display options

---

## Questions?

- **General questions / ideas** → [GitHub Discussions](https://github.com/umitkrkmz/Limbo/discussions)
- **Bug reports / feature requests** → [GitHub Issues](https://github.com/umitkrkmz/Limbo/issues)
