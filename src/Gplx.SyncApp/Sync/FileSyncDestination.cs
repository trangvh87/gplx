namespace Gplx.SyncApp.Sync;

public sealed class FileSyncDestination : ISyncDestination
{
    private readonly string _directoryPath;

    public string Name => $"Thư mục: {_directoryPath}";

    public FileSyncDestination(string directoryPath)
    {
        _directoryPath = directoryPath;
    }

    public Task<bool> ExistsAsync(string id, CancellationToken ct = default)
    {
        var path = Path.Combine(_directoryPath, id);
        return Task.FromResult(File.Exists(path));
    }

    public Task WriteAsync(IDataRecord record, CancellationToken ct = default)
    {
        var path = Path.Combine(_directoryPath, record.Id);
        File.WriteAllText(path, record.Content);
        File.SetLastWriteTime(path, record.LastModified);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var path = Path.Combine(_directoryPath, id);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }
}
