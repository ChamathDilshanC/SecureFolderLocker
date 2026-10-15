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

## Recovery

The application should record enough non-secret rule metadata to remove its own rule if the UI database entry is stale. A recovery command must fail explicitly if the expected rule is absent or ambiguous; it must not replace the entire ACL.

## Privacy

MVP data is local-only. Document the exact storage location and retention policy before implementation. Any future diagnostics must be opt-in and scrub paths and identifiers.

## Security review checklist

Test incorrect passwords, repeated attempts, path traversal-like inputs, missing folders, inherited ACLs, non-NTFS locations, cancellation, process interruption, and standard-user/admin behavior. Review dependencies and installer privileges before release.
