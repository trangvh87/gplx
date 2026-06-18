using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Gplx.SoApp;

public delegate void ScriptProgressHandler(string message);

public sealed class ScriptRunner
{
    private readonly string _scriptsDir;
    private readonly string _srcDb;
    private readonly string _dstDb;
    private readonly string _dstConnStr;
    private readonly string? _courseCode;
    private readonly string? _newSoCode;

    public event ScriptProgressHandler? OnProgress;

    public ScriptRunner(string scriptsDir, string srcDb, string dstDb,
        string dstConnStr, string? courseCode = null, string? newSoCode = null)
    {
        _scriptsDir = scriptsDir;
        _srcDb = srcDb;
        _dstDb = dstDb;
        _dstConnStr = dstConnStr;
        _courseCode = courseCode;
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
                sql = sql.Replace("GPLX_SOGTVT", _srcDb)
                         .Replace("GPLX_CDB_SOXAYDUNG_v2", _dstDb);

                if (!string.IsNullOrEmpty(_courseCode))
                    sql = sql.Replace("{CourseCode}", _courseCode);

                if (!string.IsNullOrEmpty(_newSoCode))
                    sql = sql.Replace("{NewSoCode}", _newSoCode);

                var batches = SplitSqlBatches(sql);

                using var conn = new SqlConnection(_dstConnStr);
                await conn.OpenAsync();

                var inserted = 0;
                foreach (var batch in batches)
                {
                    var trimmed = batch.Trim();
                    if (trimmed.Length == 0) continue;

                    using var cmd = new SqlCommand(trimmed, conn);
                    cmd.CommandTimeout = 120;
                    inserted += await cmd.ExecuteNonQueryAsync();
                }

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

    private void Report(string msg) => OnProgress?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");

    private static List<string> SplitSqlBatches(string sql)
    {
        var batches = new List<string>();
        var lines = sql.Split('\n');
        var current = new System.Text.StringBuilder();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                if (current.Length > 0)
                    batches.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.AppendLine(line);
            }
        }

        if (current.Length > 0)
            batches.Add(current.ToString());

        return batches;
    }
}

public sealed class ScriptRunResult
{
    public int Total { get; init; }
    public int SuccessCount { get; init; }
    public int FailCount { get; init; }
    public int TotalInserted { get; init; }
    public TimeSpan Elapsed { get; init; }
}
