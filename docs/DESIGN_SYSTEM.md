# Design System

**Status:** Proposed. No UI implementation exists yet.

## Principles

1. Make protection state obvious.
2. Explain consequences before changing filesystem permissions.
3. Prefer recoverable actions and explicit errors.
4. Keep the primary flow usable with keyboard and screen readers.

## Visual direction

Use WPF-UI Fluent styling with a Windows 11-compatible light/dark theme. Existing color tokens do not exist; the following are proposals and must be checked for contrast in implementation:

- `Accent`: system accent for primary actions.
- `Surface`: window and card backgrounds.
- `Success`, `Warning`, `Danger`, `Info`: semantic status colors paired with text and icons.

Use the system UI font, a clear hierarchy of title/body/caption text, consistent 8px spacing increments, restrained corner radii, and elevation only to separate surfaces.

## Components and states

Required components: navigation shell, password setup form, password prompt, folder list, status badge, folder picker action, confirmation dialog, error banner, and toast/inline success notification. Each interactive component needs default, hover, keyboard-focus, disabled, loading, error, and empty states.

## Main screens

- First-run password setup.
- Protected folders dashboard.
- Unlock/password verification dialog.
- Settings/about and limitation information.

## Interaction rules

Lock and unlock are destructive permission changes and require a confirmation step. The UI must disable repeated submission while an operation is running, preserve the selected path, and show an error with the next safe action. Support `Tab`, `Shift+Tab`, `Enter`, and `Escape`.

Respect reduced-motion preferences. Do not use color alone to communicate state. Keep status text adjacent to the affected folder.
