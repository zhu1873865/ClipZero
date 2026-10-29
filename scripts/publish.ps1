[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [ValidateSet('framework-dependent', 'self-contained')]
    [string]$Mode = 'self-contained'
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $projectRoot 'src/ClipZero/ClipZero.csproj'
$outputPath = Join-Path $projectRoot "artifacts/publish/$Runtime/$Mode"

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
    '-o', $outputPath
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
}
finally {
    Pop-Location
}

$executablePath = Join-Path $outputPath 'ClipZero.exe'
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Expected executable was not produced: $executablePath"
}

Write-Host "Published: $outputPath"
if ($Mode -eq 'framework-dependent') {
    Write-Host 'Keep all files together. Requires .NET Desktop Runtime 10 of the same architecture.'
}
else {
    Write-Host 'The application EXE includes its compressed runtime; startup decompresses the bundle and native runtime files extract into the temporary directory.'
}
