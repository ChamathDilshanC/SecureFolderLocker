# ADR-0001: Use an Application-Owned Windows ACL Rule

- **Status:** Proposed
- **Date:** 2026-10-15

## Context

The MVP needs local folder access protection without claiming to provide encryption. Windows ACLs are integrated with the filesystem, but replacing an entire security descriptor could destroy user permissions.

## Decision

Implement a dedicated adapter that adds and removes one identifiable application-owned rule. It must read and verify the current descriptor, preserve unrelated rules, and require administrator authorization for changes. SQLite state is updated only after the filesystem operation succeeds.

## Consequences

This provides a simple Windows-native control and a recoverable ownership boundary. It does not protect against administrators, offline disk access, or all filesystem implementations. The rule marker, exact identity, and recovery record format remain implementation details to validate in Windows integration tests.
