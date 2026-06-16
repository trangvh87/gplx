using System.Text.Json;
using Gplx.DbSync.Models;
using Gplx.DbSync.Sync;

var configPath = args.Length > 0 ? args[0] : "appsettings.json";

if (!File.Exists(configPath))
{
    Console.Error.WriteLine($"Không tìm thấy file cấu hình: {configPath}");
    return 1;
}

var json = await File.ReadAllTextAsync(configPath);
var config = JsonSerializer.Deserialize<SyncConfig>(json, new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
});

if (config == null || config.Tables.Count == 0)
{
    Console.Error.WriteLine("File cấu hình không hợp lệ hoặc không có bảng nào để đồng bộ.");
    return 1;
}

Console.WriteLine($"Source: {config.SourceConnectionString}");
Console.WriteLine($"Dest:   {config.DestConnectionString}");
Console.WriteLine($"Bảng cần đồng bộ: {config.Tables.Count}");
Console.WriteLine();

var progress = new Progress<string>(msg => Console.WriteLine(msg));
var engine = new TableSyncEngine(config, progress);

var startTime = DateTime.Now;
var results = await engine.RunAllAsync();
var elapsed = DateTime.Now - startTime;

Console.WriteLine();
Console.WriteLine(new string('=', 50));
Console.WriteLine("=== TỔNG KẾT ===");

var successCount = results.Count(r => r.Success);
var failCount = results.Count(r => !r.Success);
var totalInserted = results.Sum(r => r.InsertedCount);

foreach (var r in results)
{
    var status = r.Success ? "OK" : "LỖI";
    Console.WriteLine($"  [{status}] {r.TableName,-30} +{r.InsertedCount,8} bản ghi ({r.Duration.TotalSeconds,6:F1}s)");
}

Console.WriteLine($"Tổng: {successCount}/{results.Count} bảng thành công, +{totalInserted} bản ghi, {failCount} bảng lỗi");
Console.WriteLine($"Thời gian: {elapsed.TotalMinutes:F1} phút");
return failCount > 0 ? 1 : 0;
