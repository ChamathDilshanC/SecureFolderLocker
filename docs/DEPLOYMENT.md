# Deployment

**Status:** Publish and installer workflow implemented; release validation remains to be completed.

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

- `ci.yml` restores and builds on every push to `main` and on pull requests.
- `release.yml` runs when a tag matching `v*` is pushed. It publishes the self-contained build, installs Inno Setup with Chocolatey, compiles the installer with `/DMyAppVersion=<tag without v>`, writes a SHA-256 checksum, and creates a GitHub Release with `gh release create` (release notes generated from commits; tags containing `-`, such as `v1.1.0-beta1`, are marked as pre-releases).

To release:

```powershell
git tag v1.0.1
git push origin v1.0.1
```

The workflow uses only the built-in `GITHUB_TOKEN` with `contents: write`; no secrets need to be configured. The installer is not code-signed, so Windows SmartScreen may warn on first run. The `installer/` folder is git-ignored; installers are distributed only through Releases. This workflow is proposed until it has run successfully on GitHub.

## Secure ZIP export

The application uses SharpZipLib to create AES-256 encrypted ZIP archives. ZIP export is
performed at runtime and does not add archives or passwords to the installer. Users should
choose a destination outside the source folder and share the ZIP password separately.

## Release checks

- Build and publish from a clean checkout.
- Run automated tests.
- Install on a clean supported Windows environment.
- Verify first-run setup, lock, unlock, uninstall behavior, and ACL recovery.
- Confirm the installer does not package databases, logs, or credentials.

## Rollback

Keep the prior installer available and document the version. Uninstallation must not delete user folders. Whether uninstall removes application metadata or leaves it for recovery is TBD and must be communicated to users.
