# Testing Strategy

**Status:** Proposed; no test project exists yet.

## Test pyramid

- **Unit:** password setup rules, path validation, state transitions, ACL rule construction, and error mapping.
- **Integration:** SQLite migrations/repositories and Windows ACL adapter on disposable test folders.
- **UI/manual:** first-run flow, keyboard navigation, elevation prompt, lock/unlock feedback, and recovery.

## Critical scenarios

1. Set and verify a password without persisting plaintext.
2. Reject incorrect passwords without changing filesystem state.
3. Lock and unlock a supported folder while preserving unrelated ACL rules.
4. Handle missing, duplicate, or unsupported paths.
5. Refuse false success after ACL or database failure.
6. Reconcile stale database state with actual ACL state.
7. Run as a standard user and as administrator.

## Quality gates

Every implementation change must pass the repository build, targeted tests, and relevant manual checks. Windows-only ACL tests should be clearly marked and isolated from destructive real-user paths.
