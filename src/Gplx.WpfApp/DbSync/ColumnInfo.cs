namespace Gplx.WpfApp.DbSync;

public sealed class ColumnInfo
{
    public string Name { get; init; } = "";
    public string DataType { get; init; } = "";
    public int? MaxLength { get; init; }
    public int? Precision { get; init; }
    public int? Scale { get; init; }
    public bool IsIdentity { get; init; }
    public bool IsNullable { get; init; }
}
