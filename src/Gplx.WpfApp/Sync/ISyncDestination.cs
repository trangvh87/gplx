namespace Gplx.WpfApp.Sync;

public interface ISyncDestination
{
    string Name { get; }
    Task<bool> ExistsAsync(string id, CancellationToken ct = default);
    Task WriteAsync(IDataRecord record, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
}
