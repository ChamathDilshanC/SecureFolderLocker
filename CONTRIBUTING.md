# Contributing

## Prerequisites

The application requires Windows, the .NET 8 SDK (Visual Studio 2022 with the .NET desktop workload is optional), and Inno Setup 6 for installer work. Build and test with `dotnet build .\SecureFolderLocker.sln` and `dotnet test .\SecureFolderLocker.sln`. The `sfl` command-line tool is documented in [docs/CLI.md](docs/CLI.md).

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
