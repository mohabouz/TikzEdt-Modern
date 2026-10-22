[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.3.0',
    [string]$IsccPath = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    [string]$PublishedDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if (-not (Test-Path -LiteralPath $IsccPath -PathType Leaf)) { throw "Inno Setup compiler not found: $IsccPath" }
$outputDirectory = Join-Path $repoRoot 'artifacts\setup'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

if (-not $PublishedDirectory) {
    $PublishedDirectory = Join-Path $repoRoot ('artifacts\installer-payload\' + $Version + '\' + [guid]::NewGuid().ToString('N'))
    & dotnet publish (Join-Path $repoRoot 'TikzEdt.Modern\TikzEdt.Modern.csproj') -c Release -r win-x64 --self-contained true -o $PublishedDirectory '-p:NuGetAudit=false' "-p:Version=$Version" '-p:DebugType=None' '-p:DebugSymbols=false'
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }
}
$PublishedDirectory = (Resolve-Path -LiteralPath $PublishedDirectory).Path
foreach ($required in @('TikzEdt.Modern.exe', 'TikzEdt.Modern.dll', 'coreclr.dll', 'PresentationFramework.dll', 'TheSnippets.xml', 'CircuitikzCatalog.json', 'TikzSyntax.xshd', 'TikzParser.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $PublishedDirectory $required))) { throw "Payload is missing $required. A self-contained Windows x64 publish is required." }
}
$runtime = Get-Content -LiteralPath (Join-Path $PublishedDirectory 'TikzEdt.Modern.runtimeconfig.json') -Raw | ConvertFrom-Json
if (-not $runtime.runtimeOptions.includedFrameworks) { throw 'Payload is framework-dependent; .NET must be bundled.' }

$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
foreach ($framework in $runtime.runtimeOptions.includedFrameworks) {
    $packageName = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
    $packageRoot = Join-Path $nugetRoot ($packageName + '\' + $framework.version)
    $licenseDestination = Join-Path $PublishedDirectory ('licenses\' + $framework.name)
    New-Item -ItemType Directory -Path $licenseDestination -Force | Out-Null
    foreach ($licenseName in @('LICENSE', 'LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
        $licenseSource = Join-Path $packageRoot $licenseName
        if (Test-Path -LiteralPath $licenseSource -PathType Leaf) { Copy-Item -LiteralPath $licenseSource -Destination $licenseDestination -Force }
    }
    if (-not (Get-ChildItem -LiteralPath $licenseDestination -File)) { throw "Runtime license files missing: $packageRoot" }
}

& $IsccPath /Q "/DAppVersion=$Version" "/DPublishDir=$PublishedDirectory" "/DSetupOutputDir=$outputDirectory" (Join-Path $repoRoot 'installer\TikzEdt.Modern.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
$setupPath = Join-Path $outputDirectory "TikzEdt-Modern-$Version-Setup-win-x64.exe"
$hash = Get-FileHash -LiteralPath $setupPath -Algorithm SHA256
[IO.File]::WriteAllText($setupPath + '.sha256', $hash.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($setupPath) + [Environment]::NewLine)
$metadata = [ordered]@{
    Version = $Version
    Architecture = 'win-x64'
    SelfContained = $true
    BuiltAtUtc = [DateTime]::UtcNow.ToString('O')
    Payload = $PublishedDirectory
    Installer = $setupPath
    Sha256 = $hash.Hash.ToLowerInvariant()
    Files = @(Get-ChildItem -LiteralPath $PublishedDirectory -Recurse -File | Where-Object Extension -ne '.pdb' | ForEach-Object { $_.FullName.Substring($PublishedDirectory.Length + 1) })
}
[IO.File]::WriteAllText($setupPath + '.manifest.json', ($metadata | ConvertTo-Json -Depth 5))
Write-Host "Setup ready: $setupPath"
Write-Host "SHA256: $($hash.Hash)"
