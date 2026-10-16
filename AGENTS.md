# Coding Agent Guide

## Repository orientation

This is a Windows Folder Locker project with two front ends over one library: the WPF desktop app (repository root) and the `sfl` command-line tool (`src/Sfl.Cli`), both using `src/SecureFolderLocker.Core` (ACL adapter, SQLite database helper, protection service, ZIP services, elevation). The desktop app has an as-invoker manifest with on-demand administrator elevation, MVVM view model and WPF-UI dashboard. Release packaging (two Inno Setup installers and GitHub Actions) is implemented, and xUnit tests live in `tests/SecureFolderLocker.Tests`. The structure is described in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and the CLI in [docs/CLI.md](docs/CLI.md).

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
- Put logic that both front ends need in `SecureFolderLocker.Core`, not in the app or the CLI. Do not copy it.
- Never store plaintext passwords or secrets. The CLI has no password option and must never print a password.
- Use Windows ACL APIs through a dedicated service, not directly from a view or a command.
- `sfl unzip` must keep working without a database, a master password or administrator rights.
- Preserve existing ACL rules and provide a recovery path for failed operations.

## Validation

Build verification uses `dotnet restore .\SecureFolderLocker.sln`, `dotnet build .\SecureFolderLocker.sln --no-restore` and `dotnet test .\SecureFolderLocker.sln --no-build`. The root project also still builds alone with `dotnet build .\SecureFolderLocker.csproj`.

Quality gates are build, the tests (password, ACL, database, ZIP, CLI), and a manual administrator-mode smoke test on Windows. Tests must create their own folders under `%TEMP%`, remove their ACL rule in cleanup, and never lock a drive root, the Windows directory, or a real user path. Do not run manual experiments against the real user database; set `SFL_DATABASE` to a temp file.

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
