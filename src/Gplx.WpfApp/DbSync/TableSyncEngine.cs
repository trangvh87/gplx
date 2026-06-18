using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Gplx.Core.DbSync;

namespace Gplx.WpfApp.DbSync;

public delegate void DbSyncProgressHandler(string message);

    public sealed class TableSyncEngine
    {
    private readonly string _sourceConn;
    private readonly string _destConn;
    private readonly List<SyncTableConfig> _tables;
    private readonly int _batchSize;
    private readonly int _commandTimeout;
    private readonly string? _newCsdtCode;
    private readonly string? _newSoCode;
    private readonly string? _courseCode;
    private readonly string? _newCourseName;
    private readonly string _runId = Guid.NewGuid().ToString("N");
    private readonly bool _allocateCsdt;
    private readonly string? _oldCsdt;
    private string? _allocatedCsdt; // set when allocation performed atomically

    public event DbSyncProgressHandler? OnProgress;

    public TableSyncEngine(
        string sourceConn, string destConn,
        List<SyncTableConfig> tables,
        int batchSize = 50000, int commandTimeout = 600,
        string? newCsdtCode = null,
        string? newSoCode = null,
        string? courseCode = null,
        string? newCourseName = null)
    {
        _sourceConn = sourceConn;
        _destConn = destConn;
        _tables = tables;
        _batchSize = batchSize;
        _commandTimeout = commandTimeout;
        _newCsdtCode = newCsdtCode;
        _newSoCode = newSoCode;
        _courseCode = courseCode;
        _newCourseName = newCourseName;
        _allocateCsdt = false;
        _oldCsdt = null;
    }

    public TableSyncEngine(
        string sourceConn, string destConn,
        List<SyncTableConfig> tables,
        int batchSize, int commandTimeout,
        string? newCsdtCode = null,
        string? newSoCode = null,
        string? courseCode = null,
        string? newCourseName = null,
        bool allocateCsdt = false,
        string? oldCsdt = null)
        : this(sourceConn, destConn, tables, batchSize, commandTimeout, newCsdtCode, newSoCode, courseCode, newCourseName)
    {
        _allocateCsdt = allocateCsdt;
        _oldCsdt = oldCsdt;
    }

    public async Task<List<SyncResult>> RunAllAsync()
    {
        var results = new List<SyncResult>();
        if (_allocateCsdt && !string.IsNullOrEmpty(_oldCsdt))
        {
            try
            {
                await AllocateCsdtAtomicAsync();
                Report($"  Đã cấp mã CSĐT mới: {_allocatedCsdt}");
            }
            catch (Exception ex)
            {
                Report($"LỖI: Không thể cấp mã CSĐT: {ex.Message}");
                return results;
            }
        }
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

    private async Task AllocateCsdtAtomicAsync()
    {
        if (string.IsNullOrEmpty(_oldCsdt)) throw new InvalidOperationException("Old CSĐT required");
        var province = _oldCsdt!.Substring(0, 2);
        using var conn = new SqlConnection(_destConn);
        await conn.OpenAsync();
            var sql = $"""
            DECLARE @res int;
            EXEC @res = sp_getapplock @Resource = N'GplxAllocateCsdt_{province}', @LockMode='Exclusive', @LockTimeout=60000, @LockOwner='Session';
            IF @res < 0 BEGIN RAISERROR('Không lấy được khoá cấp mã',16,1); RETURN; END;
            DECLARE @max int = (SELECT ISNULL(MAX(TRY_CONVERT(int, SUBSTRING(MaCSDT,3,3))),0) FROM KhoaHoc WHERE LEFT(ISNULL(MaCSDT,''),2) = @p AND LEN(ISNULL(MaCSDT,'')) = 5);
            DECLARE @next int = @max + 1;
            IF @next > 999 BEGIN EXEC sp_releaseapplock @Resource = N'GplxAllocateCsdt_{province}'; RAISERROR('Vượt quá giới hạn cấp mã',16,1); RETURN; END;
            SELECT @next AS NextSeq;
            EXEC sp_releaseapplock @Resource = N'GplxAllocateCsdt_{province}';
        """;

        try
        {
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@p", province);
            cmd.CommandTimeout = 60;
            var obj = await cmd.ExecuteScalarAsync();
            if (obj == null || obj == DBNull.Value) throw new InvalidOperationException("Không nhận được sequence");
            var next = Convert.ToInt32(obj);
            _allocatedCsdt = province + next.ToString("D3");
        }
        catch (Exception ex)
        {
            // If applock is not available or fails (permissions, unsupported, etc.), fallback to non-atomic allocation
            Report($"  Cảnh báo: không thể lấy applock, thực hiện cấp mã không nguyên tử: {ex.Message}");
            using var cmd2 = new SqlCommand(
                "SELECT ISNULL(MAX(TRY_CONVERT(int, SUBSTRING(MaCSDT,3,3))),0) FROM KhoaHoc WHERE LEFT(ISNULL(MaCSDT,''),2) = @p AND LEN(ISNULL(MaCSDT,'')) = 5", conn);
            cmd2.Parameters.AddWithValue("@p", province);
            cmd2.CommandTimeout = 30;
            var r = await cmd2.ExecuteScalarAsync();
            int max = 0;
            if (r != null && r != DBNull.Value) { try { max = Convert.ToInt32(r); } catch { max = 0; } }
            var next = max + 1;
            if (next > 999) throw new InvalidOperationException("Vượt quá giới hạn cấp mã");
            _allocatedCsdt = province + next.ToString("D3");
        }
    }

    private async Task<SyncResult> RunSingleTableAsync(SyncTableConfig table)
    {
        var sw = Stopwatch.StartNew();
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

            var dstNames = new HashSet<string>(dstColumns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
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
            var tempTable = $"##Gplx_Sync_{table.DestTable}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";

            var keyCond = BuildKeyCondition(table.KeyColumns, columns, "S", "T");

            using var srcConn = new SqlConnection(_sourceConn);
            await srcConn.OpenAsync();

            using var dstConn = new SqlConnection(_destConn);
            await dstConn.OpenAsync();

            var cuCols = GetRequiredCuColumns(table.DestTable, columns);
            if (cuCols.Count > 0)
            {
                await EnsureCuColumnsAsync(dstConn, dstFull, cuCols);
                foreach (var cu in cuCols) dstNames.Add(cu);
            }

            var createSql = BuildCreateTempSql(tempTable, columns, identityCol);
            using (var cmd = new SqlCommand(createSql, dstConn))
            {
                cmd.CommandTimeout = _commandTimeout;
                await cmd.ExecuteNonQueryAsync();
            }
            Report($"  Bảng tạm {tempTable}");

            var selectSql = BuildSelectSql(srcFull, colList, table.DestTable, table.DestSchema);
            using (var bulk = new SqlBulkCopy(dstConn)
            {
                DestinationTableName = tempTable,
                BatchSize = _batchSize,
                BulkCopyTimeout = _commandTimeout,
                EnableStreaming = true
            })
            {
                using var cmd = new SqlCommand(selectSql, srcConn);
                cmd.CommandTimeout = _commandTimeout;
                if (!string.IsNullOrEmpty(_courseCode))
                    cmd.Parameters.AddWithValue("@CourseCode", _courseCode);

                using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
                await bulk.WriteToServerAsync(reader);
            }

            long sourceCount;
            using (var cmd = new SqlCommand($"SELECT COUNT(*) FROM {tempTable}", dstConn))
            {
                cmd.CommandTimeout = _commandTimeout;
                sourceCount = Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0);
            }
            Report($"  Copy {sourceCount} bản ghi");

            await ApplyTransformAsync(dstConn, tempTable, table.DestTable);
            Report($"  Transform mã");

            var allColList = colList;
            if (cuCols.Count > 0)
            {
                var cuList = string.Join(", ", cuCols.Select(c => $"[{c}]"));
                allColList = $"{colList}, {cuList}";
            }

            var identityOn = "";
            var identityOff = "";
            if (identityCol != null)
            {
                identityOn = $"SET IDENTITY_INSERT {dstFull} ON;";
                identityOff = $";SET IDENTITY_INSERT {dstFull} OFF;";
            }

            var insertSql = $"""
                {identityOn}
                INSERT INTO {dstFull} ({allColList})
                SELECT {allColList} FROM {tempTable} S
                WHERE NOT EXISTS (
                    SELECT 1 FROM {dstFull} T
                    WHERE {keyCond}
                );
                {identityOff}
                """;

            long inserted;
            using (var cmd = new SqlCommand(insertSql, dstConn))
            {
                cmd.CommandTimeout = _commandTimeout;
                inserted = await cmd.ExecuteNonQueryAsync();
            }
            Report($"  +{inserted} bản ghi mới");

            using (var cmd = new SqlCommand($"DROP TABLE {tempTable}", dstConn))
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

    private string BuildSelectSql(string srcFull, string colList, string tableName, string schema)
    {
        if (string.IsNullOrEmpty(_courseCode))
            return $"SELECT {colList} FROM {srcFull}";

        var courseFilter = GetCourseFilter(tableName);
        if (courseFilter == null)
            return $"SELECT {colList} FROM {srcFull}";

        return $"SELECT {colList} FROM {srcFull} WHERE {courseFilter}";
    }

    private string? GetCourseFilter(string tableName)
    {
        return tableName switch
        {
            "KhoaHoc" => $"[MaKH] = @CourseCode",
            "BaoCaoI" => $"[MaKH] = @CourseCode",
            "NguoiLX_HoSo" => $"[MaKhoaHoc] = @CourseCode",
            "KhoaHoc_GiaoVien" => $"[MaKH] = @CourseCode",
            "LichHoc" => $"[MaKH] = @CourseCode",
            "NguoiLX" => $"[MaDK] IN (SELECT [MaDK] FROM [NguoiLX_HoSo] WHERE [MaKhoaHoc] = @CourseCode)",
            "NguoiLX_GPLX" => $"[MaDK] IN (SELECT [MaDK] FROM [NguoiLX_HoSo] WHERE [MaKhoaHoc] = @CourseCode)",
            "NguoiLXHS_GiayTo" => $"[MaDK] IN (SELECT [MaDK] FROM [NguoiLX_HoSo] WHERE [MaKhoaHoc] = @CourseCode)",
            "BaoCaoII" => $"[MaBCI] IN (SELECT [MaBCI] FROM [BaoCaoI] WHERE [MaKH] = @CourseCode)",
            _ => null
        };
    }

    private async Task ApplyTransformAsync(SqlConnection conn, string tempTable, string tableName)
    {
        var updates = new List<string>();

            if (!string.IsNullOrEmpty(_newCsdtCode))
            {
                // For KhoaHoc we need to allocate new MaKH sequences based on NgayTao (year)
                if (tableName == "KhoaHoc")
                {
                var sqlAlloc = $"""
                    -- Allocate new MaKH for each row in {tempTable} using TT17 structure
                    -- assumes MaKH format: N1N2N3N4N5KYYNNNN where YY is last two digits of year
                    DECLARE @NewCsdt nvarchar(10) = @NewCsdtCode;
                    DECLARE @res int;
                    DECLARE @resName nvarchar(128) = N'GplxAllocateMaKH_' + @NewCsdt;
                    EXEC @res = sp_getapplock @Resource = @resName, @LockMode='Exclusive', @LockTimeout=60000, @LockOwner='Session';
                    IF @res < 0 BEGIN RAISERROR('Không lấy được khoá cấp MaKH',16,1); RETURN; END;
                    UPDATE [{tempTable}] SET MaKH_Cu = MaKH;
                    ;WITH t AS (
                        SELECT *, ROW_NUMBER() OVER (PARTITION BY RIGHT(CONVERT(varchar(4), YEAR(ISNULL(NgayTao, GETDATE()))),2) ORDER BY (SELECT 1)) AS rn,
                               RIGHT(CONVERT(varchar(4), YEAR(ISNULL(NgayTao, GETDATE()))),2) AS yy
                        FROM {tempTable}
                    ), maxes AS (
                        SELECT yy, ISNULL(MAX(CAST(SUBSTRING(MaKH,8,4) AS int)),0) AS maxseq
                        FROM (
                            SELECT DISTINCT RIGHT(CONVERT(varchar(4), YEAR(ISNULL(NgayTao, GETDATE()))),2) AS yy FROM {tempTable}
                        ) x
                        LEFT JOIN KhoaHoc k ON LEFT(k.MaKH,5) = @NewCsdt AND SUBSTRING(k.MaKH,6,2) = x.yy
                        GROUP BY yy
                    )
                    UPDATE t
                    SET MaKH = @NewCsdt + 'K' + t.yy + RIGHT('0000' + CAST((ISNULL(m.maxseq,0) + t.rn) AS varchar(4)),4)
                    FROM t
                    LEFT JOIN maxes m ON m.yy = t.yy;
                    EXEC sp_releaseapplock @Resource = @resName;
                    """;
                updates.Add(sqlAlloc);
                // Also set MaCSDT column to new code
                    updates.Add($"UPDATE [{tempTable}] SET [MaCSDT_Cu] = [MaCSDT], [MaCSDT] = @NewCsdtCode");
                // set course name if provided
                if (!string.IsNullOrEmpty(_newCourseName))
                {
                    updates.Add($"UPDATE [{tempTable}] SET [TenKH] = @NewCourseName");
                }
                    // we will still transform MaDK below to include yyyymmdd if possible
                }
                else
                {
                    updates.AddRange(GetCsdtTransform(tableName, tempTable));
                }
            }

        if (!string.IsNullOrEmpty(_newSoCode))
        {
            updates.AddRange(GetSoCodeTransform(tableName, tempTable));
        }

            foreach (var sql in updates)
            {
                using var cmd = new SqlCommand(sql, conn);
                var csdtParam = _allocatedCsdt ?? _newCsdtCode;
                if (!string.IsNullOrEmpty(csdtParam))
                    cmd.Parameters.AddWithValue("@NewCsdtCode", csdtParam);
                if (!string.IsNullOrEmpty(_newSoCode))
                    cmd.Parameters.AddWithValue("@NewSoCode", _newSoCode);
                // pass new course name if available
                if (!string.IsNullOrEmpty(_newCourseName))
                    cmd.Parameters.AddWithValue("@NewCourseName", _newCourseName);
                cmd.CommandTimeout = _commandTimeout;
                await cmd.ExecuteNonQueryAsync();
            }
    }

    private static List<string> GetCsdtTransform(string tableName, string tempTable)
    {
        var sql = new List<string>();

        if (HasColumn(tableName, "MaCSDT"))
        {
            sql.Add($"UPDATE [{tempTable}] SET [MaCSDT_Cu] = [MaCSDT], [MaCSDT] = @NewCsdtCode");
        }
        if (HasColumn(tableName, "MaKH"))
        {
            sql.Add($"UPDATE [{tempTable}] SET [MaKH_Cu] = [MaKH], [MaKH] = @NewCsdtCode + SUBSTRING([MaKH], 6, LEN([MaKH]))");
        }
        if (HasColumn(tableName, "MaKhoaHoc"))
        {
            sql.Add($"UPDATE [{tempTable}] SET [MaKhoaHoc_Cu] = [MaKhoaHoc], [MaKhoaHoc] = @NewCsdtCode + SUBSTRING([MaKhoaHoc], 6, LEN([MaKhoaHoc]))");
        }
        if (HasColumn(tableName, "MaDK"))
        {
            // For NguoiLX (học viên) we need to allocate a 6-digit sequence per NewCsdt
            if (tableName == "NguoiLX")
            {
                var sqlAlloc = $"""
                    -- Allocate MaDK for NguoiLX: NewCsdt-yyyymmdd-###### (6-digit sequence), reset per day
                    DECLARE @NewCsdt nvarchar(10) = @NewCsdtCode;
                    DECLARE @res int;
                    DECLARE @resName nvarchar(128) = N'GplxAllocateMaDK_' + @NewCsdt;
                    EXEC @res = sp_getapplock @Resource = @resName, @LockMode='Exclusive', @LockTimeout=60000, @LockOwner='Session';
                    IF @res < 0 BEGIN RAISERROR('Không lấy được khoá cấp MaDK',16,1); RETURN; END;
                    -- store old value
                    UPDATE [{tempTable}] SET MaDK_Cu = MaDK;
                    -- compute row number partitioned by ymd and determine existing max per ymd
                    ;WITH t AS (
                        SELECT *, ROW_NUMBER() OVER (PARTITION BY CONVERT(varchar(8), ISNULL(NgayTao, GETDATE()), 112) ORDER BY (SELECT 1)) AS rn,
                               CONVERT(varchar(8), ISNULL(NgayTao, GETDATE()), 112) AS ymd
                        FROM {tempTable}
                    ), distinct_dates AS (
                        SELECT DISTINCT CONVERT(varchar(8), ISNULL(NgayTao, GETDATE()), 112) AS ymd FROM {tempTable}
                    ), mx AS (
                        SELECT d.ymd, ISNULL(MAX(TRY_CONVERT(int, RIGHT(k.MaDK,6))),0) AS maxseq
                        FROM distinct_dates d
                        LEFT JOIN NguoiLX k ON LEFT(ISNULL(k.MaDK,''),5) = @NewCsdt AND SUBSTRING(k.MaDK, CHARINDEX('-', k.MaDK)+1, 8) = d.ymd
                        GROUP BY d.ymd
                    )
                    UPDATE t
                    SET MaDK = @NewCsdt + '-' + t.ymd + '-' + RIGHT('000000' + CAST((ISNULL(m.maxseq,0) + t.rn) AS varchar(6)),6)
                    FROM t
                    LEFT JOIN mx m ON m.ymd = t.ymd;
                    EXEC sp_releaseapplock @Resource = @resName;
                    """;
                sql.Add(sqlAlloc);
            }
            else
            {
                // For other tables that reference MaDK, we'll replace MaDK by joining dest.NguoiLX on MaDK_Cu
                var sqlMap = $"""
                    -- Map old MaDK -> new MaDK using destination NguoiLX.MaDK_Cu
                    UPDATE [{tempTable}] SET MaDK = ISNULL(n.MaDK, [{tempTable}].MaDK)
                    FROM [{tempTable}] t
                    LEFT JOIN NguoiLX n ON n.MaDK_Cu = t.MaDK;
                    """;
                sql.Add(sqlMap);
            }
        }

        return sql;
    }

    private static List<string> GetSoCodeTransform(string tableName, string tempTable)
    {
        if (!HasColumn(tableName, "MaSoGTVT"))
            return new List<string>();

        return new List<string>
        {
            $"UPDATE [{tempTable}] SET [MaSoGTVT_Cu] = [MaSoGTVT], [MaSoGTVT] = @NewSoCode"
        };
    }

    private static bool HasColumn(string tableName, string columnName)
    {
        return columnName switch
        {
            "MaCSDT" => tableName is "KhoaHoc" or "GiaoVien" or "XeTap" or "NguoiLX_HoSo" or "BaoCaoI" or "BaoCaoII" or "DM_LuuLuongDaoTao",
            "MaKH" => tableName is "KhoaHoc" or "BaoCaoI" or "KhoaHoc_GiaoVien" or "LichHoc",
            "MaKhoaHoc" => tableName == "NguoiLX_HoSo",
            "MaDK" => tableName is "NguoiLX" or "NguoiLX_HoSo" or "NguoiLX_GPLX" or "NguoiLXHS_GiayTo",
            "MaSoGTVT" => tableName is "KhoaHoc" or "GiaoVien" or "XeTap" or "NguoiLX_HoSo",
            _ => false
        };
    }

    private void Report(string msg) => OnProgress?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");

    private static readonly Dictionary<string, string> CuColumnTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MaCSDT_Cu"] = "varchar(6)",
        ["MaKH_Cu"] = "varchar(13)",
        ["MaKhoaHoc_Cu"] = "varchar(13)",
        ["MaDK_Cu"] = "varchar(25)",
        ["MaSoGTVT_Cu"] = "varchar(6)",
    };

    private List<string> GetRequiredCuColumns(string tableName, List<ColumnInfo> columns)
    {
        var names = new HashSet<string>(columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        if (!string.IsNullOrEmpty(_newCsdtCode))
        {
            if (HasColumn(tableName, "MaCSDT") && names.Contains("MaCSDT")) result.Add("MaCSDT_Cu");
            if (HasColumn(tableName, "MaKH") && names.Contains("MaKH")) result.Add("MaKH_Cu");
            if (HasColumn(tableName, "MaKhoaHoc") && names.Contains("MaKhoaHoc")) result.Add("MaKhoaHoc_Cu");
            if (HasColumn(tableName, "MaDK") && names.Contains("MaDK")) result.Add("MaDK_Cu");
        }

        if (!string.IsNullOrEmpty(_newSoCode))
        {
            if (HasColumn(tableName, "MaSoGTVT") && names.Contains("MaSoGTVT")) result.Add("MaSoGTVT_Cu");
        }

        return result;
    }

    private async Task EnsureCuColumnsAsync(SqlConnection conn, string dstFull, List<string> cuCols)
    {
        foreach (var cu in cuCols)
        {
            if (!CuColumnTypes.TryGetValue(cu, out var type))
                type = "varchar(13)";
            var sql = $"""
                IF NOT EXISTS (
                    SELECT 1 FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'{dstFull}')
                      AND name = N'{cu}'
                )
                ALTER TABLE {dstFull} ADD [{cu}] {type} NULL;
                """;
            using var cmd = new SqlCommand(sql, conn);
            cmd.CommandTimeout = _commandTimeout;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static string BuildCreateTempSql(string tempTable, List<ColumnInfo> columns, ColumnInfo? identityCol)
    {
        var cols = columns.Select(c =>
        {
            var type = MapToSqlType(c);
            return $"[{c.Name}] {type} {(c.IsNullable ? "NULL" : "NOT NULL")}";
        }).ToList();

        var extra = new List<string>();
        if (columns.Any(c => c.Name is "MaCSDT" or "MaKH" or "MaKhoaHoc" or "MaDK" or "MaSoGTVT"))
        {
            if (columns.Any(c => c.Name == "MaCSDT")) extra.Add("[MaCSDT_Cu] [varchar](6) NULL");
            if (columns.Any(c => c.Name == "MaKH")) extra.Add("[MaKH_Cu] [varchar](13) NULL");
            if (columns.Any(c => c.Name == "MaKhoaHoc")) extra.Add("[MaKhoaHoc_Cu] [varchar](13) NULL");
            if (columns.Any(c => c.Name == "MaDK")) extra.Add("[MaDK_Cu] [varchar](25) NULL");
            if (columns.Any(c => c.Name == "MaSoGTVT")) extra.Add("[MaSoGTVT_Cu] [varchar](6) NULL");
        }

        cols.AddRange(extra);
        return $"CREATE TABLE [{tempTable}] (\n  {string.Join(",\n  ", cols)}\n);";
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
