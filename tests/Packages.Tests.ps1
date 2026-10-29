[CmdletBinding()]
param(
    [switch]$RequireClean,

    [ValidatePattern('^10\.0\.[0-9]+$')]
    [string]$RuntimeFrameworkVersion = '10.0.14'
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'src/ClipZero/ClipZero.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Invalid package version: $version"
}

$commit = $null
if ($RequireClean) {
    $revision = & git -C $projectRoot rev-parse --verify HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Could not determine the current source commit.' }
    $commit = ([string]$revision).Trim()
    $changes = & git -C $projectRoot status --porcelain --untracked-files=normal
    if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Commit all source changes before requiring clean package provenance.' }
}

$localDotnet = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
if (Test-Path -LiteralPath $localDotnet -PathType Leaf) {
    $dotnetPath = $localDotnet
}
else {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnetCommand) { throw 'Install the .NET 10 SDK or add dotnet to PATH before running package smoke tests.' }
    $dotnetPath = $dotnetCommand.Source
}
$testAssembly = Join-Path $projectRoot 'tests/ClipZero.Tests/bin/Release/net10.0-windows/ClipZero.Tests.dll'
if (-not (Test-Path -LiteralPath $testAssembly -PathType Leaf)) {
    throw 'Build ClipZero.slnx in Release before verifying packages.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($runtime in @('win-x64','win-arm64')) {
    foreach ($mode in @('self-contained','framework-dependent')) {
        $name = "ClipZero-v$version-$runtime-$mode.zip"
        $path = Join-Path $projectRoot "artifacts/packages/$name"
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        $hashRecord = [IO.File]::ReadAllText($path + '.sha256').Trim()
        if ($hashRecord -ne "$hash  $name") { throw "Checksum mismatch: $name" }
        $archive = [IO.Compression.ZipFile]::OpenRead($path)
        try {
            $entryNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($entry in $archive.Entries) {
                $entryPath = $entry.FullName.Replace('\','/')
                if ($entryPath -match '(^|/)(\.git|\.tools|bin|obj|artifacts)(/|$)|(^|/)\.\.(/|$)|explorer-undo-backup\.json$|:' -or [IO.Path]::IsPathRooted($entryPath)) {
                    throw "Unexpected package entry: $entryPath"
                }
                if (-not $entryNames.Add($entryPath)) { throw "Duplicate package entry: $entryPath" }
            }
            $requiredEntries = @('ClipZero.exe','LICENSE','README.md','BUILD-INFO.txt','docs/release-notes.md','docs/testing.md','scripts/Set-ExplorerUndo.ps1')
            if ($mode -eq 'framework-dependent') {
                $requiredEntries += @('ClipZero.dll','ClipZero.Core.dll','ClipZero.deps.json','ClipZero.runtimeconfig.json')
            }
            foreach ($required in $requiredEntries) {
                if (-not $archive.GetEntry($required)) { throw "Missing entry: $required" }
            }
            $reader = [IO.StreamReader]::new($archive.GetEntry('BUILD-INFO.txt').Open())
            try { $info = $reader.ReadToEnd() } finally { $reader.Dispose() }
            $expectedInfo = @(
                "ClipZero version: $version", "Runtime identifier: $runtime", "Deployment mode: $mode",
                "Runtime framework version: $RuntimeFrameworkVersion"
            )
            if ($RequireClean) { $expectedInfo += @("Source commit: $commit", 'Source state: clean') }
            foreach ($line in $expectedInfo) {
                if ($info -notmatch ('(?m)^' + [regex]::Escape($line) + '\r?$')) {
                    throw "Package provenance mismatch: expected $line"
                }
            }
            if ($mode -eq 'framework-dependent') {
                $reader = [IO.StreamReader]::new($archive.GetEntry('ClipZero.runtimeconfig.json').Open())
                try { $config = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
                $frameworks = @($config.runtimeOptions.frameworks)
                if ($frameworks.Count -ne 2 -or
                    @($frameworks | Where-Object version -NE $RuntimeFrameworkVersion).Count -gt 0 -or
                    @($frameworks | Where-Object name -EQ 'Microsoft.NETCore.App').Count -ne 1 -or
                    @($frameworks | Where-Object name -EQ 'Microsoft.WindowsDesktop.App').Count -ne 1) {
                    throw 'Runtime configuration mismatch.'
                }
            }
            $stream = $archive.GetEntry('ClipZero.exe').Open()
            $memory = [IO.MemoryStream]::new()
            try { $stream.CopyTo($memory); $bytes = $memory.ToArray() } finally { $stream.Dispose(); $memory.Dispose() }
            if ($bytes.Length -lt 64 -or [BitConverter]::ToUInt16($bytes, 0) -ne 0x5a4d) {
                throw 'Executable is missing its DOS header.'
            }
            $peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
            if ($peOffset -lt 64 -or $peOffset -gt $bytes.Length - 6 -or
                [BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x00004550) {
                throw 'Executable has an invalid PE header.'
            }
            $machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
            $expectedMachine = if ($runtime -eq 'win-x64') { 0x8664 } else { 0xaa64 }
            if ($machine -ne $expectedMachine) { throw 'Executable architecture mismatch.' }
            Write-Output "PASS Package content, architecture, runtime and SHA-256: $name"
        }
        finally { $archive.Dispose() }
    }
}
$verifyPath = [IO.Path]::GetFullPath((Join-Path $projectRoot ('artifacts/verify/' + [guid]::NewGuid().ToString('N'))))
$prefix = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts/verify')) + [IO.Path]::DirectorySeparatorChar
if (-not $verifyPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid extraction target.' }
$candidate = $verifyPath
while ($candidate -and $candidate.Length -ge $projectRoot.Length) {
    if (Test-Path -LiteralPath $candidate) {
        $item = Get-Item -LiteralPath $candidate -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to extract through a linked path: $candidate"
        }
    }
    $candidate = [IO.Path]::GetDirectoryName($candidate)
}
if (Test-Path -LiteralPath $verifyPath) { throw 'Extraction target already exists.' }
[IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $projectRoot "artifacts/packages/ClipZero-v$version-win-x64-self-contained.zip"), $verifyPath)
$executable = Join-Path $verifyPath 'ClipZero.exe'
if ($RequireClean) {
    $productVersion = (Get-Item -LiteralPath $executable).VersionInfo.ProductVersion
    if ($productVersion -notmatch ('^' + [regex]::Escape("$version+$commit") + '(?:$|\.)')) {
        throw "EXE source version mismatch: $productVersion"
    }
    Write-Output "PASS EXE product version identifies clean source commit: $productVersion"
}
# This launches only the extracted x64 EXE on a private desktop. It never requests
# clipboard clearing, settings changes, keyboard input, or a desktop switch.
Push-Location $projectRoot
try {
    $smokeOutput = @(& $dotnetPath $testAssembly --portable $executable)
    $smokeExitCode = $LASTEXITCODE
    $smokeOutput | Write-Output
    if ($smokeExitCode -ne 0 -or -not ($smokeOutput -match '^Portable: 3/3 passed;')) {
        throw 'Extracted portable EXE smoke test did not pass completely; a SKIP is not a pass.'
    }
}
finally { Pop-Location }
Write-Output "Verified extracted executable: $executable"
