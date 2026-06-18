using System.Collections.Generic;

namespace Gplx.DbSync.Models;

public sealed class SyncTableConfig
{
    public string SourceSchema { get; set; } = "dbo";
    public string SourceTable { get; set; } = "";
    public string DestSchema { get; set; } = "dbo";
    public string DestTable { get; set; } = "";
    public List<string> KeyColumns { get; set; } = new List<string>();
}
