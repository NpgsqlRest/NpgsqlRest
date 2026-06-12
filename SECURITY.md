# Security Policy

## Supported Versions

Security fixes are provided for the **latest released minor version** of NpgsqlRest (and its plugins). Older versions do not receive backported fixes — upgrade to the latest release.

| Version | Supported |
|---|---|
| Latest 3.x release | ✅ |
| Older releases | ❌ |

## Reporting a Vulnerability

**Please do NOT open a public issue for security vulnerabilities.**

Report privately via **[GitHub Private Vulnerability Reporting](https://github.com/NpgsqlRest/NpgsqlRest/security/advisories/new)** — this creates a private advisory visible only to the maintainer.

What to include:

- The affected component (core library, `NpgsqlRestClient` binary, a specific plugin, Docker image, or npm package) and version.
- A description of the vulnerability and its impact.
- Steps to reproduce — a minimal config + SQL setup is ideal.
- Any suggested fix, if you have one.

## What to Expect

- **Acknowledgement** within **7 days**.
- An assessment and, for confirmed vulnerabilities, a **fix or documented mitigation targeted within 90 days** (usually much sooner; severity drives priority).
- **Coordinated disclosure**: we ask that you keep the report private until a fixed release is available. You will be credited in the advisory and changelog unless you prefer otherwise.

## Scope

In scope:

- The NpgsqlRest core library (NuGet: `NpgsqlRest`)
- The `NpgsqlRestClient` application (release binaries, `vbilopav/npgsqlrest` Docker images, `npgsqlrest` npm package)
- Official plugins in this repository (`plugins/`)

Out of scope:

- The documentation website
- Example/demo code (`examples/`)
- Vulnerabilities in dependencies with no NpgsqlRest-specific exploitation path (report those upstream; we still appreciate a heads-up so we can update)
- Issues requiring a hostile configuration explicitly documented as unsafe (e.g., empty `RelyingPartyOrigins` in production, wildcard CORS)
