using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Gplx.SoApp;

public delegate void ScriptProgressHandler(string message);

public sealed class ScriptRunner
{
    private readonly string _scriptsDir;
    private readonly string _srcDb;
    private readonly string _dstDb;
    private readonly string _srcConnStr;
    private readonly string _dstConnStr;
    private readonly string? _newSoCode;
    private SqlConnection? _srcConn;

    public event ScriptProgressHandler? OnProgress;

    public ScriptRunner(string scriptsDir, string srcDb, string dstDb,
        string srcConnStr, string dstConnStr,
        string? newSoCode = null)
    {
        _scriptsDir = scriptsDir;
        _srcDb = srcDb;
        _dstDb = dstDb;
        _srcConnStr = srcConnStr;
        _dstConnStr = dstConnStr;
        _newSoCode = newSoCode;
    }

    public async Task<ScriptRunResult> RunAllAsync()
    {
        var sw = Stopwatch.StartNew();
        var dir = new DirectoryInfo(_scriptsDir);

        if (!dir.Exists)
        {
            Report($"Thư mục script không tồn tại: {_scriptsDir}");
            return new ScriptRunResult { Total = 0, SuccessCount = 0, FailCount = 0, Elapsed = sw.Elapsed };
        }

        var files = dir.GetFiles("*.sql").OrderBy(f => f.Name).ToList();
        if (files.Count == 0)
        {
            Report("Không tìm thấy file .sql nào.");
            return new ScriptRunResult { Total = 0, SuccessCount = 0, FailCount = 0, Elapsed = sw.Elapsed };
        }

        Report($"Tìm thấy {files.Count} script, bắt đầu đồng bộ...");

        // Open shared source connection and create filter temp tables
        Report("Tạo bộ lọc từ nguồn...");
        _srcConn = new SqlConnection(_srcConnStr);
        await _srcConn.OpenAsync();
        try
        {
            await CreateSourceFilterTablesAsync();

            var ok = 0;
            var fail = 0;
            var totalInserted = 0;

            foreach (var file in files)
            {
                if (file.Name.Contains("Fail")) continue;

                Report($"--- {file.Name} ---");

                try
                {
                    var sql = File.ReadAllText(file.FullName);
                    var parsed = ParseScript(sql, file.Name);
                    if (parsed == null)
                    {
                        Report("  Bỏ qua: không parse được script");
                        continue;
                    }

                    var inserted = await SyncTableAsync(parsed.Value);
                    totalInserted += inserted;
                    ok++;
                    Report($"  +{inserted} bản ghi");
                }
                catch (Exception ex)
                {
                    fail++;
                    Report($"  LỖI: {ex.Message}");
                }
            }

            sw.Stop();
            Report($"Hoàn tất: {ok}/{files.Count} script, +{totalInserted} bản ghi, {fail} lỗi ({sw.Elapsed.TotalSeconds:F1}s)");

            return new ScriptRunResult
            {
                Total = files.Count,
                SuccessCount = ok,
                FailCount = fail,
                TotalInserted = totalInserted,
                Elapsed = sw.Elapsed
            };
        }
        finally
        {
            // Cleanup temp tables and close connection
            try
            {
                using var cmd = _srcConn.CreateCommand();
                cmd.CommandText = @"
                    IF OBJECT_ID('tempdb..#srcCourseFilter') IS NOT NULL DROP TABLE #srcCourseFilter;
                    IF OBJECT_ID('tempdb..#srcMaBCIFilter') IS NOT NULL DROP TABLE #srcMaBCIFilter;
                    IF OBJECT_ID('tempdb..#srcMaDKFilter') IS NOT NULL DROP TABLE #srcMaDKFilter;
                    IF OBJECT_ID('tempdb..#srcMaKySHFilter') IS NOT NULL DROP TABLE #srcMaKySHFilter;";
                cmd.CommandTimeout = 30;
                await cmd.ExecuteNonQueryAsync();
            }
            catch { }
            _srcConn.Close();
            _srcConn.Dispose();
        }
    }

