using System.Globalization;
using System.Security.Principal;
using System.Text;
using ClipZero.Core;
using Microsoft.Win32;

namespace ClipZero.Interop;

/// <summary>Current-user registry access; never starts a process or restarts Explorer.</summary>
internal sealed class WindowsExplorerUndoStore : IExplorerUndoStore
{
    private readonly string _backupPath;

    public WindowsExplorerUndoStore()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        UserSid = identity.User?.Value ?? throw new InvalidOperationException("无法识别当前 Windows 账户。");
        _backupPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClipZero", "explorer-undo-backup.json");
    }

    public string UserSid { get; }
    public IExplorerUndoSession OpenSession() => new Session(_backupPath);

    private sealed class Session : IExplorerUndoSession
    {
        private const int MaximumBackupBytes = 16 * 1024 * 1024;
        private readonly string _backupPath;
        private FileStream? _backup;

        public Session(string backupPath)
        {
            _backupPath = backupPath;
            if (File.Exists(_backupPath))
                _backup = new FileStream(_backupPath, FileMode.Open, FileAccess.Read, FileShare.None);
        }

        public bool BackupExists => _backup is not null;

        public ExplorerUndoValue ReadValue()
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(ExplorerUndoService.RegistryPath, writable: false);
            if (key is null || !key.GetValueNames().Contains(ExplorerUndoService.ValueName, StringComparer.OrdinalIgnoreCase))
                return ExplorerUndoValue.Absent;
            RegistryValueKind kind = key.GetValueKind(ExplorerUndoService.ValueName);
            object value = key.GetValue(ExplorerUndoService.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
                ?? throw new InvalidDataException("无法读取完整的原注册表设置。");
            object data = kind switch
            {
                RegistryValueKind.String or RegistryValueKind.ExpandString => (string)value,
                RegistryValueKind.MultiString => (string[])value,
                RegistryValueKind.DWord => ((int)value).ToString(CultureInfo.InvariantCulture),
                RegistryValueKind.QWord => ((long)value).ToString(CultureInfo.InvariantCulture),
                RegistryValueKind.Binary or RegistryValueKind.None => Convert.ToBase64String((byte[])value),
                _ => throw new InvalidDataException("原注册表设置的数据类型不受支持，未作修改。")
            };
            return ExplorerUndoValue.Create(kind.ToString(), data);
        }

        public string ReadBackup()
        {
            FileStream stream = _backup ?? throw new FileNotFoundException("没有找到原设置备份。");
            if (stream.Length > MaximumBackupBytes)
                throw new InvalidDataException("原设置备份文件过大，未作修改。");
            stream.Position = 0;
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096, leaveOpen: true);
            return reader.ReadToEnd();
        }

        public void CreateBackup(string json)
        {
            if (_backup is not null) throw new IOException("原设置备份已存在，不能覆盖。");
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            if (bytes.Length > MaximumBackupBytes)
                throw new InvalidDataException("原设置过大，无法生成可恢复的备份。没有修改设置。");
            Directory.CreateDirectory(Path.GetDirectoryName(_backupPath)!);
            _backup = new FileStream(_backupPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            _backup.Write(bytes);
            _backup.Flush(flushToDisk: true);
        }

        public void WriteValue(ExplorerUndoValue value)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(ExplorerUndoService.RegistryPath);
            if (!value.Exists)
            {
                key.DeleteValue(ExplorerUndoService.ValueName, throwOnMissingValue: false);
            }
            else
            {
                RegistryValueKind kind = Enum.Parse<RegistryValueKind>(value.Kind!);
                object data = kind switch
                {
                    RegistryValueKind.String or RegistryValueKind.ExpandString => value.Data.GetString()!,
                    RegistryValueKind.MultiString => value.Data.EnumerateArray().Select(entry => entry.GetString()!).ToArray(),
                    RegistryValueKind.DWord => int.Parse(value.Data.GetString()!, CultureInfo.InvariantCulture),
                    RegistryValueKind.QWord => long.Parse(value.Data.GetString()!, CultureInfo.InvariantCulture),
                    RegistryValueKind.Binary or RegistryValueKind.None => Convert.FromBase64String(value.Data.GetString()!),
                    _ => throw new InvalidDataException("原注册表设置的数据类型不受支持。")
                };
                key.SetValue(ExplorerUndoService.ValueName, data, kind);
            }
            key.Flush();
        }

        public void DeleteBackup()
        {
            _backup?.Dispose();
            _backup = null;
            File.Delete(_backupPath);
        }

        public void Dispose() => _backup?.Dispose();
    }
}
