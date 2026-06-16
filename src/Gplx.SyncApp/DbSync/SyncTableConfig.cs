using System.Text.Json.Serialization;

namespace Gplx.SyncApp.DbSync;

public sealed class SyncTableConfig
{
    [JsonPropertyName("s")]
    public string SourceSchema { get; set; } = "dbo";

    [JsonPropertyName("t")]
    public string SourceTable { get; set; } = "";

    [JsonPropertyName("d")]
    public string DestSchema { get; set; } = "dbo";

    [JsonPropertyName("T")]
    public string DestTable { get; set; } = "";

    [JsonPropertyName("k")]
    public List<string> KeyColumns { get; set; } = [];
}
