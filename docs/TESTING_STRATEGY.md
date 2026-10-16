# Testing Strategy

**Status:** The xUnit project `tests/SecureFolderLocker.Tests` exists and runs in CI. It covers the shared library and the `sfl` command-line tool. The desktop UI has no automated tests; it is checked manually.

Run all tests with `dotnet test .\SecureFolderLocker.sln`.

## Test pyramid

- **Unit:** password rules and hashing, path validation and protected locations, exit-code mapping, command-line option scanning, the preview watcher rules, the argument quoting used for elevation (checked against Windows' own `CommandLineToArgvW`), and the output mirror.
- **Integration:** the real Windows ACL adapter on disposable folders under `%TEMP%`; real SQLite databases in a temp folder; real AES-256 ZIPs written and read with SharpZipLib, including hand-built hostile archives; real named pipes for the elevation relay; the CLI run in-process end to end with a fake console and fake prompts.
- **UI/manual:** first-run flow, keyboard navigation, elevation prompt, lock/unlock feedback, and recovery in the desktop app; the real UAC relaunch of the CLI; the administrator-mode install.

## What is covered

| Area | Tests |
| --- | --- |
| Passwords | Hash and verify, wrong password, change, minimum length, plaintext absent from the database file, same password hashes differently |
| ACL | Lock adds exactly one deny rule; lock twice does not stack; unlock restores the original rules; a foreign deny rule survives unlock; locked folders deny reads; preview allows reads and denies writes; hidden attribute set and cleared |
| Protection service | Lock then track; already locked; missing folder; protected locations rejected (validated without touching the ACL); orphaned rule can be removed; preview and relock keep the hidden choice |
| ZIP | Round trip (nested, unicode, empty files); archive really is AES-256 and unreadable without the password; wrong password and cancel write nothing; three attempts at a keyboard; existing destination needs `--force`; `unzip` creates no database and needs no administrator; zip-slip names (`..`, backslashes, absolute, drive letters, alternate data streams, trailing dots and spaces) rejected before anything is written, including for unencrypted archives |
| Commands | init, lock, unlock, relock, list, password change, zip, status success and failure paths; exit codes; nothing printed that contains a password; CLI and a second `DatabaseHelper` share one database and master password |
| Elevation | Already elevated; no keyboard; user says no; user says yes (same arguments, database path, result replayed); `--yes`; child exit code and errors; child never connects; child reports ready; Windows prompt declined; process cannot start; every ACL-changing command goes through the gate |
| Preview | Opens read-only and locks again on window close, Ctrl+C, and when Explorer never shows the folder; wrong master password never opens it; refused while another watcher holds the lease; stale previews detected, reported, and recovered with `relock --all` |
| Menu | Items; logo drawn once while the status line and current folder repeat; running a command and returning; picker walks into folders, goes up, accepts typed, quoted and relative paths, lists ZIP files, and backs out without running anything; changing the current folder; an elevated restart replays the chosen command with a full path and the same working folder; help when not interactive |
| Path picker | Entries and ordering, hidden system folders skipped, unreadable folder, drive roots, typed-path resolution (`~`, `%VAR%`, quotes, `..`) |

## Not covered by automated tests

- The real UAC consent prompt and relaunch. The automated tests use a fake launcher. A real relaunch (lock and ZIP of a locked folder, from the menu) was observed working in the maintainer's manual session, but is not repeatable in CI.
- The system-PATH (administrator) installer mode. The per-user mode was run by hand.
- The WPF windows. After moving logic into Core, the app was only launched and observed to start.
- Behavior when UAC runs the elevated copy as a different account.
- The 1-in-65536 case where the AES two-byte password check passes for a wrong password (the final integrity check handles it; only the message classifier is tested).

## Critical scenarios

1. Set and verify a password without persisting plaintext.
2. Reject incorrect passwords without changing filesystem state.
3. Lock and unlock a supported folder while preserving unrelated ACL rules.
4. Handle missing, duplicate, or unsupported paths.
5. Refuse false success after ACL or database failure.
6. Reconcile stale database state with actual ACL state (stale previews are handled; stale deleted folders are not yet, see T-020).
7. Run as a standard user and as administrator.

## Quality gates

Every implementation change must pass the repository build, the tests, and relevant manual checks. Windows-only ACL tests create their own folders under `%TEMP%`, always remove their rule in cleanup, and never lock a drive root, the Windows directory, or any real user path: the protected-location checks are tested through a validation method that changes nothing.
