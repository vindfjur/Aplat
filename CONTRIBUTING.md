# Contributing to Aplat

Thank you for your interest. Small, readable changes are preferred over large rewrites.
Please open an issue before starting anything large.

## Getting set up

Aplat is a Windows program. The application is built and tested on Windows; a few self-contained tests can also
run under Mono on other systems (see Tests).

- Open `Aplat.sln` in Visual Studio 2019+ (with the .NET Framework 4.7.2 targeting pack), or run:

  ```
  msbuild Aplat.csproj /p:Configuration=Release
  ```

- The language level is C# 7.3. Match the surrounding code's naming, comments and layout.
- The installer needs Inno Setup 6.3+: run `build-installer.bat` (see [installer/README.md](installer/README.md)).

## Tests

Each folder under `tools/` is a small console test program. `scenefile` and `inktest` compile the
needed sources alone and run with [mono](https://www.mono-project.com/) on any system. The others
check the built `Aplat.exe` and need Windows, so build Release first.

| Folder | What it checks | How to run |
|---|---|---|
| `tools/scenefile` | the `.aplat` container and undo history | `tools/scenefile/run.sh` |
| `tools/inktest` | the ink journal and material ink | `tools/inktest/run.sh` |
| `tools/modeltest` | models, primitives, camera, export, hostile files | `tools/paneltest/run.ps1 -Source tools\modeltest\ModelTests.cs` |
| `tools/paneltest` | the inspector panel | `tools/paneltest/run.ps1` |
| `tools/formtest` | the main window's commands and dialogs | `tools/paneltest/run.ps1 -Source tools\formtest\FormTests.cs` |
| `tools/uitest` | drives the real window with mouse and keyboard; needs an interactive desktop with OpenGL | `tools/paneltest/run.ps1 -Source tools\uitest\UiTests.cs` |
| `tools/rulertest` | the ruler: coordinate mapping, guiding the pen along its edges, handles, the scene object and picking | `tools/paneltest/run.ps1 -Source tools\rulertest\RulerTests.cs` |
| `tools/docktest` | docking of the floating panels (`DockTests.cs`; `DockUiTests.cs` drives the real window) | `tools/paneltest/run.ps1 -Source tools\docktest\DockTests.cs` |
| `tools/matchtest` | the command search's matching: typos, layouts, ranking, "did you mean" | `tools/paneltest/run.ps1 -Source tools\matchtest\MatchTests.cs` |
| `tools/commandtest` | the command search lists every command, in both languages | `tools/paneltest/run.ps1 -Source tools\commandtest\CommandTests.cs` |
| `tools/sizetest` | the size slider means one stroke width for every drawing tool | `tools/paneltest/run.ps1 -Source tools\sizetest\SizeTests.cs` |
| `tools/updatetest` | the update check's parsing and version comparison (no network) | `tools/paneltest/run.ps1 -Source tools\updatetest\UpdateTests.cs` |
| `tools/opentest` | opening a project from the command line and from a second launch | `tools/paneltest/run.ps1 -Source tools\opentest\OpenTests.cs` |
| `tools/eyedroppertest` | the eyedropper's return-to-previous-tool option | `tools/paneltest/run.ps1 -Source tools\eyedroppertest\EyedropperTests.cs` |
| `tools/perftest` | the FPS and load plate (View → Show FPS) and the About dialog | `tools/paneltest/run.ps1 -Source tools\perftest\PerfTests.cs` |
| `tools/editshapetest` | the Shape mode with real mouse input (`EditShapeTests.cs`), and painting on a deformed shape: picking against an independent ray cast, dab placement, paint kept on its faces, timings (`PaintDeformedTests.cs`) | `tools/paneltest/run.ps1 -Source tools\editshapetest\EditShapeTests.cs` (or `PaintDeformedTests.cs`) |
| `tools/suntest` | the light marker (sun symbol and arrow) in both themes and at two zoom levels | `tools/paneltest/run.ps1 -Source tools\suntest\SunMarkerTests.cs` |
| `tools/texttest` | text and shapes on a 3D shape: upright from every side, one undo step, Esc removes them, no streaks across atlas islands, no painting through occluders | `tools/paneltest/run.ps1 -Source tools\texttest\TextOrientationTests.cs` (and `SurfacePaintingTests.cs`) |
| `tools/loctest` | the English interface has no Russian text left (windows, menus, dialogs) | `tools/paneltest/run.ps1 -Source tools\loctest\LocTests.cs` |

`tools/exporttest/ExportDump.cs` is not a test but a helper that writes sample OBJ, STL and FBX files to open in
other programs; `tools/iconscene/gen.cs` regenerates the application icon.

Before opening a pull request, run the tests that touch what you changed and add a test for a bug you fix.
Changes to the interface should also be tried by hand in both themes and both languages.

### Diagnostics

The environment variable `APLAT_SHADOWS=off` draws without shadows, as on a driver without the needed OpenGL
features. `APLAT_SHADOWS=force` skips the OpenGL version check for shadows and is meant only for diagnosing
drivers; it can make the program fail on drivers that lack the features.

## Translating the interface

The interface exists in Russian and English. The Russian text in the code is also the key: write every
user-visible text as `Loc.T("Русский текст")` (or `Loc.F("…{0}…", value)` for a format) and add a line
`t["Русский текст"] = "English text";` to `Localization/English.cs`. Keep placeholders such as `{0}` and
escapes such as `\n` identical in both. `tools/loctest` proves that no Russian is left in the windows. The language is
chosen at start-up (View → Language, else the Windows language) and applies after a restart.

## Pull requests

1. Fork, create a branch and keep the change focused.
2. Describe what changed and why, and how you checked it.
3. Update `CHANGELOG.md` under *Unreleased* for anything a user would notice.
4. Scene files are versioned (`FormatVersion` in `IO/SceneFileManager.cs`): bump it when the
   layout changes, and keep reading older versions once a release has shipped.

By submitting a pull request you agree that your contribution is licensed under
Apache-2.0, like the rest of the project.

## Reporting bugs and security problems

Use the issue templates for bugs and ideas. For a vulnerability, follow
[SECURITY.md](SECURITY.md) and do not open a public issue.
