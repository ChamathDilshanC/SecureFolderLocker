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
