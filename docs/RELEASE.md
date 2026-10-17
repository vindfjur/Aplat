# Releasing

A checklist for the maintainer. Everything below runs on Windows.

1. **Version.** Set `AssemblyVersion` and `AssemblyFileVersion` in `Properties/AssemblyInfo.cs`.
2. **Changelog.** Move the *Unreleased* entries of [CHANGELOG.md](../CHANGELOG.md) under a new version heading with
   the date and update the compare links at the bottom.
3. **Build.** Run `build-installer.bat` (see [installer/README.md](../installer/README.md)). It writes the
   installer, the portable archive and their SHA-256 files to `dist/`.
4. **Documentation review.** Check that README, FEATURES (both languages), SECURITY and the screenshots match the
   release, that the requirements and links are correct and that the Russian and English texts say the same.
5. **Smoke test.** On a clean machine install the application, start it, draw on the canvas, add and change a 3D
   object, open and save a scene, export a PNG image and a model, then uninstall.
6. **Tag.** Create the tag `v<version>` and push it.
7. **Publish.** Create a GitHub Release from the tag, attach the files from `dist/` and paste the SHA-256 values
   into the release notes. The application's update check compares the release tag (`v1.2.3`) with the running
   version, so the tag must match `AssemblyVersion`. Do not publish drafts or pre-releases as the latest release
   unless that is intended.
8. **Verify the published files.** Download the files from the GitHub Release and check their SHA-256 values
   against the ones in the release notes.
