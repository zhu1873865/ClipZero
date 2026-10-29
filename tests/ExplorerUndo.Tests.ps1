#requires -Version 5.1
<#
.SYNOPSIS
Dependency-free, safe tests for the optional Explorer undo helper.
.DESCRIPTION
Only Status and operations with -WhatIf are invoked. Tests never write the real
registry. JSON fixtures are isolated, exact paths are checked before deletion,
and the user's original MaxUndoItems value is compared after all tests.
#>
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string] $TemporaryDirectory = (Join-Path ([IO.Path]::GetTempPath()) ('ClipZero-ExplorerUndo-Tests-' + [Guid]::NewGuid().ToString('N')))
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'These tests require Windows; no registry setting has been changed.'
}

$helperPath = Join-Path $PSScriptRoot '..\scripts\Set-ExplorerUndo.ps1'
$helperPath = [IO.Path]::GetFullPath($helperPath)
$fixtureDirectory = [IO.Path]::GetFullPath($TemporaryDirectory)
$fixturePrefix = $fixtureDirectory.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$createdDirectory = -not [IO.Directory]::Exists($fixtureDirectory)
$null = [IO.Directory]::CreateDirectory($fixtureDirectory)
$fixturePaths = New-Object 'System.Collections.Generic.List[string]'
$script:passed = 0
$script:failed = 0
$script:skipped = 0

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

function Assert-ExactValue {
    param([object] $Expected, [object] $Actual)
    Assert-True ($Expected.GetType() -eq $Actual.GetType()) 'Decoded CLR type differs from the original.'
    if ($Expected -is [Array]) {
        Assert-True ($Expected.Length -eq $Actual.Length) 'Decoded array length differs.'
        for ($index = 0; $index -lt $Expected.Length; $index++) {
            Assert-True ($Expected[$index] -ceq $Actual[$index]) ('Decoded array element differs at index ' + $index + '.')
        }
    }
    else {
        Assert-True ($Expected -ceq $Actual) 'Decoded value differs from the original.'
    }
}

function Assert-Throws {
    param([scriptblock] $Operation, [string] $MessagePattern = '*')
    $threw = $false
    try { & $Operation | Out-Null }
    catch {
        $threw = $true
        Assert-True ($_.Exception.Message -like $MessagePattern) ('Unexpected error: ' + $_.Exception.Message)
    }
    Assert-True $threw 'Expected the operation to refuse invalid or conflicting data.'
}

function Invoke-Test {
    param([string] $Name, [scriptblock] $Operation)
    try {
        & $Operation | Out-Null
        $script:passed++
        Write-Host ('PASS ' + $Name)
    }
    catch {
        $script:failed++
        Write-Host ('FAIL ' + $Name + ': ' + $_.Exception.Message)
    }
}

function Read-NativeSetting {
    $readKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced', $false)
    try {
        if ($null -eq $readKey -or $readKey.GetValueNames() -notcontains 'MaxUndoItems') {
            return [pscustomobject]@{ Exists = $false; Kind = $null; Value = $null }
        }
        return [pscustomobject]@{
            Exists = $true
            Kind = $readKey.GetValueKind('MaxUndoItems')
            Value = $readKey.GetValue('MaxUndoItems', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        }
    }
    finally { if ($null -ne $readKey) { $readKey.Dispose() } }
}

function New-FixturePath {
    $path = Join-Path $fixtureDirectory ('fixture-' + [Guid]::NewGuid().ToString('N') + '.json')
    $fixturePaths.Add($path)
    return $path
}

function Write-Fixture {
    param([string] $Path, [string] $Text)
    # Fixtures are always new files, so the test runner cannot overwrite existing data.
    $fixtureStream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $bytes = (New-Object Text.UTF8Encoding($false)).GetBytes($Text)
        $fixtureStream.Write($bytes, 0, $bytes.Length)
    }
    finally { $fixtureStream.Dispose() }
}

function New-BackupRecord {
    return [pscustomobject]@{
        SchemaVersion = 1
        RegistryHive = 'CurrentUser'
        RegistryPath = 'Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
        RegistryUserSid = $script:ExplorerUndoUserSid
        ValueName = 'MaxUndoItems'
        Original = [pscustomobject]@{ Exists = $false; Kind = $null; Data = $null }
    }
}

