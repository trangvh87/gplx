namespace Gplx.SyncApp.Sync;

public delegate void SyncProgressHandler(string message);

public sealed class SyncEngine
{
    private readonly ISyncSource _source;
    private readonly ISyncDestination _destination;

    public event SyncProgressHandler? OnProgress;

    public SyncEngine(ISyncSource source, ISyncDestination destination)
    {
        _source = source;
        _destination = destination;
    }

    public async Task<SyncResult> RunAsync(CancellationToken ct = default)
    {
        Report($"=== Bắt đầu đồng bộ: {_source.Name} -> {_destination.Name} ===");

        IEnumerable<IDataRecord> sourceRecords;
        try
        {
            sourceRecords = await _source.GetAllAsync(ct);
        }
        catch (Exception ex)
        {
            Report($"Lỗi đọc nguồn: {ex.Message}");
            return new SyncResult { Errors = 1, ErrorMessages = [ex.Message] };
        }

        var added = 0;
        var updated = 0;
        var deleted = 0;
        var skipped = 0;
        var errors = 0;
        var errorMessages = new List<string>();

        foreach (var record in sourceRecords)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var exists = await _destination.ExistsAsync(record.Id, ct);
                if (exists)
                {
                    Report($"  [CẬP NHẬT] {record.Id}");
                    await _destination.WriteAsync(record, ct);
                    updated++;
                }
                else
                {
                    Report($"  [THÊM] {record.Id}");
                    await _destination.WriteAsync(record, ct);
                    added++;
                }
            }
            catch (Exception ex)
            {
                Report($"  [LỖI] {record.Id}: {ex.Message}");
                errors++;
                errorMessages.Add($"[{record.Id}] {ex.Message}");
            }
        }

        Report($"=== Kết thúc: +{added} ~{updated} -{deleted} x{skipped} !{errors} ===");
        return new SyncResult
        {
            Added = added,
            Updated = updated,
            Deleted = deleted,
            Skipped = skipped,
            Errors = errors,
            ErrorMessages = errorMessages
        };
    }

    private void Report(string message)
    {
        OnProgress?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
    }
}
