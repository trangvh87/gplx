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
    private readonly string? _explicitNewCourseCode;
    private readonly string? _oldPhotoPath;
    private readonly string? _newPhotoPath;
    private readonly string _runId = Guid.NewGuid().ToString("N");
    private readonly bool _allocateCsdt;
    private readonly bool _captureCuAlways;

    public event DbSyncProgressHandler? OnProgress;

    public TableSyncEngine(
        string sourceConn, string destConn,
        List<SyncTableConfig> tables,
        int batchSize = 50000, int commandTimeout = 600,
        string? newCsdtCode = null,
        string? newSoCode = null,
        string? courseCode = null,
        string? newCourseName = null,
        string? explicitNewCourseCode = null,
        string? oldPhotoPath = null,
        string? newPhotoPath = null)
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
        _explicitNewCourseCode = explicitNewCourseCode;
        _oldPhotoPath = oldPhotoPath;
        _newPhotoPath = newPhotoPath;
        _allocateCsdt = false;
        _captureCuAlways = false;
    }

    public TableSyncEngine(
        string sourceConn, string destConn,
        List<SyncTableConfig> tables,
        int batchSize, int commandTimeout,
        string? newCsdtCode = null,
        string? newSoCode = null,
        string? courseCode = null,
        string? newCourseName = null,
        string? explicitNewCourseCode = null,
        string? oldPhotoPath = null,
        string? newPhotoPath = null,
        bool allocateCsdt = false)
        : this(sourceConn, destConn, tables, batchSize, commandTimeout, newCsdtCode, newSoCode, courseCode, newCourseName, explicitNewCourseCode, oldPhotoPath, newPhotoPath)
    {
        _allocateCsdt = allocateCsdt;
        _captureCuAlways = false;
    }

    public TableSyncEngine(
        string sourceConn, string destConn,
        List<SyncTableConfig> tables,
        int batchSize, int commandTimeout,
        string? newCsdtCode = null,
        string? newSoCode = null,
        string? courseCode = null,
        string? newCourseName = null,
        string? explicitNewCourseCode = null,
        string? oldPhotoPath = null,
        string? newPhotoPath = null,
        bool allocateCsdt = false,
        bool captureCuAlways = false)
        : this(sourceConn, destConn, tables, batchSize, commandTimeout, newCsdtCode, newSoCode, courseCode, newCourseName, explicitNewCourseCode, oldPhotoPath, newPhotoPath, allocateCsdt)
    {
        _captureCuAlways = captureCuAlways;
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
            // If we created _Cu columns in the temp table, include source columns aliased into those _Cu columns
            // so that the bulk copy writes the original values directly.
            if (cuCols.Count > 0)
            {
                var cuAliases = new List<string>();
                foreach (var cu in cuCols)
                {
                    var orig = cu.EndsWith("_Cu", StringComparison.OrdinalIgnoreCase)
                        ? cu.Substring(0, cu.Length - 3)
                        : null;
                    if (orig != null && columns.Any(c => c.Name.Equals(orig, StringComparison.OrdinalIgnoreCase)))
                    {
                        cuAliases.Add($"[{orig}] AS [{cu}]");
                    }
                }
                if (cuAliases.Count > 0)
                {
                    var idx = selectSql.IndexOf(" FROM ", StringComparison.OrdinalIgnoreCase);
                    if (idx > 0)
                    {
                        selectSql = selectSql.Substring(0, idx) + ", " + string.Join(", ", cuAliases) + selectSql.Substring(idx);
                    }
                }
            }
            // Log the SELECT used for bulk copy and the CourseCode parameter for diagnostics
            Report($"  SELECT SQL: {selectSql}");
            if (!string.IsNullOrEmpty(_courseCode)) Report($"  CourseCode param: {_courseCode}");
            using (var bulk = new SqlBulkCopy(dstConn)
            {
                DestinationTableName = tempTable,
                BatchSize = _batchSize,
                BulkCopyTimeout = _commandTimeout,
                EnableStreaming = true
            })
            {
                // Ensure explicit column mappings by name so aliased _Cu columns map correctly
                var srcColNames = columns.Select(c => c.Name).ToList();
                srcColNames.AddRange(cuCols);
                foreach (var name in srcColNames)
                {
                    try { bulk.ColumnMappings.Add(name, name); } catch { /* ignore duplicates */ }
                }

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

            // Ensure "_Cu" columns capture the original source values immediately after bulk copy.
            // Some transforms rely on these _Cu snapshots (MaSoGTVT_Cu, MaKH_Cu, MaCSDT_Cu, etc.).
            try
            {
                var cuAssignments = new List<string>();
                // Map of Cu column -> original column name
                var cuMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["MaSoGTVT_Cu"] = "MaSoGTVT",
                    ["MaKH_Cu"] = "MaKH",
                    ["MaCSDT_Cu"] = "MaCSDT",
                    ["MaKhoaHoc_Cu"] = "MaKhoaHoc",
                    ["MaDK_Cu"] = "MaDK"
                };

                var dstNamesSet = new HashSet<string>(columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
                foreach (var kv in cuMap)
                {
                    var cu = kv.Key;
                    var orig = kv.Value;
                    if (dstNamesSet.Contains(orig))
                    {
                        cuAssignments.Add($"[{cu}] = [{orig}]");
                    }
                }

                if (cuAssignments.Count > 0)
                {
                    var updateSql = $"UPDATE [{tempTable}] SET {string.Join(", ", cuAssignments)}";
                    using var cmdCu = new SqlCommand(updateSql, dstConn);
                    cmdCu.CommandTimeout = _commandTimeout;
                    await cmdCu.ExecuteNonQueryAsync();
                    Report($"  Initialized _Cu columns: {string.Join(",", cuAssignments.Select(s => s.Split('=')[0].Trim()))}");
                }
            }
            catch (Exception ex)
            {
                Report($"  Cảnh báo: không thể khởi tạo các cột _Cu trên bảng tạm: {ex.Message}");
            }

            await ApplyTransformAsync(dstConn, tempTable, table.DestTable);
            Report($"  Transform mã");

            // Diagnostic logging: report distinct MaKH/MaKhoaHoc values in the temp table
            try
            {
                if (columns.Any(c => c.Name == "MaKH" || c.Name == "MaKhoaHoc"))
                {
                    using var cmdCount = new SqlCommand($"SELECT COUNT(*) FROM {tempTable} WHERE ISNULL(MaKH,'') <> ''", dstConn);
                    cmdCount.CommandTimeout = _commandTimeout;
                    var cnt = Convert.ToInt32(await cmdCount.ExecuteScalarAsync());
                    Report($"  Temp table {tempTable} MaKH non-empty count: {cnt}");

                    using var cmdDistinct = new SqlCommand($"SELECT DISTINCT TOP 10 ISNULL(MaKH,'') FROM {tempTable}", dstConn);
                    cmdDistinct.CommandTimeout = _commandTimeout;
                    using var rdr = await cmdDistinct.ExecuteReaderAsync();
                    var vals = new List<string>();
                    while (await rdr.ReadAsync()) vals.Add(rdr.GetString(0));
                    Report($"  Temp table {tempTable} distinct MaKH (up to 10): {string.Join(", ", vals)}");

                    // also check destination KhoaHoc for the explicit target MaKH if provided
                    if (!string.IsNullOrEmpty(_explicitNewCourseCode))
                    {
                        using var cmdExist = new SqlCommand("SELECT COUNT(*) FROM KhoaHoc WHERE MaKH = @p", dstConn);
                        cmdExist.Parameters.AddWithValue("@p", _explicitNewCourseCode);
                        cmdExist.CommandTimeout = _commandTimeout;
                        var exist = Convert.ToInt32(await cmdExist.ExecuteScalarAsync());
                        Report($"  Dest KhoaHoc has explicit MaKH={_explicitNewCourseCode}: {exist}");
                    }
                }
            }
            catch (Exception ex)
            {
                Report($"  Diagnostic log failed: {ex.Message}");
            }

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

            // Diagnostic: compute how many rows in the temp table and how many already exist in destination
            try
            {
                var keyCols = table.KeyColumns.Where(k => columns.Any(c => c.Name.Equals(k, StringComparison.OrdinalIgnoreCase))).ToList();
                if (keyCols.Count > 0)
                {
                    var tempCountSql = $"SELECT COUNT(*) FROM {tempTable}";
                    using var cmdTempCount = new SqlCommand(tempCountSql, dstConn);
                    cmdTempCount.CommandTimeout = _commandTimeout;
                    var tempCount = Convert.ToInt32(await cmdTempCount.ExecuteScalarAsync());
                    Report($"  Temp rows: {tempCount}");

                    var joinCond = string.Join(" AND ", keyCols.Select(k => $"t.[{k}] = d.[{k}]"));
                    var existsSql = $"SELECT COUNT(*) FROM {tempTable} t INNER JOIN {dstFull} d ON {joinCond}";
                    using var cmdExists = new SqlCommand(existsSql, dstConn);
                    cmdExists.CommandTimeout = _commandTimeout;
                    var existCount = Convert.ToInt32(await cmdExists.ExecuteScalarAsync());
                    Report($"  Temp rows already present in dest (by key): {existCount}");

                    var toInsert = tempCount - existCount;
                    Report($"  Rows expected to insert: {toInsert}");

                    // show sample keys that would be inserted
                    var sampleSql = $"SELECT TOP 10 {string.Join(", ", keyCols.Select(k => $"t.[{k}]") )} FROM {tempTable} t WHERE NOT EXISTS (SELECT 1 FROM {dstFull} d WHERE {joinCond})";
                    using var cmdSample = new SqlCommand(sampleSql, dstConn);
                    cmdSample.CommandTimeout = _commandTimeout;
                    using var rdr = await cmdSample.ExecuteReaderAsync();
                    var samples = new List<string>();
                    while (await rdr.ReadAsync())
                    {
                        var vals = new List<string>();
                        for (int i = 0; i < rdr.FieldCount; i++) vals.Add(rdr.IsDBNull(i) ? "NULL" : rdr.GetValue(i).ToString());
                        samples.Add(string.Join("|", vals));
                    }
                    if (samples.Count > 0) Report($"  Sample keys to insert (up to 10): {string.Join(", ", samples)}");
                }
            }
            catch (Exception ex)
            {
                Report($"  Diagnostic join check failed: {ex.Message}");
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
                    BEGIN TRY
                        BEGIN TRAN;
                        -- Allocate new MaKH for each row in {tempTable} using TT17 structure
                        -- assumes MaKH format: N1N2N3N4N5KYYNNNN where YY is last two digits of year
                        DECLARE @NewCsdt nvarchar(10) = @NewCsdtCode;
                        DECLARE @res int;
                        DECLARE @resName nvarchar(128) = N'GplxAllocateMaKH_' + @NewCsdt;
                        EXEC @res = sp_getapplock @Resource = @resName, @LockMode='Exclusive', @LockTimeout=60000, @LockOwner='Session';
                        IF @res < 0 BEGIN RAISERROR('Không lấy được khoá cấp MaKH',16,1); ROLLBACK TRAN; RETURN; END;
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
                        EXEC sp_releaseapplock @Resource = @resName, @LockOwner='Session';
                        COMMIT TRAN;
                    END TRY
                    BEGIN CATCH
                        IF XACT_STATE() <> 0 AND @@TRANCOUNT > 0 ROLLBACK TRAN;
                        DECLARE @err nvarchar(4000) = ERROR_MESSAGE();
                        RAISERROR(@err,16,1);
                    END CATCH
                    """;
                updates.Add(sqlAlloc);
                // if explicit full MaKH provided, set it directly and skip TT17 allocation
                if (!string.IsNullOrEmpty(_explicitNewCourseCode))
                {
                    updates.Add($"UPDATE [{tempTable}] SET [MaKH_Cu] = ISNULL([MaKH_Cu],[MaKH]), [MaKH] = @ExplicitMaKH");
                    // ensure MaCSDT matches left 5 chars
                    updates.Add($"UPDATE [{tempTable}] SET [MaCSDT_Cu] = ISNULL([MaCSDT_Cu],[MaCSDT]), [MaCSDT] = LEFT(@ExplicitMaKH, 5)");
                }
                // Also set MaCSDT column to new code
                    updates.Add($"UPDATE [{tempTable}] SET [MaCSDT_Cu] = ISNULL([MaCSDT_Cu],[MaCSDT]), [MaCSDT] = @NewCsdtCode");
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

            // If an explicit full course code was provided by the user, ensure non-KhoaHoc tables
            // also get their MaKH/MaKhoaHoc/MaCSDT values overridden to that explicit value so
            // foreign keys remain consistent with the KhoaHoc row which may have been set
            // to @ExplicitMaKH earlier.
            if (!string.IsNullOrEmpty(_explicitNewCourseCode) && tableName != "KhoaHoc")
            {
                if (HasColumn(tableName, "MaCSDT"))
                    updates.Add($"UPDATE [{tempTable}] SET [MaCSDT_Cu] = [MaCSDT], [MaCSDT] = LEFT(@ExplicitMaKH, 5)");
                if (HasColumn(tableName, "MaKH"))
                    updates.Add($"UPDATE [{tempTable}] SET [MaKH_Cu] = ISNULL([MaKH_Cu],[MaKH]), [MaKH] = @ExplicitMaKH");
                if (HasColumn(tableName, "MaKhoaHoc"))
                    updates.Add($"UPDATE [{tempTable}] SET [MaKhoaHoc_Cu] = [MaKhoaHoc], [MaKhoaHoc] = @ExplicitMaKH");
            }

            // Photo path transform for NguoiLX_HoSo
            if (!string.IsNullOrEmpty(_oldPhotoPath) && !string.IsNullOrEmpty(_newPhotoPath))
            {
                updates.AddRange(GetPhotoPathTransform(tableName, tempTable));
            }

            foreach (var sql in updates)
            {
                using var cmd = new SqlCommand(sql, conn);
                var csdtParam = _newCsdtCode;
                if (!string.IsNullOrEmpty(csdtParam))
                    cmd.Parameters.AddWithValue("@NewCsdtCode", csdtParam);
                if (!string.IsNullOrEmpty(_explicitNewCourseCode))
                    cmd.Parameters.AddWithValue("@ExplicitMaKH", _explicitNewCourseCode);
                if (!string.IsNullOrEmpty(_newSoCode))
                    cmd.Parameters.AddWithValue("@NewSoCode", _newSoCode);
                // pass new course name if available
                if (!string.IsNullOrEmpty(_newCourseName))
                    cmd.Parameters.AddWithValue("@NewCourseName", _newCourseName);
                if (!string.IsNullOrEmpty(_newPhotoPath))
                    cmd.Parameters.AddWithValue("@NewPhotoPath", _newPhotoPath);
                cmd.CommandTimeout = _commandTimeout;
                await cmd.ExecuteNonQueryAsync();
            }
    }

    private List<string> GetCsdtTransform(string tableName, string tempTable)
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
            // For NguoiLX (học viên) we need to decide how to produce MaDK depending on mode:
            // - If _allocateCsdt == true => TT17 mode: allocate new MaDK sequences NewCsdt-yyyymmdd-######
            // - Else if _newCsdtCode provided => Replace mode: keep existing suffix (from first '-') but replace prefix with NewCsdt
            // - Else (no change) => leave MaDK as-is and mapping of dependent tables will be handled elsewhere
            if (tableName == "NguoiLX")
            {
                if (_allocateCsdt)
                {
                    var sqlAlloc = $"""
                        BEGIN TRY
                            BEGIN TRAN;
                            -- Allocate MaDK for NguoiLX: NewCsdt-yyyymmdd-###### (6-digit sequence), reset per day
                            DECLARE @NewCsdt nvarchar(10) = @NewCsdtCode;
                            DECLARE @res int;
                            DECLARE @resName nvarchar(128) = N'GplxAllocateMaDK_' + @NewCsdt;
                            EXEC @res = sp_getapplock @Resource = @resName, @LockMode='Exclusive', @LockTimeout=60000, @LockOwner='Session';
                            IF @res < 0 BEGIN RAISERROR('Không lấy được khoá cấp MaDK',16,1); ROLLBACK TRAN; RETURN; END;
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
                            EXEC sp_releaseapplock @Resource = @resName, @LockOwner='Session';
                            COMMIT TRAN;
                        END TRY
                        BEGIN CATCH
                            IF XACT_STATE() <> 0 AND @@TRANCOUNT > 0 ROLLBACK TRAN;
                            DECLARE @err nvarchar(4000) = ERROR_MESSAGE();
                            RAISERROR(@err,16,1);
                        END CATCH
                        """;
                    sql.Add(sqlAlloc);
                }
                else if (!string.IsNullOrEmpty(_newCsdtCode))
                {
                    // Replace prefix (first 5 chars / part before '-') of existing MaDK with new Csdt code,
                    // keeping the remainder (including leading '-') when possible. If MaDK does not contain '-',
                    // fall back to building a simple MaDK with date and sequence '000001'.
                    var sqlReplace = $"""
                        -- Replace MaDK prefix with new Csdt while preserving suffix after first '-'
                        UPDATE [{tempTable}]
                        SET MaDK_Cu = MaDK,
                            MaDK = CASE WHEN CHARINDEX('-', ISNULL(MaDK,'')) > 0 THEN @NewCsdtCode + SUBSTRING(MaDK, CHARINDEX('-', MaDK), LEN(MaDK))
                                        ELSE @NewCsdtCode + '-' + CONVERT(varchar(8), ISNULL(NgayTao, GETDATE()), 112) + '-000001' END;
                        """;
                    sql.Add(sqlReplace);
                }
                // else: no change to MaDK
            }
            else
            {
                // For other tables that reference MaDK, we'll replace MaDK by joining dest.NguoiLX on MaDK_Cu
                var sqlMap = $"""
                    -- Map old MaDK -> new MaDK using destination NguoiLX.MaDK_Cu
                    UPDATE t
                    SET MaDK = ISNULL(n.MaDK, t.MaDK)
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

    private List<string> GetPhotoPathTransform(string tableName, string tempTable)
    {
        if (tableName != "NguoiLX_HoSo" || string.IsNullOrEmpty(_oldPhotoPath) || string.IsNullOrEmpty(_newPhotoPath))
            return new List<string>();

        // The new photo path structure: [NewPhotoPath]\[MaKhoaHoc]\[MaDK].[extension]
        // NguoiLX_HoSo uses MaKhoaHoc, not MaKH
        
        var sql = $"""
            -- Update DuongDanAnh to new path structure: [NewPhotoPath]\[MaKhoaHoc]\[MaDK].[ext]
            -- Extract extension from old DuongDanAnh
            UPDATE [{tempTable}]
            SET DuongDanAnh = @NewPhotoPath + '\' + [MaKhoaHoc] + '\' + [MaDK] + '.' + 
                CASE 
                    WHEN CHARINDEX('.', REVERSE(ISNULL(DuongDanAnh,''))) > 0 
                    THEN REVERSE(SUBSTRING(REVERSE(DuongDanAnh), 1, CHARINDEX('.', REVERSE(DuongDanAnh)) - 1))
                    ELSE 'jp2' END
            WHERE DuongDanAnh IS NOT NULL AND DuongDanAnh <> '';
            """;

        return new List<string> { sql };
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

        // Add _Cu snapshot columns when either a new CSĐT is being applied OR the user provided an explicit
        // new course code (explicitNewCourseCode). The explicitNewCourseCode case includes the "Giữ nguyên"
        // mode where the user keeps the original code but still expects _Cu snapshots to be recorded.
        if (!string.IsNullOrEmpty(_newCsdtCode) || !string.IsNullOrEmpty(_explicitNewCourseCode) || _captureCuAlways)
        {
            if (HasColumn(tableName, "MaCSDT") && names.Contains("MaCSDT")) result.Add("MaCSDT_Cu");
            if (HasColumn(tableName, "MaKH") && names.Contains("MaKH")) result.Add("MaKH_Cu");
            if (HasColumn(tableName, "MaKhoaHoc") && names.Contains("MaKhoaHoc")) result.Add("MaKhoaHoc_Cu");
            if (HasColumn(tableName, "MaDK") && names.Contains("MaDK")) result.Add("MaDK_Cu");
        }

        // MaSoGTVT_Cu should be created when a new Sở code is provided or when explicitNewCourseCode is present
        if (!string.IsNullOrEmpty(_newSoCode) || !string.IsNullOrEmpty(_explicitNewCourseCode) || _captureCuAlways)
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
