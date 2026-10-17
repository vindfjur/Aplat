# Windows only. No downloads, repository synchronization or application launch.
[CmdletBinding()]
param([string]$ISCCPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Run this script inside Windows only.' }
$root = Split-Path $PSScriptRoot -Parent
$work = $null
try {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) {
        throw 'Install Visual Studio Build Tools with .NET desktop build tools and the .NET Framework 4.7.2 targeting pack.'
    }
    $found = @(& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe')
    if ($LASTEXITCODE -ne 0 -or $found.Count -eq 0) { throw 'Visual Studio MSBuild was not found.' }
    $msbuild = $found[0]
    if (-not $ISCCPath) {
        $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($command) { $ISCCPath = $command.Source }
        else { $ISCCPath = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
    }
    if (-not (Test-Path -LiteralPath $ISCCPath -PathType Leaf)) {
        throw 'Install Inno Setup 6.3 or newer, or pass -ISCCPath with the full path to ISCC.exe.'
    }
    # ISCC may have a 0.0.0.0 Windows version resource. Aplat.iss checks
    # the actual compiler version through its preprocessor instead.
    foreach ($relative in @('libs\OpenTK.dll', 'libs\OpenTK.GLControl.dll', 'LICENSE.txt',
        'THIRD_PARTY_NOTICES.md', 'installer\OpenTK-LICENSE.txt', 'icon.ico')) {
        if (-not (Test-Path -LiteralPath (Join-Path $root $relative) -PathType Leaf)) {
            throw "Required file is missing: $relative"
        }
    }
    $work = Join-Path ([IO.Path]::GetTempPath()) ('Aplat-installer-' + [guid]::NewGuid().ToString('N'))
    $build = Join-Path $work 'build'
    $payload = Join-Path $work 'payload'
    New-Item -ItemType Directory -Path $build, $payload | Out-Null
    $project = Join-Path $root 'Aplat.csproj'
    $buildArgs = @($project, '/t:Rebuild', '/p:Configuration=Release', '/p:Platform=AnyCPU',
        "/p:OutputPath=$build/", "/p:BaseIntermediateOutputPath=$work/obj/",
        "/p:IntermediateOutputPath=$work/obj/Release/", '/p:DebugSymbols=false',
        '/p:DebugType=None', '/nologo', '/verbosity:minimal')
    & $msbuild @buildArgs
    if ($LASTEXITCODE -ne 0) { throw "Release build failed ($LASTEXITCODE)." }
    $allowed = @('Aplat.exe', 'Aplat.exe.config', 'OpenTK.dll', 'OpenTK.GLControl.dll')
    foreach ($name in $allowed) {
        $file = Join-Path $build $name
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Build output is missing: $name" }
        Copy-Item -LiteralPath $file -Destination $payload
    }
    # Validate the complete staging directory, not just the files we expected to find.
    $unexpected = @(Get-ChildItem -LiteralPath $payload -Recurse -Force |
        Where-Object { $_.PSIsContainer -or $_.Name -notin $allowed })
    if ($unexpected.Count) { throw 'Unexpected content in installer staging directory.' }
    $exe = Join-Path $payload 'Aplat.exe'
    $versionInfo = (Get-Item -LiteralPath $exe).VersionInfo
    $version = '{0}.{1}.{2}.{3}' -f $versionInfo.FileMajorPart, $versionInfo.FileMinorPart,
        $versionInfo.FileBuildPart, $versionInfo.FilePrivatePart
    if ($version -eq '0.0.0.0') { throw 'Application file version is missing.' }
    $output = Join-Path $root 'dist'
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $setup = Join-Path $output "Aplat-$version-Setup.exe"
    # Never leave an old artifact with the same name after a failed packaging attempt.
    foreach ($old in @($setup, "$setup.sha256", "$setup.manifest.json")) {
        if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Force }
    }
    & $ISCCPath "/DAppVersion=$version" "/DPayloadDir=$payload" "/DInstallerOutput=$output" (Join-Path $PSScriptRoot 'Aplat.iss')
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $setup)) { throw 'Installer compilation failed.' }
    $manifest = @()
    foreach ($name in $allowed) {
        $file = Join-Path $payload $name
        $manifest += [ordered]@{ Name = $name; Bytes = (Get-Item -LiteralPath $file).Length; SHA256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash }
    }
    foreach ($entry in @(@('LICENSE.txt','LICENSE.txt'), @('THIRD_PARTY_NOTICES.md','THIRD_PARTY_NOTICES.md'), @('installer\OpenTK-LICENSE.txt','OpenTK-LICENSE.txt'))) {
        $file = Join-Path $root $entry[0]
        $manifest += [ordered]@{ Name = $entry[1]; Bytes = (Get-Item -LiteralPath $file).Length; SHA256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash }
    }
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$setup.manifest.json" -Encoding UTF8
    $hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash
    "$hash  $([IO.Path]::GetFileName($setup))" | Set-Content -LiteralPath "$setup.sha256" -Encoding ASCII
    # Portable archive: the same four files plus the licences. No installer, no registry, no administrator rights.
    $portable = Join-Path $output "Aplat-$version-Portable.zip"
    foreach ($old in @($portable, "$portable.sha256")) {
        if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Force }
    }
    $stage = Join-Path $work 'portable\Aplat'
    New-Item -ItemType Directory -Path $stage | Out-Null
    foreach ($name in $allowed) { Copy-Item -LiteralPath (Join-Path $payload $name) -Destination $stage }
    foreach ($relative in @('LICENSE.txt', 'THIRD_PARTY_NOTICES.md', 'installer\OpenTK-LICENSE.txt')) {
        Copy-Item -LiteralPath (Join-Path $root $relative) -Destination $stage
    }
    Compress-Archive -Path $stage -DestinationPath $portable -CompressionLevel Optimal
    if (-not (Test-Path -LiteralPath $portable)) { throw 'Portable archive was not created.' }
    $portableHash = (Get-FileHash -LiteralPath $portable -Algorithm SHA256).Hash
    "$portableHash  $([IO.Path]::GetFileName($portable))" | Set-Content -LiteralPath "$portable.sha256" -Encoding ASCII
    Write-Host "Portable ready: $portable"
    Write-Host "Installer ready: $setup"
    Write-Host 'Included: four runtime files and three license/notice files. Inno Setup also adds its uninstaller.'
    Write-Host 'This build is unsigned. SHA256 is an integrity checksum, not a digital signature.'
} catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
} finally {
    if ($work -and (Test-Path -LiteralPath $work)) { Remove-Item -LiteralPath $work -Recurse -Force }
}
