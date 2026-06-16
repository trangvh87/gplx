using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.SqlClient;
using Gplx.WpfApp.DbSync;


namespace Gplx.WpfApp;

public partial class MainWindow : Window
{
    private static readonly Dictionary<string, List<string>> TableKeys = new()
    {
        ["DM_DonViGTVT"] = ["MaDV"],
        ["DM_DVHC"] = ["MaDvhc", "MaDVQL"],
        ["DM_HangDT"] = ["MaHangDT"],
        ["KhoaHoc"] = ["MaKH"],
        ["BaoCaoI"] = ["MaBCI"],
        ["BaoCaoII"] = ["MaBCII"],
        ["GiaoVien"] = ["MaGV"],
        ["LichHoc"] = ["MaLichHoc"],
        ["NguoiLX"] = ["MaDK"],
        ["NguoiLX_HoSo"] = ["MaDK"],
        ["NguoiLXHS_GiayTo"] = ["MaGT", "MaDK"],
        ["NguoiLX_GPLX"] = ["MaDK"],
        ["XeTap"] = ["BienSoXe"]
    };

    private sealed class TableItem
    {
        public string Name { get; set; } = "";
        public bool IsChecked { get; set; } = true;
    }

    private sealed class ScriptItem
    {
        public string FilePath { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string TargetTable { get; set; } = "";
        public bool IsChecked { get; set; } = true;
    }

    private static readonly string ScriptsDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\docs\script_syn_so");

    public MainWindow()
    {
        InitializeComponent();
        InitializeTables();
        InitializeScripts();
    }

    private void InitializeTables()
    {
        var names = new[]
        {
            "DM_DonViGTVT", "DM_DVHC", "DM_HangDT", "KhoaHoc",
            "BaoCaoI", "BaoCaoII", "GiaoVien", "LichHoc",
            "NguoiLX", "NguoiLX_HoSo", "NguoiLXHS_GiayTo",
            "NguoiLX_GPLX", "XeTap"
        };
        foreach (var n in names)
            lvTables.Items.Add(new TableItem { Name = n, IsChecked = true });
    }

    private void InitializeScripts()
    {
        var dir = new DirectoryInfo(ScriptsDir);
        if (!dir.Exists) return;

        foreach (var file in dir.GetFiles("*.sql").OrderBy(f => f.Name))
        {
            if (file.Name.Contains("Fail")) continue;

            var name = Path.GetFileNameWithoutExtension(file.Name);
            var table = name.Contains(']')
                ? name[(name.IndexOf('[') + 1)..name.IndexOf(']')]
                : name;

            lvScripts.Items.Add(new ScriptItem
            {
                FilePath = file.FullName,
                DisplayName = file.Name,
                TargetTable = table,
                IsChecked = true
            });
        }
    }

    // ── DB Sở Sync ──

    private static string BuildSoConnString(string server, string user, string pass)
    {
        return string.IsNullOrEmpty(user)
            ? $"Server={server};Trusted_Connection=true;TrustServerCertificate=true;Connection Timeout=30;"
            : $"Server={server};User ID={user};Password={pass};TrustServerCertificate=true;Connection Timeout=30;";
    }

