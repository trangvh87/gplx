using System.Data;
using Microsoft.Data.SqlClient;

namespace Gplx.SyncApp.DbSync;

public delegate void DbSyncProgressHandler(string message);

public sealed class TableSyncEngine
{
    private readonly string _sourceConn;
    private readonly string _destConn;
    private readonly List<SyncTableConfig> _tables;
    private readonly int _batchSize;
    private readonly int _commandTimeout;

    public event DbSyncProgressHandler? OnProgress;

    public TableSyncEngine(
        string sourceConn, string destConn,
        List<SyncTableConfig> tables,
        int batchSize = 50000, int commandTimeout = 600)
    {
        _sourceConn = sourceConn;
        _destConn = destConn;
        _tables = tables;
        _batchSize = batchSize;
        _commandTimeout = commandTimeout;
    }

    public async Task<List<SyncResult>> RunAllAsync()
    {
        var results = new List<SyncResult>();
        foreach (var table in _tables)
        {
            var result = await RunSingleTableAsync(table);
            results.Add(result);
            if (!result.Success)
            {
                Report($"LỖI: {result.ErrorMessage}. Dừng đồng bộ.");
                break;
            }
        }
        return results;
    }

    private async Task<SyncResult> RunSingleTableAsync(SyncTableConfig table)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var label = $"[{table.DestSchema}].[{table.DestTable}]";
        Report($"--- {label} ---");

        try
        {
            var srcColumns = await SchemaDiscovery.GetColumnsAsync(
                _sourceConn, table.SourceSchema, table.SourceTable);
            var dstColumns = await SchemaDiscovery.GetColumnsAsync(
                _destConn, table.DestSchema, table.DestTable);

            if (srcColumns.Count == 0)
            {
                Report($"  Không tìm thấy cột ở nguồn");
                return new SyncResult { TableName = label, Success = false, ErrorMessage = "No source columns" };
            }
            if (dstColumns.Count == 0)
            {
                Report($"  Không tìm thấy cột ở đích");
                return new SyncResult { TableName = label, Success = false, ErrorMessage = "No dest columns" };
            }

            var dstNames = dstColumns.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var columns = srcColumns.Where(c => dstNames.Contains(c.Name)).ToList();
            var skipped = srcColumns.Where(c => !dstNames.Contains(c.Name)).ToList();

            if (skipped.Count > 0)
                Report($"  Bỏ qua cột: {string.Join(", ", skipped.Select(c => c.Name))}");

            if (columns.Count == 0)
            {
                Report($"  Không có cột chung giữa nguồn và đích");
                return new SyncResult { TableName = label, Success = false, ErrorMessage = "No common columns" };
            }

            var identityCol = columns.FirstOrDefault(c => c.IsIdentity);
            var colList = string.Join(", ", columns.Select(c => $"[{c.Name}]"));
            var srcFull = $"[{table.SourceSchema}].[{table.SourceTable}]";
            var dstFull = $"[{table.DestSchema}].[{table.DestTable}]";
            var tempTable = $"##Gplx_Sync_{table.DestTable}_{Guid.NewGuid().ToString("N")[..8]}";

            var keyCond = BuildKeyCondition(table.KeyColumns, columns, "S", "T");

            await using var srcConn = new SqlConnection(_sourceConn);
            await srcConn.OpenAsync();

            await using var dstConn = new SqlConnection(_destConn);
            await dstConn.OpenAsync();

            var createSql = BuildCreateTempSql(tempTable, columns, identityCol);
            await using (var cmd = new SqlCommand(createSql, dstConn))
            {
                cmd.CommandTimeout = _commandTimeout;
                await cmd.ExecuteNonQueryAsync();
            }
            Report($"  Bảng tạm {tempTable}");

            using (var bulk = new SqlBulkCopy(dstConn)
            {
                DestinationTableName = tempTable,
                BatchSize = _batchSize,
                BulkCopyTimeout = _commandTimeout,
                EnableStreaming = true
            })
            {
                await using var cmd = new SqlCommand(
                    $"SELECT {colList} FROM {srcFull}", srcConn);
                cmd.CommandTimeout = _commandTimeout;

                await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
                await bulk.WriteToServerAsync(reader);
            }

            long sourceCount;
            await using (var cmd = new SqlCommand($"SELECT COUNT(*) FROM {tempTable}", dstConn))
            {
                cmd.CommandTimeout = _commandTimeout;
                sourceCount = Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0);
            }
            Report($"  Copy {sourceCount} bản ghi");

