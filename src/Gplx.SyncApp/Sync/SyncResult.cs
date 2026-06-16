namespace Gplx.SyncApp.Sync;

public sealed class SyncResult
{
    public int Added { get; init; }
    public int Updated { get; init; }
    public int Deleted { get; init; }
    public int Skipped { get; init; }
    public int Errors { get; init; }
    public IReadOnlyList<string> ErrorMessages { get; init; } = [];
}
