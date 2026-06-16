namespace Gplx.WpfApp.Sync;

public interface ISyncSource
{
    string Name { get; }
    Task<IReadOnlyList<IDataRecord>> GetAllAsync(CancellationToken ct = default);
}
