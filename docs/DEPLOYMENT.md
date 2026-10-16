# Deployment

**Status:** Publish and installer workflows implemented for the desktop app and the `sfl` command-line tool; release validation remains to be completed.

## Build and publish

For a small installer, use a self-contained, single-file Windows x64 publish. It avoids requiring a separate .NET 8 runtime on the target computer:

```powershell
dotnet publish .\SecureFolderLocker.csproj `
  --configuration Release `
  --framework net8.0-windows `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  --output .\bin\Release\net8.0-windows\publish
```

The generated executable is `SecureFolderLocker.exe`.

## Installer

The [SecureFolderLocker.iss](../SecureFolderLocker.iss) script packages the publish output with administrator privileges, an application directory under Program Files, and Start Menu/Desktop shortcuts. Run the publish command first, then open the script in Inno Setup Compiler and select **Build > Compile**. The generated installer is written to `installer\SecureFolderLockerSetup-1.0.0.exe`.

The script uses a self-contained `win-x64` publish. Do not package the SQLite database, logs, or user data.

## Automated releases (GitHub Actions)

Two workflows live in `.github/workflows/`:

- `ci.yml` restores, builds and runs the tests for the whole solution on every push to `main` and on pull requests.
- `release.yml` runs when a tag matching `v*` is pushed. It restores and runs the tests, publishes the desktop app and `sfl` as self-contained single files, runs a smoke test of the published `sfl.exe` (version matches the tag, `zip`, wrong-password `unzip` returns 3 and extracts nothing, correct `unzip`, no database created), installs Inno Setup with Chocolatey, compiles both installers with `/DMyAppVersion=<tag without v>`, writes a SHA-256 checksum for each, and creates a GitHub Release with `gh release create` containing `SecureFolderLockerSetup-<version>.exe`, `SfLCliSetup-<version>.exe` and their `.sha256` files (release notes generated from commits; tags containing `-`, such as `v1.1.0-beta1`, are marked as pre-releases). The smoke-test commands were run locally against the published exe; the workflow itself has not run on GitHub.

To release:

```powershell
git tag v1.0.1
git push origin v1.0.1
```

The workflow uses only the built-in `GITHUB_TOKEN` with `contents: write`; no secrets need to be configured. The installer is not code-signed, so Windows SmartScreen may warn on first run. The `installer/` folder is git-ignored; installers are distributed only through Releases. This workflow is proposed until it has run successfully on GitHub.

## Command-line tool installer

`sfl.exe` has its own installer, independent of the desktop app (different `AppId`), so either or both can be installed.

```powershell
dotnet publish .\src\Sfl.Cli\Sfl.Cli.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  --output .\bin\cli-publish

& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" /DMyAppVersion=1.0.1 .\sfl.iss
```

The result is `installer\SfLCliSetup-<version>.exe` (about 31 MB) containing the 35 MB single-file `sfl.exe`; no .NET runtime is needed on the target computer. `IncludeNativeLibrariesForSelfExtract` is required because the SQLite native library is unpacked from inside the file; the first launch after install takes a few seconds while Windows unpacks and scans it (about 5 s measured), later launches about a quarter of a second.

What the installer does:

- Installs `sfl.exe` to `C:\Program Files\SecureFolderLockerCLI` and **appends that folder to the system PATH** (a `REG_EXPAND_SZ` value, so other entries keep expanding). It does nothing if the folder is already there.
- On uninstall, removes exactly that entry and restores the rest of PATH byte for byte. User data (the database) is not touched.
- `SfLCliSetup-<version>.exe /CURRENTUSER` installs for the current user only: `%LOCALAPPDATA%\Programs\SecureFolderLockerCLI` and the user PATH, without administrator rights.
- `ChangesEnvironment=yes` tells Windows about the new PATH. Terminals that were already open keep the old PATH; open a new one.
- Silent: `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`.

Verification status: the per-user install and uninstall were run on a real machine (files installed, PATH entry added once even when installed twice, `sfl` found from a fresh environment, uninstall removed the folder and restored PATH exactly). The default administrator-mode install writes the same entry to the system PATH under `HKLM`; that path shares the code but has **not been run** yet (see [TASKS](TASKS.md) T-017 and T-019).

### Second computer

Install `SfLCliSetup-<version>.exe`, open a new terminal, and run `sfl unzip secret.zip`. It needs only the ZIP password. Verified with the published `sfl.exe` copied alone into an empty folder with no database present: the extraction succeeds and no database file is created.

## Secure ZIP export

The application uses SharpZipLib to create AES-256 encrypted ZIP archives. ZIP export is
performed at runtime and does not add archives or passwords to the installer. Users should
choose a destination outside the source folder and share the ZIP password separately.

### Extracting a Secure ZIP

Windows Explorer cannot open AES-encrypted ZIPs and fails with error `0x80004005`. Extract them with
the terminal extractor built into the app, which shows the app logo and details and asks for the
password (masked, three attempts):

```powershell
SecureFolderLocker.exe --extract "C:\path\Chamath.zip" [--out "C:\target"]
```

Dropping a `.zip` on the executable does the same. The installer also adds a right-click entry,
**Extract with Secure Folder Locker**, for `.zip` files (under "Show more options" on Windows 11).
By default files go to a new folder next to the ZIP; existing files are never overwritten. This
behavior is proposed until the installer entry has been verified on a clean machine.

## Release checks

- Build and publish from a clean checkout.
- Run automated tests (`dotnet test .\SecureFolderLocker.sln`).
- Install on a clean supported Windows environment.
- Verify first-run setup, lock, unlock, uninstall behavior, and ACL recovery, in the desktop app and with `sfl`.
- Install `SfLCliSetup` in administrator mode, open a new terminal, and confirm `sfl --version` and `sfl unzip` work; uninstall and confirm PATH is restored.
- Confirm the installer does not package databases, logs, or credentials.

## Rollback

Keep the prior installer available and document the version. Uninstallation must not delete user folders. Whether uninstall removes application metadata or leaves it for recovery is TBD and must be communicated to users.
