using System.Collections.Generic;

namespace Gplx.DbSync.Models;

public sealed class SyncConfig
{
    public string SourceConnectionString { get; set; } = "";
    public string DestConnectionString { get; set; } = "";
    public int BatchSize { get; set; } = 50000;
    public int CommandTimeoutSeconds { get; set; } = 600;
    public List<SyncTableConfig> Tables { get; set; } = new List<SyncTableConfig>();
}
