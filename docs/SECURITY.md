# Security

**Status:** Proposed security baseline.

## Security boundary

The application protects local folders for a standard device user. A Windows administrator, bootable external environment, or direct disk access may bypass or change ACL protection. The product must never describe ACLs as encryption.

## Threat model

Relevant threats include password disclosure, unauthorized local access, tampered SQLite metadata, path confusion, ACL corruption, log leakage, and malicious or accidental interruption during permission changes.

## Controls

- Hash passwords with BCrypt.Net-Next and verify using the library.
- Never log passwords, hashes, full sensitive paths, or exception dumps containing them.
- Canonicalize and validate paths; reject unsupported locations and duplicate entries.
- Apply only an application-owned, identifiable ACL rule.
- Preserve and verify unrelated ACL entries.
- Require administrator elevation for ACL changes, requested on demand through a confirmation dialog that explains why (the app itself starts unelevated).
- Update persisted state only after successful verification.
- Use parameterized SQLite commands and schema constraints.
- Use safe, actionable errors without exposing security-sensitive implementation details.

## Command-line tool (`sfl`)

The `sfl` command-line tool has the **same security limits as the desktop app**: it applies Windows ACLs and does not encrypt anything. A Windows administrator, a bootable external environment, or direct disk access can bypass or change the protection. Do not rely on it for highly sensitive data; use disk encryption (BitLocker) or encrypted archives. The ZIP export is encrypted (AES-256); the folder lock is not.

Controls specific to the CLI (see [CLI](CLI.md)):

- **No password on the command line.** There is no `--password` option, so passwords stay out of shell history, process listings, and the arguments handed to the elevated copy. Prompts read without echo (masked with `*`). When input is redirected, one line per prompt is read from it. Passwords are never printed, including in error messages and `--quiet` output; tests check this.
- **Master password required where a lock is bypassed or removed:** `unlock`, `preview`, `zip` of a fully locked folder, and `password change`. `lock` and `relock` add protection and do not ask. `list` and `status` show folder paths from the local database without a password, as anyone with the user's profile could already read that file.
- **The wrong-password path changes nothing.** Three attempts at a keyboard, one without; a wrong or cancelled master password leaves the folder locked and untouched.
- **Elevation.** The CLI asks before restarting as administrator, validates the request first, and passes the original arguments plus the database path. The elevated copy reports back over a named pipe that only the current user's SID can open; the pipe carries output text only. `--yes` skips the question but not the Windows permission prompt.
- **Untrusted paths.** A folder to lock must exist, be on a local NTFS drive, and must not be a drive root, the Windows directory, Program Files, or the application's own data folder or a parent of it (locking those would break Windows or lock the app out of its own state).
- **Untrusted archives.** `sfl unzip` rejects an archive before writing anything if an entry name contains a `..` component, a drive letter or alternate-data-stream marker (`:`), a leading separator, or a component ending in a dot or space, or if its resolved path leaves the destination (zip-slip). It checks the password first, deletes a half-written file if the AES integrity check fails, never overwrites existing files without `--force`, and removes a destination it created if extraction fails. It does not limit decompressed size, so a hostile archive can still fill the disk.
- **`unzip` works on any computer** and needs only the ZIP password. It does not read or create the database.
- **Preview watcher.** `sfl preview` locks the folder again when Explorer's window closes, on Ctrl+C, and when its console window is closed. If the process is killed or the machine loses power, the folder stays readable until `sfl relock --all` (or the desktop app's next administrator sign-in) locks it; `sfl status` and `sfl list` warn about this state. A preview is not a security boundary against someone using the machine at that moment.
- **`SFL_DATABASE`** points the CLI at another database file. Whoever can set environment variables for the user can therefore point the CLI at a database whose master password they chose; that person already runs as the user.

Known limits: if UAC runs the elevated copy as a different account, Explorer-window detection for a preview may not see the user's Explorer windows (*not verified*); the folder then locks again after 20 seconds. The installers and binaries are not code-signed.

## Recovery

The application should record enough non-secret rule metadata to remove its own rule if the UI database entry is stale. A recovery command must fail explicitly if the expected rule is absent or ambiguous; it must not replace the entire ACL.

## Privacy

MVP data is local-only. Document the exact storage location and retention policy before implementation. Any future diagnostics must be opt-in and scrub paths and identifiers.

## Security review checklist

Test incorrect passwords, repeated attempts, path traversal-like inputs, missing folders, inherited ACLs, non-NTFS locations, cancellation, process interruption, and standard-user/admin behavior. Review dependencies and installer privileges before release.
