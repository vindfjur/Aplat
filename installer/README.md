# Windows installer

The installer is built with [Inno Setup](https://jrsoftware.org/) from the Release build. Everything below runs
on Windows.

## Building

Requirements:

- Visual Studio or Build Tools with the desktop .NET build components, the C# 7.3 compiler and the
  .NET Framework 4.7.2 targeting pack;
- Inno Setup 6.3 or newer with the Russian language files.

Run `build-installer.bat` from the project folder. It builds the Release configuration, compiles the installer
and packs the portable archive. Nothing is downloaded or installed by the script, and it does not start Aplat or
touch Git. If the Inno Setup compiler is in a non-standard place:

```powershell
.\installer\build-installer.ps1 -ISCCPath 'C:\Tools\Inno Setup 6\ISCC.exe'
```

The output is written to `dist/`:

| File | Content |
|---|---|
| `Aplat-<version>-Setup.exe` | the installer |
| `Aplat-<version>-Setup.exe.sha256` | its SHA-256 checksum |
| `Aplat-<version>-Setup.exe.manifest.json` | the names, sizes and checksums of the installed files |
| `Aplat-<version>-Portable.zip` | the same files without an installer |
| `Aplat-<version>-Portable.zip.sha256` | its SHA-256 checksum |

The version is taken from `Properties/AssemblyInfo.cs`; `AssemblyVersion` and `AssemblyFileVersion` must be
changed together.

## What is installed

Seven explicitly listed files (there are no wildcard masks), plus the uninstaller that Inno Setup creates:

- `Aplat.exe`, `Aplat.exe.config`
- `OpenTK.dll`, `OpenTK.GLControl.dll`
- `LICENSE.txt`, `THIRD_PARTY_NOTICES.md`, `OpenTK-LICENSE.txt`

The icon is part of the executable. Sources, debug symbols, scripts and Git data are not packaged, and the build
runs in a temporary folder that is removed afterwards.

## Behaviour

- Per-user installation into `%LOCALAPPDATA%\Programs\vindfjur\Aplat`; no administrator rights.
- Requires Windows 10 or newer and .NET Framework 4.7.2 or newer. Without it the setup stops with an explanation
  before copying anything; it never downloads a runtime.
- The setup is available in Russian and English, offers an optional desktop shortcut, registers in the list of
  installed applications, updates an existing installation in place and refuses to downgrade. The `AppId`, the
  installation type and the publisher folder must not change between releases without a migration plan.
- The `.aplat` file type is registered for the current user only (keys under `HKCU\Software\Classes`), so that a
  double click opens a project in Aplat. The uninstaller removes those keys.
- The uninstaller removes only the files it installed; the settings in `%APPDATA%\Aplat` and your projects are
  left alone.
- Graphics depend on the OpenGL driver, which includes the graphics adapter of a virtual machine.

## Checks before a release

1. Build the installer and check the seven entries of the manifest.
2. On a clean Windows, install as an ordinary user, decline and accept the licence, start Aplat from the Start
   menu, open, save and reopen a project.
3. Install a newer version over an older one and confirm that settings are kept and that installing an older
   version is refused.
4. Uninstall through Windows settings, confirm that projects and settings remain, then install again.
5. On a system without the required .NET Framework confirm that the setup stops before copying files, also with
   `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`.

## Signing

The executable and the installer are not signed. A release with a verified publisher needs an Authenticode
certificate or a signing service: sign the application before packaging, then the installer and the uninstaller
through SignTool in Inno Setup, and recalculate the SHA-256 afterwards. A checksum does not replace a digital
signature. Never keep certificates, passwords or private keys in the repository.
