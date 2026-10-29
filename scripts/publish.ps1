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
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $projectRoot 'src/ClipZero/ClipZero.csproj'
$outputPath = Join-Path $projectRoot "artifacts/publish/$Runtime/$Mode"
$publishRoot = Join-Path $projectRoot 'artifacts/publish'
$stagingPath = Join-Path $projectRoot "artifacts/publish/$Runtime/.staging-$Mode-$([Guid]::NewGuid().ToString('N'))"

function Assert-PublishPath([string]$Path) {
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $allowedPrefix = [System.IO.Path]::GetFullPath($publishRoot).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to change a path outside artifacts/publish: $fullPath"
    }

    # Reject junctions/symlinks in the target and its ancestors before changing files.
    $candidate = $fullPath
    while ($candidate -and $candidate.Length -ge $projectRoot.Length) {
        if (Test-Path -LiteralPath $candidate) {
            $item = Get-Item -LiteralPath $candidate -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing to use a linked publish path: $candidate"
            }
        }
        $candidate = [IO.Path]::GetDirectoryName($candidate)
    }
    return $fullPath
}

$outputPath = Assert-PublishPath $outputPath
$stagingPath = Assert-PublishPath $stagingPath

$localDotnetPath = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
if (Test-Path -LiteralPath $localDotnetPath -PathType Leaf) {
    $dotnetPath = $localDotnetPath
}
else {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnetCommand) {
        throw 'Install the .NET 10 SDK or add dotnet to PATH before publishing.'
    }
    $dotnetPath = $dotnetCommand.Source
}

$publishArguments = @(
    'publish', $projectPath,
    '-c', 'Release',
    '-r', $Runtime,
    '--self-contained', $(if ($Mode -eq 'self-contained') { 'true' } else { 'false' }),
    '-p:UseSharedCompilation=false',
    '-p:PublishTrimmed=false',
    '-p:DebugType=none',
    '-p:DebugSymbols=false',
    "-p:RuntimeFrameworkVersion=$RuntimeFrameworkVersion",
    '-o', $stagingPath
)

if ($Mode -eq 'self-contained') {
    $publishArguments += '-p:PublishSingleFile=true'
    $publishArguments += '-p:IncludeNativeLibrariesForSelfExtract=true'
    $publishArguments += '-p:EnableCompressionInSingleFile=true'
}
else {
    $publishArguments += '-p:PublishSingleFile=false'
}

Write-Host "Publishing ClipZero ($Runtime, $Mode)..."
Push-Location $projectRoot
try {
    & $dotnetPath @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $executablePath = Join-Path $stagingPath 'ClipZero.exe'
    if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
        throw "Expected executable was not produced: $executablePath"
    }

    # dotnet publish does not remove unrelated files left by earlier builds.
    # Only replace this architecture/mode after a fresh publish has succeeded.
    $outputPath = Assert-PublishPath $outputPath
    if (Test-Path -LiteralPath $outputPath) {
        $linkedChildren = Get-ChildItem -LiteralPath $outputPath -Force -Recurse |
            Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 }
        if ($linkedChildren) { throw 'Refusing to replace publish output containing links.' }
        Remove-Item -LiteralPath $outputPath -Recurse -Force
    }
    $stagingPath = Assert-PublishPath $stagingPath
    Move-Item -LiteralPath $stagingPath -Destination $outputPath
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $stagingPath) {
        $stagingPath = Assert-PublishPath $stagingPath
        Remove-Item -LiteralPath $stagingPath -Recurse -Force
    }
}

Write-Host "Published: $outputPath"
Write-Host "Runtime framework version: $RuntimeFrameworkVersion"
if ($Mode -eq 'framework-dependent') {
    Write-Host "Keep all files together. Requires .NET Desktop Runtime $RuntimeFrameworkVersion or a newer 10.0 patch of the same architecture."
}
else {
    Write-Host 'The application EXE includes its compressed runtime; startup decompresses the bundle and native runtime files extract into the temporary directory.'
}