            var identityOn = "";
            var identityOff = "";
            if (identityCol != null)
            {
                identityOn = $"SET IDENTITY_INSERT {dstFull} ON;";
                identityOff = $";SET IDENTITY_INSERT {dstFull} OFF;";
            }

            var insertSql = $"""
                {identityOn}
                INSERT INTO {dstFull} ({colList})
                SELECT S.* FROM {tempTable} S
                WHERE NOT EXISTS (
                    SELECT 1 FROM {dstFull} T
                    WHERE {keyCond}
                );
                {identityOff}
                """;

            long inserted;
            await using (var cmd = new SqlCommand(insertSql, dstConn))
            {
                cmd.CommandTimeout = _commandTimeout;
                inserted = await cmd.ExecuteNonQueryAsync();
            }
            Report($"  +{inserted} bản ghi mới");

            await using (var cmd = new SqlCommand($"DROP TABLE {tempTable}", dstConn))
            {
                cmd.CommandTimeout = _commandTimeout;
                await cmd.ExecuteNonQueryAsync();
            }

            sw.Stop();
            Report($"  OK ({sw.Elapsed.TotalSeconds:F1}s)");

            return new SyncResult
            {
                TableName = label, SourceCount = sourceCount,
                InsertedCount = inserted, SkippedCount = sourceCount - inserted,
                Duration = sw.Elapsed, Success = true
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            Report($"  LỖI: {ex.Message}");
            return new SyncResult { TableName = label, Duration = sw.Elapsed, Success = false, ErrorMessage = ex.Message };
        }
    }

    private void Report(string msg) => OnProgress?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");

    private static string BuildCreateTempSql(string tempTable, List<ColumnInfo> columns, ColumnInfo? identityCol)
    {
        var cols = columns.Select(c =>
        {
            var type = MapToSqlType(c);
            return $"[{c.Name}] {type} {(c.IsNullable ? "NULL" : "NOT NULL")}";
        });
        return $"CREATE TABLE {tempTable} (\n  {string.Join(",\n  ", cols)}\n);";
    }

    private static string MapToSqlType(ColumnInfo col)
    {
        var t = col.DataType.ToLower();
        return t switch
        {
            "nvarchar" or "varchar" or "nchar" or "char" or "varbinary" or "binary"
                => $"{t}({(col.MaxLength == -1 ? "MAX" : col.MaxLength?.ToString() ?? "MAX")})",
            "decimal" or "numeric"
                => $"{t}({col.Precision ?? 18},{col.Scale ?? 0})",
            "datetime2" or "datetimeoffset" or "time"
                => $"{t}({col.Scale ?? 7})",
            _ => t
        };
    }

    private static string BuildKeyCondition(List<string> keyCols, List<ColumnInfo> columns, string sa, string ta)
    {
        if (keyCols.Count > 0)
        {
            var vk = keyCols.Where(k => columns.Any(c => c.Name.Equals(k, StringComparison.OrdinalIgnoreCase))).ToList();
            if (vk.Count > 0) return string.Join(" AND ", vk.Select(k => $"{sa}.[{k}] = {ta}.[{k}]"));
        }
        var pk = columns.Where(c => c.Name.EndsWith("ID", StringComparison.OrdinalIgnoreCase) || c.Name.StartsWith("Ma", StringComparison.OrdinalIgnoreCase)).Select(c => c.Name).ToList();
        if (pk.Count > 0) return string.Join(" AND ", pk.Select(k => $"{sa}.[{k}] = {ta}.[{k}]"));
        return string.Join(" AND ", columns.Select(c => $"{sa}.[{c.Name}] = {ta}.[{c.Name}]"));
    }
}
