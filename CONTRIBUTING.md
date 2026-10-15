# Contributing

## Prerequisites

The planned application requires Windows, Visual Studio 2022 with the .NET desktop workload, the .NET 8 SDK, and Inno Setup for installer work.

## Workflow

1. Read [AGENTS.md](AGENTS.md) and the relevant documents in [docs/](docs/README.md).
2. Create a focused topic branch.
3. Implement the smallest coherent change.
4. Add or update tests and documentation.
5. Review the diff for secrets, unrelated files, and unsafe ACL behavior.
6. Open a pull request with validation results and known limitations.

## Code and security expectations

Follow the C# nullable and MVVM conventions. Passwords must be BCrypt-hashed, never logged or persisted in plaintext. Validate paths, preserve ACL state, and show actionable errors to users.

## Review checklist

- Requirements and edge cases are covered.
- Tests cover success and failure paths.
- Administrator requirements are clear.
- No credentials or personal data are committed.
- Documentation links and status labels are accurate.

## Issues and documentation

Include reproduction steps, Windows/.NET versions, logs with secrets removed, and expected versus actual behavior. Update the [PRD](docs/PRD.md), [architecture](docs/ARCHITECTURE.md), or [security guide](docs/SECURITY.md) when behavior changes.

The sole listed maintainer is [ChamathDilshanC](https://github.com/ChamathDilshanC).
