#requires -Version 5.1
<#
.SYNOPSIS
Reads, disables, or restores the current user's Explorer file-operation undo setting.
.DESCRIPTION
MaxUndoItems is an undocumented Windows setting. Nothing is changed unless the
Disable or Restore action is explicitly requested. Explorer is never restarted.
The prior registry value is backed up before Disable writes DWORD 0.
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [ValidateSet('Status', 'Disable', 'Restore')]
    [string] $Action = 'Status',

    [ValidateNotNullOrEmpty()]
    [string] $BackupPath = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ClipZero\explorer-undo-backup.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:ExplorerUndoRegistryPath = 'Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
$script:ExplorerUndoValueName = 'MaxUndoItems'

function Get-ExplorerUndoSnapshot {
    param([AllowNull()] [Microsoft.Win32.RegistryKey] $Key)

    if ($null -eq $Key -or $Key.GetValueNames() -notcontains $script:ExplorerUndoValueName) {
        return [pscustomobject]@{ Exists = $false; Kind = $null; Data = $null }
    }

    $kind = $Key.GetValueKind($script:ExplorerUndoValueName)
    $value = $Key.GetValue($script:ExplorerUndoValueName, $null,
        [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
    return ConvertTo-ExplorerUndoSnapshot -Kind $kind -Value $value
}

function ConvertTo-ExplorerUndoSnapshot {
    param(
        [Microsoft.Win32.RegistryValueKind] $Kind,
        [AllowNull()] [object] $Value
    )

    # Strings preserve ExpandString tokens; integer strings avoid JSON precision loss.
    switch ($Kind.ToString()) {
        'String'       { $data = [string] $Value }
        'ExpandString' { $data = [string] $Value }
        'MultiString'  { $data = [string[]] @($Value) }
        'DWord'        { $data = ([int] $Value).ToString([Globalization.CultureInfo]::InvariantCulture) }
        'QWord'        { $data = ([long] $Value).ToString([Globalization.CultureInfo]::InvariantCulture) }
        'Binary'       { $data = [Convert]::ToBase64String([byte[]] $Value) }
        'None'         { $data = [Convert]::ToBase64String([byte[]] $Value) }
        default        { throw "Unsupported original registry value kind: $Kind. No change was made." }
    }

    return [pscustomobject]@{ Exists = $true; Kind = $Kind.ToString(); Data = $data }
}

function ConvertFrom-ExplorerUndoSnapshot {
    param([object] $Snapshot)

    if ($null -eq $Snapshot -or $null -eq $Snapshot.PSObject.Properties['Exists'] -or
        $Snapshot.Exists -isnot [bool]) {
        throw 'The backup has an invalid original-value record. No change was made.'
    }
    if ($null -eq $Snapshot.PSObject.Properties['Kind'] -or
        $null -eq $Snapshot.PSObject.Properties['Data']) {
        throw 'The backup is missing the original value kind or data. No change was made.'
    }
    if (-not $Snapshot.Exists) {
        if ($null -ne $Snapshot.Kind -or $null -ne $Snapshot.Data) {
            throw 'The absent-value backup contains unexpected kind or data. No change was made.'
        }
        return [pscustomobject]@{ Exists = $false; Kind = $null; Value = $null }
    }

    switch ([string] $Snapshot.Kind) {
        'String' {
            if ($Snapshot.Data -isnot [string]) { throw 'Invalid String backup data.' }
            $value = [string] $Snapshot.Data
        }
        'ExpandString' {
            if ($Snapshot.Data -isnot [string]) { throw 'Invalid ExpandString backup data.' }
            $value = [string] $Snapshot.Data
        }
        'MultiString' {
            if ($null -eq $Snapshot.Data -or $Snapshot.Data -is [string]) {
                throw 'Invalid MultiString backup data.'
            }
            foreach ($entry in $Snapshot.Data) {
                if ($entry -isnot [string]) { throw 'Invalid MultiString backup entry.' }
            }
            $value = [string[]] @($Snapshot.Data)
        }
        'DWord' {
            if ($Snapshot.Data -isnot [string]) { throw 'Invalid DWord backup data.' }
            $value = [int]::Parse($Snapshot.Data, [Globalization.CultureInfo]::InvariantCulture)
        }
        'QWord' {
            if ($Snapshot.Data -isnot [string]) { throw 'Invalid QWord backup data.' }
            $value = [long]::Parse($Snapshot.Data, [Globalization.CultureInfo]::InvariantCulture)
        }
        'Binary' {
            if ($Snapshot.Data -isnot [string]) { throw 'Invalid Binary backup data.' }
            $value = [Convert]::FromBase64String($Snapshot.Data)
        }
        'None' {
            if ($Snapshot.Data -isnot [string]) { throw 'Invalid None backup data.' }
            $value = [Convert]::FromBase64String($Snapshot.Data)
        }
        default { throw 'The backup contains an unsupported registry value kind. No change was made.' }
    }

    return [pscustomobject]@{
        Exists = $true
        Kind = [Microsoft.Win32.RegistryValueKind] ([string] $Snapshot.Kind)
        Value = $value
    }
}

function Test-ExplorerUndoDisabled {
    param([object] $Snapshot)
    return $Snapshot.Exists -and $Snapshot.Kind -eq 'DWord' -and $Snapshot.Data -eq '0'
}

function Test-ExplorerUndoSnapshotEqual {
    param([object] $First, [object] $Second)
    return (ConvertTo-Json -InputObject $First -Depth 5 -Compress) -ceq
        (ConvertTo-Json -InputObject $Second -Depth 5 -Compress)
}

function Read-ExplorerUndoBackup {
    param([IO.FileStream] $Stream)

    if ($Stream.Length -gt 16777216) {
        throw 'The backup file is unexpectedly large. No change was made.'
    }
    $reader = New-Object IO.StreamReader($Stream, [Text.Encoding]::UTF8, $true, 4096, $true)
    try {
        $Stream.Position = 0
        $backup = ConvertFrom-Json -InputObject $reader.ReadToEnd()
    }
    finally { $reader.Dispose() }

    foreach ($name in @('SchemaVersion', 'RegistryHive', 'RegistryPath', 'RegistryUserSid', 'ValueName', 'Original')) {
        if ($null -eq $backup -or $null -eq $backup.PSObject.Properties[$name]) {
            throw 'The existing file is not a ClipZero Explorer undo backup. No change was made.'
        }
    }
    if ($backup.SchemaVersion -ne 1 -or $backup.RegistryHive -cne 'CurrentUser' -or
        $backup.RegistryPath -cne $script:ExplorerUndoRegistryPath -or
        $backup.RegistryUserSid -cne $script:ExplorerUndoUserSid -or
        $backup.ValueName -cne $script:ExplorerUndoValueName) {
        throw 'The backup identifies an unexpected registry setting. No change was made.'
    }

    $null = ConvertFrom-ExplorerUndoSnapshot $backup.Original
    return $backup
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'This helper runs only on Windows.'
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try { $script:ExplorerUndoUserSid = $identity.User.Value }
finally { $identity.Dispose() }

$resolvedBackupPath = [IO.Path]::GetFullPath($BackupPath)
if ([IO.Path]::GetExtension($resolvedBackupPath) -ine '.json' -or
    [IO.Directory]::Exists($resolvedBackupPath)) {
    throw 'BackupPath must identify a JSON file, not a directory.'
}

$key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($script:ExplorerUndoRegistryPath, $false)
try { $snapshot = Get-ExplorerUndoSnapshot $key }
finally { if ($null -ne $key) { $key.Dispose() } }

if ($Action -eq 'Status') {
    $backupState = 'Absent'
    if ([IO.File]::Exists($resolvedBackupPath)) {
        $stream = [IO.File]::Open($resolvedBackupPath, [IO.FileMode]::Open,
            [IO.FileAccess]::Read, [IO.FileShare]::None)
        try {
            $null = Read-ExplorerUndoBackup $stream
            $backupState = 'Valid'
        }
        finally { $stream.Dispose() }
    }
    [pscustomobject]@{
        Action = 'Status'
        ExplorerUndo = $(if (Test-ExplorerUndoDisabled $snapshot) { 'DisabledBySetting' } else { 'DefaultOrCustom' })
        ValueExists = $snapshot.Exists
        ValueKind = $snapshot.Kind
        Backup = $backupState
        BackupPath = $resolvedBackupPath
    }
    return
}

$stream = $null
$key = $null
$restoreSucceeded = $false
try {
    if ([IO.File]::Exists($resolvedBackupPath)) {
        # An exclusive handle prevents two copies of this helper from applying one backup.
        $stream = [IO.File]::Open($resolvedBackupPath, [IO.FileMode]::Open,
            [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $backup = Read-ExplorerUndoBackup $stream
        if (-not (Test-ExplorerUndoDisabled $snapshot)) {
            throw 'MaxUndoItems changed after the backup, or Disable did not complete. Existing backup retained; no setting was overwritten.'
        }
        if ($Action -eq 'Disable') {
            Write-Output 'Explorer undo is already disabled by DWORD 0. Existing original-value backup retained.'
            return
        }
    }
    elseif ($Action -eq 'Restore') {
        throw 'No ClipZero backup exists. The original setting is unknown; no change was made.'
    }
    elseif (Test-ExplorerUndoDisabled $snapshot) {
        Write-Output 'MaxUndoItems is already DWORD 0. No change or replacement backup was made.'
        return
    }

    $target = 'HKCU\' + $script:ExplorerUndoRegistryPath + '\' + $script:ExplorerUndoValueName
    if ($Action -eq 'Disable') {
        if (-not $PSCmdlet.ShouldProcess($target, "Back up the original value to '$resolvedBackupPath' and set DWORD 0")) {
            return
        }
        $backupDirectory = [IO.Path]::GetDirectoryName($resolvedBackupPath)
        $null = [IO.Directory]::CreateDirectory($backupDirectory)
        # CreateNew fails if any existing file appears, rather than overwriting a backup.
        $stream = [IO.File]::Open($resolvedBackupPath, [IO.FileMode]::CreateNew,
            [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $backup = [pscustomobject]@{
            SchemaVersion = 1
            RegistryHive = 'CurrentUser'
            RegistryPath = $script:ExplorerUndoRegistryPath
            RegistryUserSid = $script:ExplorerUndoUserSid
            ValueName = $script:ExplorerUndoValueName
            CreatedUtc = [DateTime]::UtcNow.ToString('o')
            Original = $snapshot
        }
        $json = ConvertTo-Json -InputObject $backup -Depth 6
        $bytes = (New-Object Text.UTF8Encoding($false)).GetBytes($json)
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)

        $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($script:ExplorerUndoRegistryPath)
        $current = Get-ExplorerUndoSnapshot $key
        if (-not (Test-ExplorerUndoSnapshotEqual $snapshot $current)) {
            throw 'MaxUndoItems changed while the backup was being saved. No registry setting was written; backup retained.'
        }
        $key.SetValue($script:ExplorerUndoValueName, [int] 0, [Microsoft.Win32.RegistryValueKind]::DWord)
        $key.Flush()
        if (-not (Test-ExplorerUndoDisabled (Get-ExplorerUndoSnapshot $key))) {
            throw 'The setting could not be verified. Original-value backup retained.'
        }
        Write-Output 'Saved the original value and set MaxUndoItems to DWORD 0. Sign out and back in when convenient; Explorer was not restarted.'
    }
    else {
        $original = ConvertFrom-ExplorerUndoSnapshot $backup.Original
        if (-not $PSCmdlet.ShouldProcess($target, "Restore the exact original value and remove its backup '$resolvedBackupPath'")) {
            return
        }
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($script:ExplorerUndoRegistryPath, $true)
        if ($null -eq $key -or -not (Test-ExplorerUndoDisabled (Get-ExplorerUndoSnapshot $key))) {
            throw 'MaxUndoItems changed before Restore. Original-value backup retained; no setting was overwritten.'
        }
        if ($original.Exists) {
            $key.SetValue($script:ExplorerUndoValueName, $original.Value, $original.Kind)
        }
        else {
            $key.DeleteValue($script:ExplorerUndoValueName, $false)
        }
        $key.Flush()
        if (-not (Test-ExplorerUndoSnapshotEqual $backup.Original (Get-ExplorerUndoSnapshot $key))) {
            throw 'The restored value could not be verified. Backup retained.'
        }
        $restoreSucceeded = $true
    }
}
finally {
    if ($null -ne $key) { $key.Dispose() }
    if ($null -ne $stream) { $stream.Dispose() }
}

if ($restoreSucceeded) {
    [IO.File]::Delete($resolvedBackupPath)
    Write-Output 'Restored the original value and removed its backup. Sign out and back in when convenient; Explorer was not restarted.'
}
