using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Gplx.DbSync.Models;

namespace Gplx.DbSync.Sync;

public sealed class TableSyncEngine
{
    private readonly SyncConfig _config;
    private readonly IProgress<string> _progress;

    public TableSyncEngine(SyncConfig config, IProgress<string> progress)
    {
        _config = config;
        _progress = progress;
    }

    public async Task<List<SyncResult>> RunAllAsync()
    {
        var results = new List<SyncResult>();
        foreach (var table in _config.Tables)
        {
            var result = await RunSingleTableAsync(table);
            results.Add(result);
            if (!result.Success)
            {
                _progress.Report($"LỖI nghiêm trọng: {result.ErrorMessage}. Dừng đồng bộ.");
                break;
            }
        }
        return results;
    }

    private async Task<SyncResult> RunSingleTableAsync(SyncTableConfig table)
    {
        var sw = Stopwatch.StartNew();
        var tableLabel = $"[{table.DestSchema}].[{table.DestTable}]";
        _progress.Report($"--- Đồng bộ {tableLabel} ---");

        try
        {
            var srcColumns = await SchemaDiscovery.GetColumnsAsync(
                _config.SourceConnectionString, table.SourceSchema, table.SourceTable);
            var dstColumns = await SchemaDiscovery.GetColumnsAsync(
                _config.DestConnectionString, table.DestSchema, table.DestTable);

            if (srcColumns.Count == 0)
            {
                _progress.Report($"  Không tìm thấy cột ở nguồn cho {tableLabel}");
                return new SyncResult { TableName = tableLabel, Success = false, ErrorMessage = "No source columns" };
            }
            if (dstColumns.Count == 0)
            {
                _progress.Report($"  Không tìm thấy cột ở đích cho {tableLabel}");
                return new SyncResult { TableName = tableLabel, Success = false, ErrorMessage = "No dest columns" };
            }

            var dstNames = new HashSet<string>(dstColumns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
            var columns = srcColumns.Where(c => dstNames.Contains(c.Name)).ToList();
            var skipped = srcColumns.Where(c => !dstNames.Contains(c.Name)).ToList();
            if (skipped.Count > 0)
                _progress.Report($"  Bỏ qua cột: {string.Join(", ", skipped.Select(c => c.Name))}");

            if (columns.Count == 0)
            {
                _progress.Report($"  Không có cột chung giữa nguồn và đích cho {tableLabel}");
                return new SyncResult { TableName = tableLabel, Success = false, ErrorMessage = "No common columns" };
            }

            var identityCol = columns.FirstOrDefault(c => c.IsIdentity);
            var columnList = string.Join(", ", columns.Select(c => $"[{c.Name}]"));
            var sourceTableFull = $"[{table.SourceSchema}].[{table.SourceTable}]";
            var destTableFull = $"[{table.DestSchema}].[{table.DestTable}]";
            var tempTable = $"##Gplx_Sync_{table.DestTable}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";

            var keyCondition = BuildKeyCondition(table.KeyColumns, columns, "S", "T");

            using var srcConn = new SqlConnection(_config.SourceConnectionString);
            await srcConn.OpenAsync();

            using var dstConn = new SqlConnection(_config.DestConnectionString);
            await dstConn.OpenAsync();

            // 1. Create temp table on destination
            var createSql = BuildCreateTempSql(tempTable, columns, identityCol);
            using (var createCmd = new SqlCommand(createSql, dstConn))
            {
                createCmd.CommandTimeout = _config.CommandTimeoutSeconds;
                await createCmd.ExecuteNonQueryAsync();
            }
            _progress.Report($"  Đã tạo bảng tạm {tempTable}");

            // 2. Bulk copy source data into temp table
            using (var bulkCopy = new SqlBulkCopy(dstConn)
            {
                DestinationTableName = tempTable,
                BatchSize = _config.BatchSize,
                BulkCopyTimeout = _config.CommandTimeoutSeconds,
                EnableStreaming = true
            })
            {
                using var srcCmd = new SqlCommand(
                    $"SELECT {columnList} FROM {sourceTableFull}", srcConn);
                srcCmd.CommandTimeout = _config.CommandTimeoutSeconds;

                using var reader = await srcCmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
                await bulkCopy.WriteToServerAsync(reader);
            }

            // Get source count
            long sourceCount;
            using (var countCmd = new SqlCommand($"SELECT COUNT(*) FROM {tempTable}", dstConn))
            {
                countCmd.CommandTimeout = _config.CommandTimeoutSeconds;
                sourceCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync() ?? 0);
            }
            _progress.Report($"  Đã sao chép {sourceCount} bản ghi vào bảng tạm");

            // 3. Insert into real table WHERE NOT EXISTS
            var identityOn = "";
            var identityOff = "";
            if (identityCol != null)
            {
                identityOn = $"SET IDENTITY_INSERT {destTableFull} ON;";
                identityOff = $";SET IDENTITY_INSERT {destTableFull} OFF;";
            }

            var insertSql = $"""
                {identityOn}
                INSERT INTO {destTableFull} ({columnList})
                SELECT S.* FROM {tempTable} S
                WHERE NOT EXISTS (
                    SELECT 1 FROM {destTableFull} T
                    WHERE {keyCondition}
                );
                {identityOff}
                """;

            long insertedCount;
            using (var insertCmd = new SqlCommand(insertSql, dstConn))
            {
                insertCmd.CommandTimeout = _config.CommandTimeoutSeconds;
                insertedCount = await insertCmd.ExecuteNonQueryAsync();
            }
            _progress.Report($"  Đã chèn {insertedCount} bản ghi mới");

            // 4. Drop temp table
            using (var dropCmd = new SqlCommand($"DROP TABLE {tempTable}", dstConn))
            {
                dropCmd.CommandTimeout = _config.CommandTimeoutSeconds;
                await dropCmd.ExecuteNonQueryAsync();
            }

            sw.Stop();
            _progress.Report($"  OK ({sw.Elapsed.TotalSeconds:F1}s)");

            return new SyncResult
            {
                TableName = tableLabel,
                SourceCount = sourceCount,
                InsertedCount = insertedCount,
                SkippedCount = sourceCount - insertedCount,
                Duration = sw.Elapsed,
                Success = true
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _progress.Report($"  LỖI: {ex.Message}");
            return new SyncResult
            {
                TableName = tableLabel,
                Duration = sw.Elapsed,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private static string BuildCreateTempSql(
        string tempTable, List<ColumnInfo> columns, ColumnInfo? identityCol)
    {
        var cols = columns.Select(c =>
        {
            var type = MapToSqlType(c);
            var nullable = c.IsNullable ? "NULL" : "NOT NULL";
            return $"[{c.Name}] {type} {nullable}";
        });

        return $"CREATE TABLE {tempTable} (\n  {string.Join(",\n  ", cols)}\n);";
    }

    private static string MapToSqlType(ColumnInfo col)
    {
        var type = col.DataType.ToLower();
        return type switch
        {
            "nvarchar" or "varchar" or "nchar" or "char" or "varbinary" or "binary"
                => $"{type}({(col.MaxLength == -1 ? "MAX" : col.MaxLength?.ToString() ?? "MAX")})",
            "nvarchar(max)" or "varchar(max)" or "varbinary(max)" => type,
            "decimal" or "numeric"
                => $"{type}({col.Precision ?? 18},{col.Scale ?? 0})",
            "datetime2" or "datetimeoffset" or "time"
                => $"{type}({col.Scale ?? 7})",
            _ => type
        };
    }

    private static string BuildKeyCondition(
        List<string> keyCols, List<ColumnInfo> columns, string srcAlias, string dstAlias)
    {
        if (keyCols.Count > 0)
        {
            var validKeys = keyCols
                .Where(k => columns.Any(c =>
                    c.Name.Equals(k, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (validKeys.Count > 0)
                return string.Join(" AND ",
                    validKeys.Select(k =>
                        $"{srcAlias}.[{k}] = {dstAlias}.[{k}]"));
        }

        var pk = columns.Where(c =>
            c.Name.EndsWith("ID", StringComparison.OrdinalIgnoreCase) ||
            c.Name.StartsWith("Ma", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Name).ToList();

        if (pk.Count > 0)
            return string.Join(" AND ",
                pk.Select(k =>
                    $"{srcAlias}.[{k}] = {dstAlias}.[{k}]"));

        return string.Join(" AND ",
            columns.Select(c =>
                $"{srcAlias}.[{c.Name}] = {dstAlias}.[{c.Name}]"));
    }
}
