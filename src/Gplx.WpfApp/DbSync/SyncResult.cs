namespace Gplx.WpfApp.DbSync;

public sealed class SyncResult
{
    public string TableName { get; init; } = "";
    public long SourceCount { get; init; }
    public long InsertedCount { get; init; }
    public long SkippedCount { get; init; }
    public TimeSpan Duration { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}
