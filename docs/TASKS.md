# Engineering Tasks

| ID | Phase | Task | Acceptance criteria | Status |
| --- | --- | --- | --- | --- |
| T-001 | Foundation | Create .NET 8 WPF solution | Solution builds with nullable enabled and test project boundary defined | Done |
| T-002 | Foundation | Add SQLite schema initialization | Schema creates deterministically and uses constraints | Done |
| T-003 | Secure MVP | Implement password service | BCrypt hash/verify methods are wired and plaintext is absent from storage | Done |
| T-004 | Secure MVP | Implement ACL adapter | Lock/unlock changes only the matching deny rule | Done |
| T-005 | Secure MVP | Implement folder reconciliation | Stale and missing paths are surfaced without false locked state | Planned |
| T-006 | UX | Build dashboard and dialogs | Login and folder dashboard are wired with loading and error states | Done |
| T-009 | Secure MVP | Add optional super folder hiding | Hidden/System attributes and persisted hidden state are applied and shown in the dashboard | Done |
| T-010 | Secure MVP | Add read-only preview mode | Preview and full-lock commands switch application-owned ACL rules and persist mode state | Done |
| T-007 | Release | Create Inno Setup package | Clean-machine install and uninstall checks pass | Planned |
| T-008 | Release | Perform security and recovery review | Threat scenarios and interruption behavior are documented and tested | Planned |
| T-011 | CLI | Extract shared logic into `SecureFolderLocker.Core` | Desktop app and CLI reference one library; app builds and starts; lock/unlock/preview/relock/export sequences live in `FolderProtectionService` | Done (build, tests, app launch check; manual GUI click-through of every button not repeated) |
| T-012 | CLI | Implement `sfl` commands: init, lock, unlock, preview, relock, list, zip, unzip, password change, status, menu | Every command has tests for success and failure paths, documented exit codes, `--quiet`, `--no-color`, no password option | Done |
| T-013 | CLI | `unzip` works on a clean machine | No database or `init` needed; wrong password and zip-slip archives extract nothing; verified with the published single-file exe | Done |
| T-014 | CLI | On-demand elevation with `runas` and visible result | Asks y/n, relaunches with same arguments, replays the result in the original console, handles declined UAC | Implemented and tested with a fake launcher. Real relaunch observed working for `lock` and `zip` of a locked folder in the maintainer's manual menu session (2026-10-17); `preview` via real relaunch and a different-account UAC not yet checked |
| T-015 | CLI | `sfl preview` watcher | Locks on Explorer window close, Ctrl+C, console close; stale previews detected and recoverable | Done; real Explorer window verified with the elevation gate bypassed |
| T-016 | CLI | xUnit test project | ZIP round trip, wrong password, zip-slip, password hashing, parsing, ACL add/remove on temp folders | Done (see [TESTING_STRATEGY](TESTING_STRATEGY.md)) |
| T-017 | CLI | `sfl.iss` installer, `SfLCliSetup-<version>.exe` | Installs under Program Files, adds and removes PATH entry, works in a new terminal | Per-user (`/CURRENTUSER`) install and uninstall verified, PATH restored byte for byte. Administrator-mode install (system PATH) not verified |
| T-018 | CLI | Release workflow builds and publishes both installers with checksums | Tests and a ZIP smoke test gate the release | Implemented; not yet run on GitHub |
| T-019 | Release | Manual administrator-mode smoke test of desktop app and CLI | Lock, preview, relock, unlock, zip of a locked folder on an administrator account; CLI installed on a second computer extracts a ZIP | Planned |
| T-021 | CLI | Menu shows the logo once, shows the current folder, and has a folder/ZIP picker with typed paths | Banner appears once per session; picker navigates, goes up, switches drive, accepts typed and relative paths; tested with scripted keystrokes | Done (tests); real-terminal look to be confirmed by the maintainer |
| T-020 | CLI | Let a stale tracked entry (folder deleted) be forgotten | `unlock` of a missing folder removes its database entry | Planned (same limitation exists in the desktop app) |