    private async Task CreateSourceFilterTablesAsync()
    {
        // Create temp tables on source for filtering
        Report("  Khóa học...");

        // #srcCourseFilter (MaKH from KhoaHoc within course range)
        var srcMaKH = new List<string>();
        using (var cmd = _srcConn!.CreateCommand())
        {
            cmd.CommandText = $"SELECT DISTINCT MaKH FROM [{_srcDb}].[dbo].[KhoaHoc]";
            cmd.CommandTimeout = 120;
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                srcMaKH.Add(reader.GetString(0));
        }
        Report($"  KhoaHoc: {srcMaKH.Count} mã KH");

        if (srcMaKH.Count > 0)
        {
            var khIn = BuildInClause(srcMaKH);

            // #srcCourseFilter
            using (var cmd = _srcConn!.CreateCommand())
            {
                cmd.CommandText = $"CREATE TABLE #srcCourseFilter (MaKH NVARCHAR(50) PRIMARY KEY); " +
                    $"INSERT INTO #srcCourseFilter SELECT DISTINCT MaKH FROM [{_srcDb}].[dbo].[KhoaHoc] WHERE MaKH IN ({khIn})";
                cmd.CommandTimeout = 120;
                await cmd.ExecuteNonQueryAsync();
            }
            Report($"  #srcCourseFilter: {srcMaKH.Count} dòng");

            // #srcMaBCIFilter (MaBCI from BaoCaoI)
            using (var cmd = _srcConn!.CreateCommand())
            {
                cmd.CommandText = $"CREATE TABLE #srcMaBCIFilter (MaBCI NVARCHAR(50) PRIMARY KEY); " +
                    $"INSERT INTO #srcMaBCIFilter SELECT DISTINCT MaBCI FROM [{_srcDb}].[dbo].[BaoCaoI] WHERE MaKH IN ({khIn})";
                cmd.CommandTimeout = 120;
                var rows = await cmd.ExecuteNonQueryAsync();
                Report($"  #srcMaBCIFilter: {rows} dòng");
            }

            // #srcMaDKFilter (MaDK from NguoiLX_HoSo)
            using (var cmd = _srcConn!.CreateCommand())
            {
                cmd.CommandText = $"CREATE TABLE #srcMaDKFilter (MaDK NVARCHAR(50) PRIMARY KEY); " +
                    $"INSERT INTO #srcMaDKFilter SELECT DISTINCT MaDK FROM [{_srcDb}].[dbo].[NguoiLX_HoSo] WHERE MaKhoaHoc IN ({khIn})";
                cmd.CommandTimeout = 120;
                var rows = await cmd.ExecuteNonQueryAsync();
                Report($"  #srcMaDKFilter: {rows} dòng");
            }
        }

        // #srcMaKySHFilter (MaKySH from KySH)
        using (var cmd = _srcConn!.CreateCommand())
        {
            cmd.CommandText = $"CREATE TABLE #srcMaKySHFilter (MaKySH NVARCHAR(50) PRIMARY KEY); " +
                $"INSERT INTO #srcMaKySHFilter SELECT DISTINCT MaTTSH FROM [{_srcDb}].[dbo].[KySH]";
            cmd.CommandTimeout = 120;
            var rows = await cmd.ExecuteNonQueryAsync();
            Report($"  #srcMaKySHFilter: {rows} dòng");
        }
    }

    // ── Script parsing ──

    private enum FilterType { None, Course, MaBCI, MaDK, MaKySH, CourseOnMaTTSH, CourseOnMaKhoaHoc }

    private readonly struct ParsedScript
    {
        public string DestTable { get; init; }
        public string[] Columns { get; init; }
        public string SourceTable { get; init; }
        public string[] SourceJoinSql { get; init; }
        public string[] KeyColumns { get; init; }
        public FilterType Filter { get; init; }
        public string FilterColumn { get; init; }
    }

