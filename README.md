<div align="center">

<img src="LogoMark.png" alt="Secure Folder Locker logo" width="96" />

# Secure Folder Locker

**Lock folders on Windows with Windows' own access-control rules, behind a master password.**
Preview them read-only, share them as AES-256 encrypted ZIPs, and let the app lock them again for you.

![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4?logo=windows&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet&logoColor=white)
![UI](https://img.shields.io/badge/UI-WPF--UI%20Fluent-5C2D91)
![Installer](https://img.shields.io/badge/installer-Inno%20Setup-2D6CDF)

<img src="docs/images/04-dashboard.png" alt="Secure Folder Locker dashboard" width="860" />

</div>

---

## Features

| | |
|---|---|
| 🔒 **Lock any folder** | Adds an application-owned deny rule to the folder's ACL, so Explorer and other apps cannot open it. Your existing permissions are preserved. |
| 👁️ **Read-only preview** | Enter your master password to open a locked folder read-only. The folder **locks itself again** when you close the Explorer window, when the app closes, or at your next sign-in if a session ended mid-preview. |
| 🫥 **Hide folders** | Optionally mark a locked folder as hidden and system, so it disappears from normal Explorer views. |
| 📦 **Zip & Share** | Export a folder, even a locked one, to a password-protected **AES-256** ZIP with its own password for the recipient. A locked folder is opened read-only only for the export and locked again afterwards. |
| 🛡️ **Starts without UAC** | The app opens like any normal program. It asks to restart as administrator only when you lock, unlock, preview, or export a locked folder. |
| 🔑 **Master password** | Stored only as a BCrypt hash. No plaintext password is saved. |
| 🎨 **Modern Windows 11 UI** | Fluent design with Mica backdrop, native title bar buttons, and automatic light and dark theme. |

## Screenshots

<table>
  <tr>
    <td align="center"><img src="docs/images/01-create-password.png" alt="Create master password" /><br /><sub><b>First run:</b> create your master password</sub></td>
    <td align="center"><img src="docs/images/03-sign-in.png" alt="Sign in" /><br /><sub><b>Sign in</b> with your master password</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="docs/images/02-empty-dashboard.png" alt="Empty dashboard" /><br /><sub><b>Dashboard:</b> nothing locked yet</sub></td>
    <td align="center"><img src="docs/images/04-dashboard.png" alt="Dashboard with folders" /><br /><sub><b>Dashboard:</b> locked, hidden, and previewing folders</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="docs/images/05-admin-required-dialog.png" alt="Administrator permission dialog" /><br /><sub><b>On-demand elevation:</b> no UAC at startup</sub></td>
    <td align="center"><img src="docs/images/06-preview-password.png" alt="Preview password prompt" /><br /><sub><b>Preview:</b> master password required</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="docs/images/07-zip-password.png" alt="Secure ZIP password" /><br /><sub><b>Zip &amp; Share:</b> set a separate ZIP password</sub></td>
    <td align="center"><img src="docs/images/09-dashboard-dark.png" alt="Dark theme" /><br /><sub><b>Dark theme</b> follows your Windows setting</sub></td>
  </tr>
</table>

## Install

1. Download `SecureFolderLockerSetup-<version>.exe` from the [latest release](../../releases/latest).
2. Run the installer. It needs administrator rights to install under Program Files.
3. Start **Secure Folder Locker** from the Start menu or desktop shortcut.

The installer is not code-signed yet, so Windows SmartScreen may show a warning the first time. The installer is self-contained and does not need a separate .NET runtime. Each release also includes a `.sha256` checksum file.

**Requirements:** Windows 10 or 11, 64-bit. An NTFS folder is needed for ACL protection.

## How to use

1. **Create a master password** on first launch. You will need it to sign in and to preview folders.
2. Click **Lock New Folder** and choose a folder. Turn on **Hide folder** first if you also want it hidden. Windows asks for administrator permission the first time, because the app restarts elevated and you sign in again.
3. Use the buttons on each folder:
   - **Preview** opens the folder read-only. It locks again when you close the Explorer window.
   - **Lock** restores the full lock immediately.
   - **Unlock** removes the protection and stops tracking the folder.
   - **Zip & Share** creates an encrypted ZIP you can send to someone.

## How it works

- **Locking** adds one deny rule for `Everyone` to the folder's ACL. The rule is identifiable, so the app removes exactly what it added and nothing else.
- **State** (the list of folders, hidden/preview flags, and the password hash) lives in a local SQLite database under `%LOCALAPPDATA%\SecureFolderLocker`. The database is updated only after the file-system change succeeds.
- **Preview** swaps the full-lock rule for a rule that denies writes and deletes but allows reads, then watches the Explorer window.
- **Elevation** is on demand. The app relaunches itself with the `runas` verb when an operation needs administrator rights.

## Security notes

This is a convenience and privacy control, not encryption.

- Folder protection uses Windows ACLs. **A Windows administrator, a bootable external environment, or direct disk access can bypass or change it.** The files themselves are not encrypted.
- Do not rely on it for highly sensitive data. Use disk encryption (BitLocker) or encrypted archives for that.
- The ZIP export **is** encrypted (AES-256), but Windows Explorer cannot open AES-encrypted ZIPs. Recipients need a tool such as 7-Zip or WinRAR. Share the ZIP password through a different channel than the ZIP.
- If you forget the master password, you cannot preview folders. Keep it safe.

See [docs/SECURITY.md](docs/SECURITY.md) for the full threat model.

## Build from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows.

```powershell
dotnet restore .\SecureFolderLocker.csproj
dotnet build .\SecureFolderLocker.csproj --no-restore
dotnet run --project .\SecureFolderLocker.csproj
```

Build the installer with [Inno Setup 6](https://jrsoftware.org/isinfo.php); the steps are in [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md).

### Releases

Pushing a version tag builds the installer and publishes a GitHub Release automatically:

```powershell
git tag v1.0.1
git push origin v1.0.1
```

## Status

The app is implemented and has been checked manually: sign-in, lock, preview auto-lock, ZIP export, and on-demand elevation. **Automated tests are not written yet.** The release workflow is new and has not been run on GitHub yet. See the [roadmap](docs/ROADMAP.md) and [task list](docs/TASKS.md).

## Tech stack

- C# and .NET 8, WPF with [WPF-UI](https://github.com/lepoco/wpfui) (Fluent)
- SQLite via `Microsoft.Data.Sqlite`
- BCrypt via `BCrypt.Net-Next`
- AES-256 ZIP via SharpZipLib
- Inno Setup installer and GitHub Actions release pipeline

## Documentation

Start with the [documentation index](docs/README.md). The [PRD](docs/PRD.md) defines the product, and [ARCHITECTURE](docs/ARCHITECTURE.md) describes the implementation.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Maintainer details are in [CONTRIBUTORS.md](CONTRIBUTORS.md).
