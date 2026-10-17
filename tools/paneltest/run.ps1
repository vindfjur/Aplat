# Windows only. Build Release first (build-installer.bat or msbuild), then:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\paneltest\run.ps1
param([string]$ProjectRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent), [string]$Source = (Join-Path $PSScriptRoot 'PanelTests.cs'))
$ErrorActionPreference = 'Stop'
$release = Join-Path $ProjectRoot 'bin\Release'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products '*' -property installationPath
$csc = Get-ChildItem $vs -Recurse -Filter csc.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
if (-not $csc) { throw 'Roslyn csc.exe not found in Visual Studio.' }
$exe = Join-Path $release ([IO.Path]::GetFileNameWithoutExtension($Source) + '.exe')
try {
    & $csc /nologo /langversion:latest /target:exe "/out:$exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll "/r:$release\Aplat.exe" "/r:$release\OpenTK.dll" "/r:$release\OpenTK.GLControl.dll" $Source
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw 'Panel tests failed.' }
} finally { Remove-Item -LiteralPath $exe -ErrorAction SilentlyContinue }