    private async void BtnSoSync_Click(object sender, RoutedEventArgs e)
    {
        var sw = Stopwatch.StartNew();

        var dstServer = txtSoDstServer.Text.Trim();
        if (string.IsNullOrEmpty(dstServer))
        {
            MessageBox.Show("Nhập server đích.", "", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var srcDb = txtSoSrcDb.Text.Trim();
        var dstDb = txtSoDstDb.Text.Trim();
        if (string.IsNullOrEmpty(srcDb) || string.IsNullOrEmpty(dstDb))
        {
            MessageBox.Show("Nhập DB nguồn và DB đích.", "", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var scripts = lvScripts.Items.OfType<ScriptItem>().Where(s => s.IsChecked).ToList();
        if (scripts.Count == 0)
        {
            MessageBox.Show("Chọn ít nhất một script.", "", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dstConnStr = BuildSoConnString(dstServer, txtSoDstUser.Text.Trim(), pwdSoDstPass.Password);

        SetSoUI(false);
        AppendLog("--- Đồng bộ DB Sở ---");
        AppendLog($"  Server đích: {dstServer}, DB nguồn: {srcDb}, DB đích: {dstDb}");

        var totalInserted = 0;
        var ok = 0;
        var fail = 0;

        foreach (var script in scripts)
        {
            AppendLog($"  Đang chạy: {script.DisplayName}");

            try
            {
                var sql = await File.ReadAllTextAsync(script.FilePath);
                sql = sql.Replace("GPLX_SOGTVT", srcDb)
                         .Replace("GPLX_CDB_SOXAYDUNG_v2", dstDb);

                var batches = SplitSqlBatches(sql);

                await using var conn = new SqlConnection(dstConnStr);
                await conn.OpenAsync();

                var inserted = 0;
                foreach (var batch in batches)
                {
                    var trimmed = batch.Trim();
                    if (trimmed.Length == 0) continue;

                    await using var cmd = new SqlCommand(trimmed, conn);
                    cmd.CommandTimeout = 120;
                    inserted += await cmd.ExecuteNonQueryAsync();
                }

                totalInserted += inserted;
                ok++;
                AppendLog($"    +{inserted} bản ghi");
            }
            catch (Exception ex)
            {
                fail++;
                AppendLog($"    LỖI: {ex.Message}");
            }
        }

        sw.Stop();
        AppendLog($"  Hoàn tất: {ok}/{scripts.Count} bảng, +{totalInserted} bản ghi, {fail} lỗi ({sw.Elapsed.TotalSeconds:F1}s)");
        SetSoUI(true);
    }

    private async void BtnSoTestSrc_Click(object sender, RoutedEventArgs e)
    {
        var connStr = BuildSoConnString(txtSoSrcServer.Text.Trim(), txtSoSrcUser.Text.Trim(), pwdSoSrcPass.Password);
        await TestConnection(connStr, "Nguồn (Sở)");
    }

    private async void BtnSoTestDst_Click(object sender, RoutedEventArgs e)
    {
        var connStr = BuildSoConnString(txtSoDstServer.Text.Trim(), txtSoDstUser.Text.Trim(), pwdSoDstPass.Password);
        await TestConnection(connStr, "Đích (Sở)");
    }

    private void BtnSoCopyToDst_Click(object sender, RoutedEventArgs e)
    {
        txtSoDstServer.Text = txtSoSrcServer.Text;
        txtSoDstUser.Text = txtSoSrcUser.Text;
        pwdSoDstPass.Password = pwdSoSrcPass.Password;
        txtSoDstDb.Text = txtSoSrcDb.Text;
    }

    private void BtnSoCopyToSrc_Click(object sender, RoutedEventArgs e)
    {
        txtSoSrcServer.Text = txtSoDstServer.Text;
        txtSoSrcUser.Text = txtSoDstUser.Text;
        pwdSoSrcPass.Password = pwdSoDstPass.Password;
        txtSoSrcDb.Text = txtSoDstDb.Text;
    }

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

    // ── DB Sync ──

    private async void BtnDbSync_Click(object sender, RoutedEventArgs e)
    {
        var sw = Stopwatch.StartNew();

        if (!int.TryParse(txtBatchSize.Text.Trim(), out var batchSize) || batchSize < 1000)
        {
            MessageBox.Show("Batch size phải >= 1000.", "", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var srcConn = BuildConnString(txtSrcServer.Text.Trim(), txtSrcUser.Text.Trim(),
            pwdSrcPass.Password, txtSrcDb.Text.Trim());
        var dstConn = BuildConnString(txtDstServer.Text.Trim(), txtDstUser.Text.Trim(),
            pwdDstPass.Password, txtDstDb.Text.Trim());

        var tables = new List<SyncTableConfig>();
        foreach (TableItem? item in lvTables.Items)
        {
            if (item == null || !item.IsChecked) continue;
            tables.Add(new SyncTableConfig
            {
                SourceTable = item.Name,
                DestTable = item.Name,
                KeyColumns = TableKeys.GetValueOrDefault(item.Name, [])
            });
        }

        if (tables.Count == 0)
        {
            MessageBox.Show("Chọn ít nhất một bảng.", "", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var engine = new TableSyncEngine(srcConn, dstConn, tables, batchSize);
        engine.OnProgress += m => AppendLog(m);

        SetDbUI(false);
        AppendLog("--- Đồng bộ DB ---");

        try
        {
            var results = await engine.RunAllAsync();
            sw.Stop();
            var ok = results.Count(r => r.Success);
            var fail = results.Count(r => !r.Success);
            var total = results.Sum(r => r.InsertedCount);
            AppendLog($"Hoàn tất: {ok}/{results.Count} bảng, +{total} bản ghi, {fail} lỗi ({sw.Elapsed.TotalSeconds:F1}s)");
        }
        catch (Exception ex)
        {
            sw.Stop();
            AppendLog($"LỖI: {ex.Message}");
        }
        finally { SetDbUI(true); }
    }

    private async void BtnTestSrc_Click(object sender, RoutedEventArgs e)
    {
        var conn = BuildConnString(txtSrcServer.Text.Trim(), txtSrcUser.Text.Trim(),
            pwdSrcPass.Password, txtSrcDb.Text.Trim());
        await TestConnection(conn, "Nguồn");
    }

    private async void BtnTestDst_Click(object sender, RoutedEventArgs e)
    {
        var conn = BuildConnString(txtDstServer.Text.Trim(), txtDstUser.Text.Trim(),
            pwdDstPass.Password, txtDstDb.Text.Trim());
        await TestConnection(conn, "Đích");
    }

    private void BtnCopyToDst_Click(object sender, RoutedEventArgs e)
    {
        txtDstServer.Text = txtSrcServer.Text;
        txtDstUser.Text = txtSrcUser.Text;
        pwdDstPass.Password = pwdSrcPass.Password;
        txtDstDb.Text = txtSrcDb.Text;
    }

    private void BtnCopyToSrc_Click(object sender, RoutedEventArgs e)
    {
        txtSrcServer.Text = txtDstServer.Text;
        txtSrcUser.Text = txtDstUser.Text;
        pwdSrcPass.Password = pwdDstPass.Password;
        txtSrcDb.Text = txtDstDb.Text;
    }

    private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
    {
        SetAllChecked(true);
    }

    private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
    {
        SetAllChecked(false);
    }

    private void BtnSaveConn_Click(object sender, RoutedEventArgs e)
    {
        var data = new ConnectionData
        {
            SrcServer = txtSrcServer.Text,
            SrcUser = txtSrcUser.Text,
            SrcPass = pwdSrcPass.Password,
            SrcDb = txtSrcDb.Text,
            DstServer = txtDstServer.Text,
            DstUser = txtDstUser.Text,
            DstPass = pwdDstPass.Password,
            DstDb = txtDstDb.Text
        };
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "connections.json");
        File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        AppendLog("Đã lưu cấu hình kết nối.");
    }

    private void BtnLoadConn_Click(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "connections.json");
        if (!File.Exists(path))
        {
            MessageBox.Show("Không tìm thấy tập tin cấu hình.", "", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var data = JsonSerializer.Deserialize<ConnectionData>(File.ReadAllText(path));
        if (data == null) return;
        txtSrcServer.Text = data.SrcServer;
        txtSrcUser.Text = data.SrcUser;
        pwdSrcPass.Password = data.SrcPass;
        txtSrcDb.Text = data.SrcDb;
        txtDstServer.Text = data.DstServer;
        txtDstUser.Text = data.DstUser;
        pwdDstPass.Password = data.DstPass;
        txtDstDb.Text = data.DstDb;
        AppendLog("Đã tải cấu hình kết nối.");
    }

    // ── Helpers ──

    private static string BuildConnString(string server, string user, string pass, string database)
    {
        var userPass = string.IsNullOrEmpty(user)
            ? "Trusted_Connection=true;"
            : $"User ID={user};Password={pass};";
        return $"Server={server};Database={database};{userPass}TrustServerCertificate=true;Connection Timeout=30;";
    }

    private static async Task TestConnection(string connStr, string label)
    {
        try
        {
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            MessageBox.Show($"Kết nối {label} thành công!", "", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Kết nối {label} thất bại: {ex.Message}", "", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AppendLog(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => AppendLog(message));
            return;
        }
        txtLog.AppendText(message + Environment.NewLine);
        txtLog.ScrollToEnd();
    }

    private void SetSoUI(bool enabled)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => SetSoUI(enabled)); return; }
        txtSoSrcServer.IsEnabled = enabled;
        txtSoSrcUser.IsEnabled = enabled;
        pwdSoSrcPass.IsEnabled = enabled;
        txtSoSrcDb.IsEnabled = enabled;
        txtSoDstServer.IsEnabled = enabled;
        txtSoDstUser.IsEnabled = enabled;
        pwdSoDstPass.IsEnabled = enabled;
        txtSoDstDb.IsEnabled = enabled;
        btnSoTestSrc.IsEnabled = enabled;
        btnSoTestDst.IsEnabled = enabled;
        btnSoCopyToDst.IsEnabled = enabled;
        btnSoCopyToSrc.IsEnabled = enabled;
        lvScripts.IsEnabled = enabled;
        btnSoSync.IsEnabled = enabled;
    }

    private void SetDbUI(bool enabled)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => SetDbUI(enabled)); return; }
        txtSrcServer.IsEnabled = enabled;
        txtSrcUser.IsEnabled = enabled;
        pwdSrcPass.IsEnabled = enabled;
        txtSrcDb.IsEnabled = enabled;
        txtDstServer.IsEnabled = enabled;
        txtDstUser.IsEnabled = enabled;
        pwdDstPass.IsEnabled = enabled;
        txtDstDb.IsEnabled = enabled;
        btnTestSrc.IsEnabled = enabled;
        btnTestDst.IsEnabled = enabled;
        btnCopyToDst.IsEnabled = enabled;
        btnCopyToSrc.IsEnabled = enabled;
        btnSaveConn.IsEnabled = enabled;
        btnLoadConn.IsEnabled = enabled;
        lvTables.IsEnabled = enabled;
        btnSelectAll.IsEnabled = enabled;
        btnDeselectAll.IsEnabled = enabled;
        txtBatchSize.IsEnabled = enabled;
        btnDbSync.IsEnabled = enabled;
    }

    private void SetAllChecked(bool check)
    {
        foreach (TableItem? item in lvTables.Items)
            if (item != null) item.IsChecked = check;
        lvTables.Items.Refresh();
    }

    private sealed class ConnectionData
    {
        public string SrcServer { get; set; } = "";
        public string SrcUser { get; set; } = "";
        public string SrcPass { get; set; } = "";
        public string SrcDb { get; set; } = "";
        public string DstServer { get; set; } = "";
        public string DstUser { get; set; } = "";
        public string DstPass { get; set; } = "";
        public string DstDb { get; set; } = "";
    }
}
