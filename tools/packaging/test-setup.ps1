[CmdletBinding()]
param([string]$Version = '0.3.0')
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{8B743F29-7F43-4B02-B2CA-F7F5CE68B7B8}_is1'
if (Test-Path -LiteralPath $uninstallKey) { throw 'An installed TikzEdt Modern copy already exists. Refusing to replace its uninstall registration for a test.' }
$groupName = 'TikzEdt Modern'
$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) $groupName
if (Test-Path -LiteralPath $startMenu) { throw 'TikzEdt Modern Start-menu folder already exists. Refusing to alter it for a test.' }
$setup = Join-Path $repoRoot "artifacts\setup\TikzEdt-Modern-$Version-Setup-win-x64.exe"
$metadata = Get-Content -LiteralPath ($setup + '.manifest.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant() -ne $metadata.Sha256) { throw 'Setup checksum does not match manifest.' }
$testRoot = Join-Path $repoRoot ('artifacts\setup-smoke\' + [guid]::NewGuid().ToString('N'))
$testDirectory = Join-Path $testRoot 'installed'
$resolvedTest = [IO.Path]::GetFullPath($testDirectory)
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\setup-smoke')) + [IO.Path]::DirectorySeparatorChar
if (-not $resolvedTest.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test directory.' }
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$installed = $false
try {
    $process = Start-Process -FilePath $setup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=`"$testDirectory`"", "/LOG=`"$testRoot\install.log`"") -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Install failed: $($process.ExitCode). See $testRoot\install.log" }
    $installed = $true
    foreach ($relativePath in $metadata.Files | Where-Object { [IO.Path]::GetExtension($_) -ne '.pdb' }) {
        $source = Join-Path $metadata.Payload $relativePath
        $destination = Join-Path $testDirectory $relativePath
        if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) { throw "Missing installed file: $relativePath" }
        if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) { throw "Installed file hash mismatch: $relativePath" }
    }
    if (-not (Test-Path -LiteralPath $uninstallKey)) { throw 'Uninstall registration was not created.' }
    if (-not (Test-Path -LiteralPath (Join-Path $startMenu 'TikzEdt Modern.lnk'))) { throw 'Start menu shortcut missing.' }
    $installedRuntime = Get-Content -LiteralPath (Join-Path $testDirectory 'TikzEdt.Modern.runtimeconfig.json') -Raw | ConvertFrom-Json
    if (-not $installedRuntime.runtimeOptions.includedFrameworks) { throw '.NET runtime was not included.' }
    Write-Host "Installed payload verified: $($metadata.Files.Count) files; self-contained runtime, catalogues, Start menu shortcut and uninstall registration."
}
finally {
    if ($installed) {
        $uninstaller = Join-Path $testDirectory 'unins000.exe'
        $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$testRoot\uninstall.log`"") -WindowStyle Hidden -PassThru -Wait
        if ($process.ExitCode -ne 0) { throw "Test uninstall failed: $($process.ExitCode)" }
        if (Test-Path -LiteralPath $uninstallKey) { throw 'Test uninstall registration was left behind.' }
        if (Test-Path -LiteralPath (Join-Path $testDirectory 'TikzEdt.Modern.exe')) { throw 'Test app executable was left behind.' }
        Write-Host "Test installation removed by its uninstaller. Logs retained: $testRoot"
    }
}
