using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Gplx.WpfApp.Sync;

public sealed class FileSyncSource : ISyncSource
{
    private readonly string _directoryPath;

    public string Name => $"ThÆ° má»¥c: {_directoryPath}";

    public FileSyncSource(string directoryPath)
    {
        _directoryPath = directoryPath;
    }

    public Task<IReadOnlyList<IDataRecord>> GetAllAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_directoryPath))
            throw new DirectoryNotFoundException($"KhÃ´ng tÃ¬m tháº¥y thÆ° má»¥c nguá»“n: {_directoryPath}");

        var records = Directory.GetFiles(_directoryPath)
            .Select(f => (IDataRecord)new FileDataRecord(f))
            .ToList();

        return Task.FromResult<IReadOnlyList<IDataRecord>>(records);
    }
}