    private ParsedScript? ParseScript(string sql, string fileName)
    {
        // Remove USE and GO
        sql = Regex.Replace(sql, @"USE\s+\[.*?\]\s*GO", "", RegexOptions.IgnoreCase);

        // Extract dest table from INSERT INTO
        var insertMatch = Regex.Match(sql, @"INSERT\s+INTO\s+(?:\[\w+\]\.)?\[?dbo\]?\.\[?(\w+)\]?", RegexOptions.IgnoreCase);
        if (!insertMatch.Success)
        {
            Report($" Parse lỗi: không tìm thấy INSERT INTO trong {fileName}");
            return null;
        }
        var destTable = insertMatch.Groups[1].Value;

        // Extract column list — use [\s\S] instead of . to cross newlines
        var colMatch = Regex.Match(sql, @"INSERT\s+INTO\s+[\s\S]*?\(([\s\S]*?)\)\s*(?:SELECT|select)", RegexOptions.IgnoreCase);
        if (!colMatch.Success)
        {
            Report($" Parse lỗi: không tìm thấy column list trong {fileName}");
            return null;
        }
        var columns = colMatch.Groups[1].Value
            .Split('\n')
            .Select(l => Regex.Replace(l.Trim().TrimStart(','), @"[\[\]]", "").Trim())
            .Where(c => c.Length > 0)
            .ToArray();

        // Extract source table from FROM — handles GPLX_SOGTVT.[dbo].[Table], [db].[dbo].[Table], or [dbo].[Table]
        var fromRegex = @"FROM\s+(?:\w+\.)?\[?dbo\]?\.\[?(\w+)\]?\s+as\s+\w+";
        var fromMatch = Regex.Match(sql, fromRegex, RegexOptions.IgnoreCase);
        if (!fromMatch.Success)
        {
            Report($" Parse lỗi: không tìm thấy FROM in {fileName}");
            return null;
        }
        var sourceTable = fromMatch.Groups[1].Value;

        // Extract JOINs (lines between FROM and {CourseFilter})
        var joins = new List<string>();
        var lines = sql.Split('\n');
        bool inJoin = false;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.ToUpper().StartsWith("FROM") && trimmed.Contains(sourceTable))
            {
                inJoin = true;
                continue;
            }
            if (inJoin)
            {
                if (trimmed.Contains("{CourseFilter}") || trimmed.ToUpper().Contains("WHERE NOT EXISTS"))
                    break;
                if (trimmed.ToUpper().StartsWith("JOIN"))
                {
                    // Only keep source JOINs (not dest DB joins)
                    if (!trimmed.ToUpper().Contains(_dstDb.ToUpper()) &&
                        !trimmed.ToUpper().Contains("GPLX_CDB_SOXAYDUNG"))
                    {
                        joins.Add(trimmed);
                    }
                }
            }
        }

        // Extract key columns from WHERE NOT EXISTS
        var keyMatch = Regex.Match(sql, @"WHERE\s+NOT\s+EXISTS.*?WHERE\s+(.*?)\s*=\s*S\.\[(\w+)\]", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var keyColumns = new[] { keyMatch.Groups[2].Value };

        // Determine filter type
        var filter = GetFilterType(fileName, destTable, sourceTable);
        var filterCol = GetFilterColumn(filter);

        return new ParsedScript
        {
            DestTable = destTable,
            Columns = columns,
            SourceTable = sourceTable,
            SourceJoinSql = joins.ToArray(),
            KeyColumns = keyColumns,
            Filter = filter,
            FilterColumn = filterCol
        };
    }

    private static FilterType GetFilterType(string fileName, string destTable, string sourceTable)
    {
        if (destTable is "DM_DonViGTVT" or "DM_DVHC" or "SatHachVien" or "SOGPLXSEQ")
            return FilterType.None;

        if (destTable == "KhoaHoc" || destTable == "BaoCaoI")
            return FilterType.Course;

        if (destTable == "BaoCaoII")
            return FilterType.MaBCI;

        if (destTable == "KySH")
            return FilterType.CourseOnMaTTSH;

        if (destTable == "NguoiLX_HoSo")
            return FilterType.CourseOnMaKhoaHoc;

        if (destTable is "NguoiLX" or "NguoiLX_GPLX" or "SO_GPLX" or "SoGPLXQuanLy" or "NguoiLXHS_GiayTo")
            return FilterType.MaDK;

        if (destTable == "NguoiLXHS_KQSH")
            return FilterType.MaKySH;

        return FilterType.None;
    }

    private static string GetFilterColumn(FilterType filter) => filter switch
    {
        FilterType.Course => "MaKH",
        FilterType.MaBCI => "MaBCI",
        FilterType.MaDK => "MaDK",
        FilterType.MaKySH => "MaKySH",
        FilterType.CourseOnMaTTSH => "MaTTSH",
        FilterType.CourseOnMaKhoaHoc => "MaKhoaHoc",
        _ => ""
    };

    // ── Sync execution ──

    private async Task<int> SyncTableAsync(ParsedScript parsed)
    {
        // Build source query with temp table JOINs
        var srcQuery = BuildSourceQuery(parsed);
        Report($"  Truy vấn nguồn...");

        // Run source query on shared source connection
        DataTable srcData;
        using (var cmd = _srcConn!.CreateCommand())
        {
            cmd.CommandText = srcQuery;
            cmd.CommandTimeout = 300;
            using var adapter = new SqlDataAdapter(cmd);
            srcData = new DataTable();
            adapter.Fill(srcData);
        }
        Report($"  Nguồn: {srcData.Rows.Count} bản ghi");

        if (srcData.Rows.Count == 0) return 0;

        // Dedup: get existing keys from destination
        var existingKeys = await GetExistingKeysAsync(parsed.DestTable, parsed.KeyColumns);
        if (existingKeys.Count > 0)
        {
            var toRemove = new List<DataRow>();
            foreach (DataRow row in srcData.Rows)
            {
                var key = string.Join("\t", parsed.KeyColumns.Select(k => row[k]?.ToString() ?? ""));
                if (existingKeys.Contains(key))
                    toRemove.Add(row);
            }
            foreach (var row in toRemove)
                srcData.Rows.Remove(row);
        }
        Report($"  Sau khi lọc: {srcData.Rows.Count} bản ghi mới");

        if (srcData.Rows.Count == 0) return 0;

        // BulkCopy to destination
        return await BulkCopyToDestAsync(parsed.DestTable, parsed.Columns, srcData);
    }

    private string BuildSourceQuery(ParsedScript parsed)
    {
        var sb = new StringBuilder();
        sb.Append("SELECT ");
        sb.Append(string.Join(", ", parsed.Columns.Select(c => $"S.[{c}]")));
        sb.Append($" FROM [{_srcDb}].[dbo].[{parsed.SourceTable}] AS S");

        foreach (var join in parsed.SourceJoinSql)
            sb.Append($"\n  {join}");

        // Add filter JOIN based on table type
        var filterJoin = GetFilterJoin(parsed);
        if (!string.IsNullOrEmpty(filterJoin))
            sb.Append($"\n  {filterJoin}");

        return sb.ToString();
    }

    private static string GetFilterJoin(ParsedScript parsed)
    {
        return parsed.Filter switch
        {
            FilterType.Course => "INNER JOIN #srcCourseFilter CF ON CF.MaKH = S.[MaKH]",
            FilterType.MaBCI => "INNER JOIN #srcMaBCIFilter CF ON CF.MaBCI = S.[MaBCI]",
            FilterType.MaDK => "INNER JOIN #srcMaDKFilter CF ON CF.MaDK = S.[MaDK]",
            FilterType.MaKySH => "INNER JOIN #srcMaKySHFilter CF ON CF.MaKySH = S.[MaKySH]",
            FilterType.CourseOnMaTTSH => "INNER JOIN #srcCourseFilter CF ON CF.MaKH = S.[MaTTSH]",
            FilterType.CourseOnMaKhoaHoc => "INNER JOIN #srcCourseFilter CF ON CF.MaKH = S.[MaKhoaHoc]",
            _ => ""
        };
    }

    private async Task<HashSet<string>> GetExistingKeysAsync(string tableName, string[] keyColumns)
    {
        var keys = new HashSet<string>();
        using var conn = new SqlConnection(_dstConnStr);
        await conn.OpenAsync();
        var cols = string.Join(", ", keyColumns.Select(k => $"[{k}]"));
        using var cmd = new SqlCommand($"SELECT {cols} FROM [{_dstDb}].[dbo].[{tableName}]", conn);
        cmd.CommandTimeout = 120;
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var key = string.Join("\t", keyColumns.Select(k => reader[k]?.ToString() ?? ""));
            keys.Add(key);
        }
        return keys;
    }

    private async Task<int> BulkCopyToDestAsync(string tableName, string[] columns, DataTable data)
    {
        using var conn = new SqlConnection(_dstConnStr);
        await conn.OpenAsync();

        using var bulkCopy = new SqlBulkCopy(conn, SqlBulkCopyOptions.TableLock, null)
        {
            DestinationTableName = $"[{_dstDb}].[dbo].[{tableName}]",
            BatchSize = 5000,
            BulkCopyTimeout = 300
        };

        // Map columns
        foreach (var col in columns)
        {
            if (data.Columns.Contains(col))
                bulkCopy.ColumnMappings.Add(col, col);
        }

        await bulkCopy.WriteToServerAsync(data);
        return data.Rows.Count;
    }

    // ── Helpers ──

    private static string BuildInClause(List<string> values)
    {
        if (values.Count == 0) return "''";
        return string.Join(",", values.Select(v => $"'{v.Replace("'", "''")}'"));
    }

    private void Report(string msg) => OnProgress?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");
}

public sealed class ScriptRunResult
{
    public int Total { get; init; }
    public int SuccessCount { get; init; }
    public int FailCount { get; init; }
    public int TotalInserted { get; init; }
    public TimeSpan Elapsed { get; init; }
}
