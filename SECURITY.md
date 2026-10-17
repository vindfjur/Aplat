# Security Policy

Aplat is a desktop editor. It has no plug-in system and does not run other programs automatically.

## Network and external data

- **Update check.** The only network activity is the update check: one HTTPS request to `api.github.com` that
  reads the tag of the latest release (at most once a day at start-up, or on demand with Help → Check for updates;
  it can be switched off in the same menu). Aplat sends no telemetry and no project content, downloads nothing
  and installs nothing. As with any HTTPS request, GitHub sees the connection's network address. A newer version
  only offers to open the release page in the browser; the browser is started only when you ask for it.
- **Local communication.** When a project is opened from Explorer while Aplat is already running, the second
  process passes the file path to the first through a named pipe that is local to this machine and user, and
  exits. Only the path of an existing `.aplat` file is accepted.

## Trust boundaries

Input that Aplat does not control and does not trust:

- the `.aplat` scenes and the images you open or drop on the window;
- the file path given on the command line or through the named pipe;
- the response of the update check.

## Supported versions

Security fixes are made for the latest stable release only. Upgrade to the newest release to receive them.

| Version | Supported |
|---|---|
| Latest stable release | Yes |
| Older releases | No |

## Reporting a vulnerability

Please do not open a public issue. Use GitHub's **Security → Report a vulnerability** on this
repository (a private advisory). Include the file or the steps that reproduce the problem.

This project is maintained in spare time, so a first reply may take up to about two weeks, depending on the
maintainer's availability. Once a fix is released, the advisory is published and credits you if you wish.

## What is checked

- Scene files are parsed with DTD and entity processing disabled and with size caps on every
  archive entry (a defence against decompression bombs). No parser can promise to catch every malformed file.
- A scene cannot make the app read network paths (`\\host\share`), devices or URLs as textures.
- Saving writes a temporary file, verifies it, then swaps it in. This lowers the risk of damaging the
  previous version if a write fails.
- The installer installs per user (no administrator rights) from an explicit list of files.
  Release builds ship with a SHA-256 checksum.

## Known limits

- The executable is a managed .NET assembly and can be decompiled; the source is public anyway.
- Installers are not Authenticode-signed, so Windows SmartScreen may warn. A SHA-256 checksum published with a
  release detects a damaged download, not a forged one; download only from this repository's Releases page.
- Image decoding uses the Windows GDI+ library; keep Windows updated.
- A `.aplat` file is not encrypted and contains the edit history.
