using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Data.SqlClient;

namespace Gplx.SoApp;

public partial class MainWindow : Window
{
    private static readonly string ScriptsDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\docs\script_syn_so");

    private string SettingsPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "connections.json");

    public MainWindow()
    {
        InitializeComponent();
        LoadSettings();
    }

    // ── Settings ──

    private void SaveSettings()
    {
        var data = new SettingsData
        {
            SrcServer = txtSrcServer.Text,
            SrcUser = txtSrcUser.Text,
            SrcPass = pwdSrcPass.Password,
            SrcDb = txtSrcDb.Text,
            DstServer = txtDstServer.Text,
            DstUser = txtDstUser.Text,
            DstPass = pwdDstPass.Password,
            DstDb = txtDstDb.Text,
            TuNgay = dpTuNgay.SelectedDate?.ToString("o"),
            DenNgay = dpDenNgay.SelectedDate?.ToString("o"),
        };
        try
        {
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var json = File.ReadAllText(SettingsPath);
            var data = JsonSerializer.Deserialize<SettingsData>(json);
            if (data == null) return;
            txtSrcServer.Text = data.SrcServer;
            txtSrcUser.Text = data.SrcUser;
            pwdSrcPass.Password = data.SrcPass;
            txtSrcDb.Text = data.SrcDb;
            txtDstServer.Text = data.DstServer;
            txtDstUser.Text = data.DstUser;
            pwdDstPass.Password = data.DstPass;
            txtDstDb.Text = data.DstDb;
            if (!string.IsNullOrEmpty(data.TuNgay) && DateTime.TryParse(data.TuNgay, out var tu))
                dpTuNgay.SelectedDate = tu;
            if (!string.IsNullOrEmpty(data.DenNgay) && DateTime.TryParse(data.DenNgay, out var den))
                dpDenNgay.SelectedDate = den;
        }
        catch { }
    }

    // ── Get course codes from date range ──

    private async Task<List<string>> GetCourseCodesAsync(string connStr, DateTime from, DateTime to)
    {
        var sql = @"SELECT MaKH FROM KhoaHoc
                    WHERE NgayKG >= @from AND NgayKG <= @to
                    ORDER BY NgayKG";
        var dt = await QueryAsync(connStr, sql,
            new SqlParameter("@from", from),
            new SqlParameter("@to", to));
        return dt.Rows.Cast<DataRow>().Select(r => r["MaKH"].ToString()!).ToList();
    }

    private static string BuildInClause(List<string> values)
    {
        if (values.Count == 0) return "''";
        return string.Join(",", values.Select(v => $"'{v.Replace("'", "''")}'"));
    }

    // ── Preview ──

    private async void BtnPreview_Click(object sender, RoutedEventArgs e)
    {
        var srcServer = txtSrcServer.Text.Trim();
        var srcDb = txtSrcDb.Text.Trim();
        if (string.IsNullOrEmpty(srcServer) || string.IsNullOrEmpty(srcDb))
        {
            MessageBox.Show("Nhập thông tin kết nối nguồn.", "", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (dpTuNgay.SelectedDate == null || dpDenNgay.SelectedDate == null)
        {
            MessageBox.Show("Chọn khoảng ngày đồng bộ.", "", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var connStr = BuildConnString(srcServer, txtSrcUser.Text.Trim(),
            pwdSrcPass.Password, srcDb);

        var from = dpTuNgay.SelectedDate!.Value.Date;
        var to = dpDenNgay.SelectedDate.Value.Date.AddDays(1).AddSeconds(-1);

        AppendLog("--- Xem trước dữ liệu ---");
        AppendLog($"  Từ: {from:dd/MM/yyyy} đến: {to:dd/MM/yyyy}");

        try
        {
            var courseCodes = await GetCourseCodesAsync(connStr, from, to);
            txtCourseCount.Text = $"Tìm thấy {courseCodes.Count} khóa học trong khoảng này.";
            AppendLog($"  Tìm thấy {courseCodes.Count} khóa học.");

            if (courseCodes.Count == 0)
            {
                dgvNguoiLX.ItemsSource = null;
                dgvBaoCaoI.ItemsSource = null;
                dgvBaoCaoII.ItemsSource = null;
                return;
            }

            var inClause = BuildInClause(courseCodes);

            var dtNguoiLX = await QueryAsync(connStr,
                $"SELECT TOP 100 * FROM NguoiLX_HoSo WHERE MaKhoaHoc IN ({inClause}) ORDER BY NgayTao DESC");
            dgvNguoiLX.ItemsSource = dtNguoiLX.DefaultView;
            AppendLog($"  NguoiLX_HoSo: {dtNguoiLX.Rows.Count} bản ghi");

            var dtBaoCaoI = await QueryAsync(connStr,
                $"SELECT TOP 100 * FROM BaoCaoI WHERE MaKH IN ({inClause}) ORDER BY NgayTao DESC");
            dgvBaoCaoI.ItemsSource = dtBaoCaoI.DefaultView;
            AppendLog($"  BaoCaoI: {dtBaoCaoI.Rows.Count} bản ghi");

            var dtBaoCaoII = await QueryAsync(connStr,
                $"SELECT TOP 100 * FROM BaoCaoII WHERE MaBCI IN (SELECT MaBCI FROM BaoCaoI WHERE MaKH IN ({inClause})) ORDER BY NgayTao DESC");
            dgvBaoCaoII.ItemsSource = dtBaoCaoII.DefaultView;
            AppendLog($"  BaoCaoII: {dtBaoCaoII.Rows.Count} bản ghi");

            SaveSettings();
        }
        catch (Exception ex)
        {
            AppendLog($"  LỖI: {ex.Message}");
        }
    }

    private static async Task<DataTable> QueryAsync(string connStr, string sql, params SqlParameter[] parameters)
    {
        var dt = new DataTable();
        using var conn = new SqlConnection(connStr);
        using var cmd = new SqlCommand(sql, conn);
        if (parameters.Length > 0)
            cmd.Parameters.AddRange(parameters);
        cmd.CommandTimeout = 120;
        using var da = new SqlDataAdapter(cmd);
        await Task.Run(() => da.Fill(dt));
        return dt;
    }

    // ── Sync ──

    private async void BtnSync_Click(object sender, RoutedEventArgs e)
    {
        var sw = Stopwatch.StartNew();

        var dstServer = txtDstServer.Text.Trim();
        var dstDb = txtDstDb.Text.Trim();
        if (string.IsNullOrEmpty(dstServer) || string.IsNullOrEmpty(dstDb))
        {
            MessageBox.Show("Nhập thông tin kết nối đích.", "", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var srcServer = txtSrcServer.Text.Trim();
        var srcDb = txtSrcDb.Text.Trim();
        if (string.IsNullOrEmpty(srcServer) || string.IsNullOrEmpty(srcDb))
        {
            MessageBox.Show("Nhập thông tin kết nối nguồn.", "", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (dpTuNgay.SelectedDate == null || dpDenNgay.SelectedDate == null)
        {
            MessageBox.Show("Chọn khoảng ngày đồng bộ.", "", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dstConnStr = BuildConnString(dstServer, txtDstUser.Text.Trim(),
            pwdDstPass.Password, dstDb);
        var srcConnStr = BuildConnString(srcServer, txtSrcUser.Text.Trim(),
            pwdSrcPass.Password, srcDb);
        var newSoCode = txtNewSoCode.Text.Trim();

        var from = dpTuNgay.SelectedDate!.Value.Date;
        var to = dpDenNgay.SelectedDate.Value.Date.AddDays(1).AddSeconds(-1);

        SetUI(false);
        AppendLog("=== Đồng bộ DB Sở ===");
        AppendLog($"  Server đích: {dstServer}, DB nguồn: {srcDb}, DB đích: {dstDb}");
        AppendLog($"  Từ: {from:dd/MM/yyyy} đến: {to:dd/MM/yyyy}");
        if (!string.IsNullOrEmpty(newSoCode))
            AppendLog($"  Mã Sở mới: {newSoCode}");

        try
        {
            AppendLog("  Đang tìm khóa học...");
            var courseCodes = await GetCourseCodesAsync(srcConnStr, from, to);
            txtCourseCount.Text = $"Tìm thấy {courseCodes.Count} khóa học trong khoảng này.";
            AppendLog($"  Tìm thấy {courseCodes.Count} khóa học.");

            if (courseCodes.Count == 0)
            {
                AppendLog("Không có khóa học nào trong khoảng ngày đã chọn.");
                return;
            }

            AppendLog($"  Mã khóa học: {string.Join(", ", courseCodes.Take(10))}" +
                       (courseCodes.Count > 10 ? $"... (+{courseCodes.Count - 10} nữa)" : ""));

            SaveSettings();

            var runner = new ScriptRunner(ScriptsDir, srcDb, dstDb, srcConnStr, dstConnStr,
                newSoCode: string.IsNullOrEmpty(newSoCode) ? null : newSoCode);
            runner.OnProgress += m => AppendLog(m);

            var result = await runner.RunAllAsync();
            sw.Stop();
            AppendLog($"=== Hoàn tất: {result.SuccessCount}/{result.Total} script, +{result.TotalInserted} bản ghi, {result.FailCount} lỗi ({sw.Elapsed.TotalSeconds:F1}s) ===");
        }
        catch (Exception ex)
        {
            sw.Stop();
            AppendLog($"LỖI: {ex.Message}");
        }
        finally { SetUI(true); }
    }

    // ── Connection helpers ──

    private static string BuildConnString(string server, string user, string pass, string database)
    {
        var userPass = string.IsNullOrEmpty(user)
            ? "Trusted_Connection=true;"
            : $"User ID={user};Password={pass};";
        return $"Server={server};Database={database};{userPass}TrustServerCertificate=true;Connection Timeout=30;";
    }

    private async void BtnTestSrc_Click(object sender, RoutedEventArgs e)
    {
        var conn = BuildConnString(txtSrcServer.Text.Trim(), txtSrcUser.Text.Trim(),
            pwdSrcPass.Password, txtSrcDb.Text.Trim());
        await TestConnection(conn, "Nguồn", txtSrcStatus);
    }

    private async void BtnTestDst_Click(object sender, RoutedEventArgs e)
    {
        var conn = BuildConnString(txtDstServer.Text.Trim(), txtDstUser.Text.Trim(),
            pwdDstPass.Password, txtDstDb.Text.Trim());
        await TestConnection(conn, "Đích", txtDstStatus);
    }

    private async Task TestConnection(string connStr, string label, System.Windows.Controls.TextBlock statusBlock)
    {
        try
        {
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            statusBlock.Text = "✓";
            statusBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x80, 0x00));
            SaveSettings();
        }
        catch (Exception ex)
        {
            statusBlock.Text = "✗";
            statusBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x00));
            MessageBox.Show($"Kết nối {label} thất bại: {ex.Message}", "", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Helpers ──

    private void AppendLog(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => AppendLog(message));
            return;
        }
        txtLog.AppendText(message + Environment.NewLine);
        txtLog.ScrollToEnd();
        txtStatus.Text = message;
    }

    private void SetUI(bool enabled)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => SetUI(enabled)); return; }
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
        txtOldSoCode.IsEnabled = enabled;
        txtNewSoCode.IsEnabled = enabled;
        dpTuNgay.IsEnabled = enabled;
        dpDenNgay.IsEnabled = enabled;
        btnPreview.IsEnabled = enabled;
        btnSync.IsEnabled = enabled;
    }

    private sealed class SettingsData
    {
        public string SrcServer { get; set; } = "";
        public string SrcUser { get; set; } = "";
        public string SrcPass { get; set; } = "";
        public string SrcDb { get; set; } = "";
        public string DstServer { get; set; } = "";
        public string DstUser { get; set; } = "";
        public string DstPass { get; set; } = "";
        public string DstDb { get; set; } = "";
        public string? TuNgay { get; set; }
        public string? DenNgay { get; set; }
    }
}
