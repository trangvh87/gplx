namespace Gplx.WpfApp.Sync;

public sealed class FileDataRecord : IDataRecord
{
    public string Id { get; }
    public string Content { get; }
    public DateTime LastModified { get; }
    public string SourcePath { get; }

    public FileDataRecord(string filePath)
    {
        var info = new FileInfo(filePath);
        Id = info.Name;
        Content = File.ReadAllText(filePath);
        LastModified = info.LastWriteTime;
        SourcePath = filePath;
    }
}
