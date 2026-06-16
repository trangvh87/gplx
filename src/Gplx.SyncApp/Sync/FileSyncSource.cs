namespace Gplx.SyncApp.Sync;

public sealed class FileSyncSource : ISyncSource
{
    private readonly string _directoryPath;

    public string Name => $"Thư mục: {_directoryPath}";

    public FileSyncSource(string directoryPath)
    {
        _directoryPath = directoryPath;
    }

    public Task<IReadOnlyList<IDataRecord>> GetAllAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_directoryPath))
            throw new DirectoryNotFoundException($"Không tìm thấy thư mục nguồn: {_directoryPath}");

        var records = Directory.GetFiles(_directoryPath)
            .Select(f => (IDataRecord)new FileDataRecord(f))
            .ToList();

        return Task.FromResult<IReadOnlyList<IDataRecord>>(records);
    }
}
