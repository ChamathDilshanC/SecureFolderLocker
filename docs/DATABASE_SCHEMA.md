# Database Schema

**Status:** Proposed SQLite schema; migrations are not implemented.

## Rules

Store no plaintext password. Use parameterized commands, a schema version, foreign keys where relevant, and an atomic transaction for related changes.

## Tables

### `app_settings`

| Column | Type | Rules |
| --- | --- | --- |
| `key` | TEXT | Primary key |
| `value` | TEXT | Required; only non-secret settings or password hash values |
| `updated_at_utc` | TEXT | Required ISO-8601 UTC timestamp |

The password hash is stored under a controlled key or a dedicated future credential table; the final choice is an implementation decision. Never store the password itself.

### `protected_folders`

| Column | Type | Rules |
| --- | --- | --- |
| `id` | INTEGER | Primary key |
| `canonical_path` | TEXT | Required, unique |
| `display_name` | TEXT | Required |
| `state` | TEXT | Required: `Unknown`, `Locked`, `Unlocked`, `Error` |
| `rule_marker` | TEXT | Required identifier for the application-owned ACL rule |
| `is_hidden` | INTEGER | Required boolean (`0` or `1`) for Hidden/System attributes |
| `is_preview_mode` | INTEGER | Required boolean (`0` or `1`) for read-only preview ACL mode |
| `last_verified_at_utc` | TEXT | Nullable UTC timestamp |
| `created_at_utc` | TEXT | Required UTC timestamp |
| `updated_at_utc` | TEXT | Required UTC timestamp |

## Consistency

Filesystem ACL state is authoritative. A startup reconciliation pass should mark stale entries as `Error` or `Unknown` rather than silently presenting them as locked. Migrations and backup/restore policy are TBD.

The current implementation creates `IsHidden` and `IsPreviewMode` with defaults of `0` and adds missing columns to existing databases through a startup schema migration.
