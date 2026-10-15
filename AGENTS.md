# Coding Agent Guide

## Repository orientation

This is a Windows WPF Folder Locker project. The .NET 8 WPF shell, as-invoker manifest with on-demand administrator elevation, ACL adapter, SQLite database helper, MVVM view model, and WPF-UI dashboard are implemented. Release packaging (Inno Setup and GitHub Actions) is implemented; automated tests remain planned. The intended structure is described in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Source of truth

1. Existing source and tests, once implemented.
2. Project configuration and migrations.
3. [docs/PRD.md](docs/PRD.md) for product behavior.
4. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for technical boundaries.
5. Other documents for their stated scope.

Mark new decisions as proposed until they are implemented and tested. Do not claim a feature is complete without evidence.

## Conventions

- Use nullable reference types and explicit types where they improve safety.
- Keep UI, application services, infrastructure, and persistence concerns separate.
- Use MVVM and keep code-behind limited to view-specific behavior.
- Never store plaintext passwords or secrets.
- Use Windows ACL APIs through a dedicated service, not directly from a view.
- Preserve existing ACL rules and provide a recovery path for failed operations.

## Validation

Build verification currently uses `dotnet restore .\SecureFolderLocker.csproj` followed by `dotnet build .\SecureFolderLocker.csproj --no-restore`. Tests are not implemented yet.

Expected quality gates are build, tests for password/ACL/database logic, and a manual administrator-mode smoke test on Windows.

## Change and security rules

- Do not add credentials, private keys, database files, publish output, or user data.
- Treat folder paths as untrusted input and validate them before persistence or ACL changes.
- Use least privilege in services and never silently swallow authorization or IO errors.
- Update related documentation when behavior, storage, security, or user flows change.
- Do not introduce a dependency without documenting its purpose and license implications.

## Git workflow

Use a topic branch, make focused commits, and stage explicit paths. Do not reset, force-push, or rewrite unrelated work. Review the diff and run applicable checks before committing. Push only to the verified repository remote.

## Completion criteria

A change is complete when it has a clear implementation, relevant automated tests, updated documentation, and a concise validation report. Planned work must remain labeled as planned.
