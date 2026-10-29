namespace ClipZero.Core;

/// <summary>Separates the optional Explorer setting from UI and makes all mutations testable.</summary>
public interface IExplorerUndoStore
{
    string UserSid { get; }
    IExplorerUndoSession OpenSession();
}

public interface IExplorerUndoSession : IDisposable
{
    ExplorerUndoValue ReadValue();
    bool BackupExists { get; }
    string ReadBackup();
    /// <summary>Creates a new backup without replacing an existing file, and flushes it durably.</summary>
    void CreateBackup(string json);
    void WriteValue(ExplorerUndoValue value);
    void DeleteBackup();
}
