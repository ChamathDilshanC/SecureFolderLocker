# Product Requirements Document

## Status and vision

**Status:** Proposed for a greenfield .NET 8 Windows desktop application.

Secure Folder Locker should let a non-technical Windows user protect selected folders with a password, understand the current protection state, and recover safely from errors. It is not intended to replace enterprise endpoint security or encrypted storage.

## Problem and users

Users need a simple local privacy control for folders on a Windows computer. Primary users are the device owner and an administrator who can authorize ACL changes. There is no multi-user cloud account in the MVP.

## Goals and non-goals

**Goals**

- Protect and restore folder access using Windows ACLs.
- Store only a salted password hash and minimal folder metadata.
- Make lock, unlock, and failure states understandable.
- Provide a repeatable Windows installer.

**Non-goals**

- File encryption or ransomware protection.
- Remote access, cloud synchronization, or account recovery.
- Bypassing Windows administrator controls.
- Guaranteeing protection against another administrator or offline disk access.

## MVP scope

The MVP includes first-run password setup, password verification, adding/removing folders, lock/unlock actions, persisted state, administrator elevation, and clear error messages. WPF-UI styling and an Inno Setup package are planned.

## Functional requirements

| ID | Priority | Requirement | Acceptance criteria |
| --- | --- | --- | --- |
| FR-001 | Must | Configure a password | A new user can set and confirm a password; only its BCrypt hash is persisted. |
| FR-002 | Must | Verify a password | Correct credentials succeed; incorrect credentials do not reveal protected data or the stored hash. |
| FR-003 | Must | Add a folder | Existing local folders are validated and shown with their protection state. |
| FR-004 | Must | Lock a folder | The application applies the approved ACL policy and persists success only after the OS operation succeeds. |
| FR-005 | Must | Unlock a folder | The application removes only its own protection rule and restores access without deleting unrelated rules. |
| FR-006 | Must | Handle failures | Unauthorized, missing, invalid, and partially failed operations produce actionable errors and do not report false success. |
| FR-007 | Should | Manage entries | A user can remove a folder from the application without deleting the folder itself. |
| FR-008 | Should | Explain limitations | The UI explains administrator access, ACL limitations, and that this is not encryption. |

## Workflows and edge cases

The application must handle cancelled folder selection, duplicate paths, moved/deleted folders, inherited permissions, already-locked folders, externally changed ACLs, insufficient elevation, and interrupted writes. State must be reconciled with the filesystem rather than trusting SQLite alone.

## Non-functional requirements

- **Security:** no plaintext passwords, least-privilege application services, explicit ACL ownership, and safe error messages.
- **Reliability:** atomic database updates where possible and recovery guidance after ACL failures.
- **Privacy:** local-only metadata by default; no telemetry in the MVP.
- **Accessibility:** keyboard navigation, visible focus, readable contrast, and screen-reader names.
- **Performance:** normal UI actions should remain responsive; exact targets are TBD after a prototype.

## Success indicators

Proposed indicators are successful first-run setup, successful lock/unlock on supported NTFS folders, no false-success reports in failure tests, and completion of the manual administrator smoke test. Numeric targets are TBD.

## Risks and open questions

- ACL deny rules can be confusing and can affect legitimate administrators.
- A crash during an ACL operation needs a tested recovery strategy.
- The exact rule identity and backup format must be finalized before implementation.
- Whether multiple passwords or recovery reset are supported is TBD; MVP assumes one local password with no reset flow.
