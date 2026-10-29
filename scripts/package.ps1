[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [ValidateSet('framework-dependent', 'self-contained')]
    [string]$Mode = 'self-contained',

    [ValidatePattern('^10\.0\.[0-9]+$')]
    [string]$RuntimeFrameworkVersion = '10.0.14'
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectFile = Join-Path $projectRoot 'src/ClipZero/ClipZero.csproj'
[xml]$project = Get-Content -LiteralPath $projectFile -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Invalid package version: $version"
}

& (Join-Path $PSScriptRoot 'publish.ps1') -Runtime $Runtime -Mode $Mode -RuntimeFrameworkVersion $RuntimeFrameworkVersion
$publishPath = Join-Path $projectRoot "artifacts/publish/$Runtime/$Mode"
$packageRoot = Join-Path $projectRoot 'artifacts/packages'
$packageName = "ClipZero-v$version-$Runtime-$Mode.zip"
$packagePath = Join-Path $packageRoot $packageName
$temporaryPackage = Join-Path $packageRoot ".staging-$([Guid]::NewGuid().ToString('N')).zip"

function Assert-PackagePath([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $allowedPrefix = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts')).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to change a path outside artifacts: $fullPath"
    }
    $candidate = $fullPath
    while ($candidate -and $candidate.Length -ge $projectRoot.Length) {
        if (Test-Path -LiteralPath $candidate) {
            $item = Get-Item -LiteralPath $candidate -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing to use a linked package path: $candidate"
            }
        }
        $candidate = [IO.Path]::GetDirectoryName($candidate)
    }
    return $fullPath
}

$publishPath = Assert-PackagePath $publishPath
$packagePath = Assert-PackagePath $packagePath
$temporaryPackage = Assert-PackagePath $temporaryPackage
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE'), (Join-Path $projectRoot 'README.md') -Destination $publishPath
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $publishPath -Recurse
New-Item -ItemType Directory -Path (Join-Path $publishPath 'scripts') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Set-ExplorerUndo.ps1') -Destination (Join-Path $publishPath 'scripts')

$commit = 'unknown'
$sourceState = 'unknown'
if (Get-Command git -ErrorAction SilentlyContinue) {
    $revision = & git -C $projectRoot rev-parse --verify HEAD 2>$null
    if ($LASTEXITCODE -eq 0) {
        $commit = [string]$revision
        $changes = & git -C $projectRoot status --porcelain --untracked-files=normal
        if ($LASTEXITCODE -eq 0) { $sourceState = if ($changes) { 'uncommitted changes' } else { 'clean' } }
    }
}
$buildInfo = @(
    "ClipZero version: $version",
    "Source commit: $commit",
    "Source state: $sourceState",
    "Runtime identifier: $Runtime",
    "Deployment mode: $Mode",
    "Runtime framework version: $RuntimeFrameworkVersion"
)
[IO.File]::WriteAllLines((Join-Path $publishPath 'BUILD-INFO.txt'), $buildInfo, [Text.Encoding]::ASCII)

New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
try {
    [IO.Compression.ZipFile]::CreateFromDirectory($publishPath, $temporaryPackage, [IO.Compression.CompressionLevel]::Optimal, $false)
    $archive = [IO.Compression.ZipFile]::OpenRead($temporaryPackage)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        $required = @('ClipZero.exe', 'LICENSE', 'README.md', 'BUILD-INFO.txt', 'docs/release-notes.md', 'scripts/Set-ExplorerUndo.ps1')
        if ($Mode -eq 'framework-dependent') {
            $required += @('ClipZero.dll', 'ClipZero.Core.dll', 'ClipZero.deps.json', 'ClipZero.runtimeconfig.json')
        }
        foreach ($entry in $required) {
            if ($entries -notcontains $entry) { throw "Package is missing: $entry" }
        }
    }
    finally { $archive.Dispose() }

    $packagePath = Assert-PackagePath $packagePath
    Move-Item -LiteralPath $temporaryPackage -Destination $packagePath -Force
    $hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $hashPath = Assert-PackagePath ($packagePath + '.sha256')
    [IO.File]::WriteAllText($hashPath, "$hash  $packageName`r`n", [Text.Encoding]::ASCII)
}
finally {
    if (Test-Path -LiteralPath $temporaryPackage) {
        $temporaryPackage = Assert-PackagePath $temporaryPackage
        Remove-Item -LiteralPath $temporaryPackage -Force
    }
}

Write-Host "Packaged: $packagePath"
Write-Host "SHA-256: $hash"
