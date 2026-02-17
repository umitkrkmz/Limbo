# Security Policy

## Supported Versions

| Version | Supported |
|---------|-----------|
| 0.3.x   | Yes       |
| < 0.3   | No        |

Only the latest release receives security fixes.

---

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

If you discover a security vulnerability in Limbo, report it privately:

1. Go to the [GitHub Security Advisories](https://github.com/umitkrkmz/Limbo/security/advisories/new) page
2. Click **"Report a vulnerability"**
3. Fill in the details

Alternatively, you can contact the maintainer directly via GitHub.

### What to include

- Description of the vulnerability
- Steps to reproduce
- Potential impact
- Any suggested mitigations (optional)

### Response Timeline

- **Acknowledgement**: within 3 business days
- **Initial assessment**: within 7 days
- **Fix or mitigation**: depends on severity and complexity

You will be kept informed of progress. If a CVE is warranted, we will coordinate disclosure together.

---

## Scope

This policy covers the Limbo source code in this repository:

- `src/Limbo.Core` — bypass engine, radar, DNS, ECH, monitoring
- `src/Limbo.Driver` — WinDivert P/Invoke wrapper
- `src/Limbo.Console` — CLI application
- `src/Limbo.GUI` — WPF application

**Out of scope:**
- WinDivert itself (report to the [WinDivert project](https://github.com/basebe/WinDivert))
- Third-party NuGet packages
- Vulnerabilities in the .NET runtime

---

## Security Considerations

Limbo requires **Administrator privileges** to operate (WinDivert kernel driver). Users should:

- Only download Limbo from the [official repository](https://github.com/umitkrkmz/Limbo/releases)
- Verify the SHA-256 checksum of downloaded release archives
- Run Limbo only in environments where you understand and accept the elevated privilege requirements

---

## Disclosure Policy

We follow responsible disclosure. Once a fix is ready:

1. A patched release will be published
2. A GitHub Security Advisory will be created
3. The reporter will be credited (unless they prefer to remain anonymous)