$nativeBefore = Read-NativeSetting
try {
    Invoke-Test 'PowerShell helper parses without errors' {
        $tokens = $null
        $parseErrors = $null
        $null = [Management.Automation.Language.Parser]::ParseFile($helperPath, [ref] $tokens, [ref] $parseErrors)
        Assert-True ($parseErrors.Count -eq 0) 'The helper has a PowerShell syntax error.'
    }

    # Dot-source only its read-only entry point to load pure conversion functions.
    $noBackupPath = New-FixturePath
    . $helperPath -Action Status -BackupPath $noBackupPath | Out-Null

    $cases = @(
        @{ Name = 'String'; Kind = 'String'; Value = [string] 'sample' },
        @{ Name = 'empty String'; Kind = 'String'; Value = [string] '' },
        @{ Name = 'unexpanded ExpandString'; Kind = 'ExpandString'; Value = [string] '%USERPROFILE%\ClipZero' },
        @{ Name = 'Unicode MultiString'; Kind = 'MultiString'; Value = [string[]] @('one', ([char] 0x4E8C).ToString()) },
        @{ Name = 'empty MultiString'; Kind = 'MultiString'; Value = [string[]] @() },
        @{ Name = 'single MultiString'; Kind = 'MultiString'; Value = [string[]] @('one') },
        @{ Name = 'minimum signed DWord'; Kind = 'DWord'; Value = [int]::MinValue },
        @{ Name = 'maximum signed DWord'; Kind = 'DWord'; Value = [int]::MaxValue },
        @{ Name = 'zero DWord'; Kind = 'DWord'; Value = [int] 0 },
        @{ Name = 'maximum QWord'; Kind = 'QWord'; Value = [long]::MaxValue },
        @{ Name = 'minimum QWord'; Kind = 'QWord'; Value = [long]::MinValue },
        @{ Name = 'Binary'; Kind = 'Binary'; Value = [byte[]] @(0, 1, 255) },
        @{ Name = 'empty Binary'; Kind = 'Binary'; Value = [byte[]] @() },
        @{ Name = 'REG_NONE'; Kind = 'None'; Value = [byte[]] @(0, 1, 255) }
    )
    foreach ($testCase in $cases) {
        Invoke-Test ('JSON preserves exact type and value: ' + $testCase.Name) {
            $snapshot = ConvertTo-ExplorerUndoSnapshot -Kind $testCase.Kind -Value $testCase.Value
            $json = ConvertTo-Json -InputObject $snapshot -Depth 5
            $decoded = ConvertFrom-ExplorerUndoSnapshot (ConvertFrom-Json -InputObject $json)
            Assert-True $decoded.Exists 'Existing value became absent.'
            Assert-True ($decoded.Kind.ToString() -ceq $testCase.Kind) 'Registry value kind changed.'
            Assert-ExactValue -Expected $testCase.Value -Actual $decoded.Value
        }
    }

    Invoke-Test 'Absent original value remains absent' {
        $decoded = ConvertFrom-ExplorerUndoSnapshot ([pscustomobject]@{ Exists = $false; Kind = $null; Data = $null })
        Assert-True (-not $decoded.Exists -and $null -eq $decoded.Kind -and $null -eq $decoded.Value) 'Absent value was not preserved.'
    }
    Invoke-Test 'Rejects non-Boolean existence record' {
        Assert-Throws { ConvertFrom-ExplorerUndoSnapshot ([pscustomobject]@{ Exists = 'false'; Kind = $null; Data = $null }) }
    }
    Invoke-Test 'Rejects unexpected data for absent value' {
        Assert-Throws { ConvertFrom-ExplorerUndoSnapshot ([pscustomobject]@{ Exists = $false; Kind = 'DWord'; Data = '0' }) }
    }
    Invoke-Test 'Rejects out-of-range DWord' {
        Assert-Throws { ConvertFrom-ExplorerUndoSnapshot ([pscustomobject]@{ Exists = $true; Kind = 'DWord'; Data = '4294967296' }) }
    }
    Invoke-Test 'Rejects unknown registry kind' {
        Assert-Throws { ConvertFrom-ExplorerUndoSnapshot ([pscustomobject]@{ Exists = $true; Kind = 'Unexpected'; Data = '0' }) }
    }
    Invoke-Test 'Rejects invalid binary encoding' {
        Assert-Throws { ConvertFrom-ExplorerUndoSnapshot ([pscustomobject]@{ Exists = $true; Kind = 'Binary'; Data = '***' }) }
    }

    $validBackupPath = New-FixturePath
    Write-Fixture $validBackupPath (ConvertTo-Json -InputObject (New-BackupRecord) -Depth 6)
    Invoke-Test 'Status validates a correct backup without changing it' {
        $beforeText = [IO.File]::ReadAllText($validBackupPath)
        $status = & $helperPath -Action Status -BackupPath $validBackupPath
        Assert-True ($status.Backup -ceq 'Valid') 'A correct backup was not recognized.'
        Assert-True ([IO.File]::ReadAllText($validBackupPath) -ceq $beforeText) 'Status modified the backup.'
    }
    Invoke-Test 'Existing original-value backup is never overwritten' {
        $beforeText = [IO.File]::ReadAllText($validBackupPath)
        if ($nativeBefore.Exists -and $nativeBefore.Kind -eq [Microsoft.Win32.RegistryValueKind]::DWord -and $nativeBefore.Value -eq 0) {
            $null = & $helperPath -Action Disable -WhatIf -BackupPath $validBackupPath
        }
        else {
            Assert-Throws { & $helperPath -Action Disable -WhatIf -BackupPath $validBackupPath } '*changed after the backup*'
        }
        Assert-True ([IO.File]::ReadAllText($validBackupPath) -ceq $beforeText) 'Disable overwrote an existing backup.'
    }
    if ($nativeBefore.Exists -and $nativeBefore.Kind -eq [Microsoft.Win32.RegistryValueKind]::DWord -and $nativeBefore.Value -eq 0) {
        $script:skipped++
        Write-Host 'SKIP Restore conflict guard: the real setting is already DWORD 0; it is left untouched.'
    }
    else {
        Invoke-Test 'Restore refuses a registry state that differs from DWORD 0' {
            Assert-Throws { & $helperPath -Action Restore -WhatIf -BackupPath $validBackupPath } '*changed after the backup*'
            Assert-True ([IO.File]::Exists($validBackupPath)) 'Conflicting Restore discarded the backup.'
        }
    }

    foreach ($mutation in @('SchemaVersion', 'RegistryHive', 'RegistryPath', 'RegistryUserSid', 'ValueName')) {
        Invoke-Test ('Rejects backup mismatch: ' + $mutation) {
            $record = New-BackupRecord
            if ($mutation -eq 'SchemaVersion') { $record.$mutation = 2 }
            else { $record.$mutation = 'Unexpected' }
            $path = New-FixturePath
            Write-Fixture $path (ConvertTo-Json -InputObject $record -Depth 6)
            Assert-Throws { & $helperPath -Action Status -BackupPath $path } '*unexpected registry setting*'
        }
    }
    Invoke-Test 'Rejects incomplete backup' {
        $path = New-FixturePath
        Write-Fixture $path '{"SchemaVersion":1}'
        Assert-Throws { & $helperPath -Action Status -BackupPath $path } '*not a ClipZero Explorer undo backup*'
    }
    Invoke-Test 'Rejects corrupt JSON' {
        $path = New-FixturePath
        Write-Fixture $path '{invalid-json'
        Assert-Throws { & $helperPath -Action Status -BackupPath $path }
    }
    Invoke-Test 'Restore without a backup refuses to guess the original' {
        Assert-Throws { & $helperPath -Action Restore -WhatIf -BackupPath $noBackupPath } '*original setting is unknown*'
    }
    Invoke-Test 'Disable WhatIf creates no backup' {
        $null = & $helperPath -Action Disable -WhatIf -BackupPath $noBackupPath
        Assert-True (-not [IO.File]::Exists($noBackupPath)) 'WhatIf wrote a backup file.'
    }
    Invoke-Test 'Real MaxUndoItems is unchanged after all tests' {
        $nativeAfter = Read-NativeSetting
        Assert-True ($nativeBefore.Exists -eq $nativeAfter.Exists) 'Real registry value existence changed during the tests.'
        if ($nativeBefore.Exists) {
            Assert-True ($nativeBefore.Kind -eq $nativeAfter.Kind) 'Real registry value kind changed during the tests.'
            Assert-ExactValue -Expected $nativeBefore.Value -Actual $nativeAfter.Value
        }
    }
}
catch {
    $script:failed++
    Write-Host ('FAIL test setup: ' + $_.Exception.Message)
}
finally {
    foreach ($fixturePath in $fixturePaths) {
        $absoluteFixture = [IO.Path]::GetFullPath($fixturePath)
        if (-not $absoluteFixture.StartsWith($fixturePrefix, [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($absoluteFixture) -notmatch '^fixture-[0-9a-f]{32}\.json$') {
            throw 'Refusing to delete a fixture outside this test directory.'
        }
        if ([IO.File]::Exists($absoluteFixture)) { [IO.File]::Delete($absoluteFixture) }
    }
    if ($createdDirectory -and [IO.Directory]::Exists($fixtureDirectory)) {
        # Nonrecursive removal succeeds only when our newly created directory is empty.
        [IO.Directory]::Delete($fixtureDirectory, $false)
    }
}

Write-Host ('Explorer undo helper tests: ' + $script:passed + ' passed, ' + $script:failed + ' failed, ' + $script:skipped + ' skipped.')
if ($script:failed -gt 0) { exit 1 }
exit 0
