# Windows Desktop Notes

**Status:** Proposed.

The application targets Windows and starts as the invoking user (`asInvoker` manifest) so it opens without a UAC prompt. ACL operations (lock, unlock, preview, restore lock) require administrator rights: when the process is not elevated, `MainViewModel` shows a confirmation dialog and, on approval, `ElevationService` relaunches the app with the `runas` verb. The user signs in again in the elevated instance. Folder selection should use a Windows-native picker where practical. The app must detect unsupported filesystems or locations before changing permissions.

Exporting a fully locked folder to ZIP temporarily switches it to read-only preview, then restores the full lock in a `finally` block (administrator required). If the lock cannot be restored, the status bar tells the user to press Lock.

ACL operations can affect inherited permissions and administrator access. The implementation must preserve the original security descriptor or an application-owned recovery record sufficient to remove its own rule. Do not assume that a deny rule is equivalent to encryption.

The optional super-hiding mode also applies `Hidden` and `System` file attributes. Unlocking removes the ACL rule and restores normal file attributes. Attribute changes can fail independently of ACL changes and must be reported as an error rather than recorded as a successful hidden lock.

Preview mode removes the application-owned full-control deny rule and applies a narrower deny rule for write and delete operations, while retaining read/execute access. `Modify` is intentionally not denied because Windows defines it to include read permissions; denying it would prevent preview access.

When preview starts, the application opens the folder in Windows Explorer and monitors the Explorer shell window for that exact folder URL. Once that window is closed or navigated away from, the application restores the full lock automatically. The manual **Fully Lock** action remains available as a fallback if Explorer is unavailable or shell-window enumeration is interrupted.

Installer and runtime behavior must be tested on supported Windows versions once the minimum version is chosen. That minimum is currently TBD.

## Preview mode

Preview opens the folder read-only in Explorer and watches for the window to close (a window browsing a sub-folder still counts as open). The full lock is restored when the window closes, when Explorer never shows the folder within 20 seconds, when Explorer cannot be launched, when the app closes, and at the next sign-in if a previous session ended mid-preview.
