using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Gplx.WpfApp.Sync;

public interface ISyncSource
{
    string Name { get; }
    Task<IReadOnlyList<IDataRecord>> GetAllAsync(CancellationToken ct = default);
}
