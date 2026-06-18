using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Data.SqlClient;

namespace Gplx.SoApp;

public partial class MainWindow : Window
{
    private static readonly string ScriptsDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\docs\script_syn_so");

    public MainWindow()
    {
        InitializeComponent();
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

        var connStr = BuildConnString(srcServer, txtSrcUser.Text.Trim(),
            pwdSrcPass.Password, srcDb);
        var courseCode = txtCourseCode.Text.Trim();

        AppendLog("--- Xem trước dữ liệu ---");

        var sqlNguoiLX = string.IsNullOrEmpty(courseCode)
            ? "SELECT TOP 100 * FROM NguoiLX ORDER BY NgayTao DESC"
            : "SELECT * FROM NguoiLX WHERE MaDK IN (SELECT MaDK FROM NguoiLX_HoSo WHERE MaKhoaHoc = @p) ORDER BY NgayTao DESC";

        var sqlBaoCaoI = string.IsNullOrEmpty(courseCode)
            ? "SELECT TOP 100 * FROM BaoCaoI ORDER BY NgayTao DESC"
            : "SELECT * FROM BaoCaoI WHERE MaKH = @p ORDER BY NgayTao DESC";

        var sqlBaoCaoII = string.IsNullOrEmpty(courseCode)
            ? "SELECT TOP 100 * FROM BaoCaoII ORDER BY NgayTao DESC"
            : "SELECT bc2.* FROM BaoCaoII bc2 JOIN BaoCaoI bc1 ON bc2.MaBCI = bc1.MaBCI WHERE bc1.MaKH = @p ORDER BY bc2.NgayTao DESC";

        try
        {
            var dtNguoiLX = await QueryAsync(connStr, sqlNguoiLX, courseCode);
            dgvNguoiLX.ItemsSource = dtNguoiLX.DefaultView;
            AppendLog($"  NguoiLX: {dtNguoiLX.Rows.Count} bản ghi");

            var dtBaoCaoI = await QueryAsync(connStr, sqlBaoCaoI, courseCode);
            dgvBaoCaoI.ItemsSource = dtBaoCaoI.DefaultView;
            AppendLog($"  BaoCaoI: {dtBaoCaoI.Rows.Count} bản ghi");

            var dtBaoCaoII = await QueryAsync(connStr, sqlBaoCaoII, courseCode);
            dgvBaoCaoII.ItemsSource = dtBaoCaoII.DefaultView;
            AppendLog($"  BaoCaoII: {dtBaoCaoII.Rows.Count} bản ghi");
        }
        catch (Exception ex)
        {
            AppendLog($"  LỖI: {ex.Message}");
        }
    }

    private static async Task<DataTable> QueryAsync(string connStr, string sql, string? param = null)
    {
        var dt = new DataTable();
        using var conn = new SqlConnection(connStr);
        using var cmd = new SqlCommand(sql, conn);
        if (!string.IsNullOrEmpty(param))
            cmd.Parameters.AddWithValue("@p", param);
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

        var srcDb = txtSrcDb.Text.Trim();
        if (string.IsNullOrEmpty(srcDb))
        {
            MessageBox.Show("Nhập tên DB nguồn.", "", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dstConnStr = BuildConnString(dstServer, txtDstUser.Text.Trim(),
            pwdDstPass.Password, dstDb);
        var courseCode = txtCourseCode.Text.Trim();
        var newSoCode = txtNewSoCode.Text.Trim();

        SetUI(false);
        AppendLog("=== Đồng bộ DB Sở ===");
        AppendLog($"  Server đích: {dstServer}, DB nguồn: {srcDb}, DB đích: {dstDb}");
        if (!string.IsNullOrEmpty(courseCode))
            AppendLog($"  Khóa học: {courseCode}");
        if (!string.IsNullOrEmpty(newSoCode))
            AppendLog($"  Mã Sở mới: {newSoCode}");

        try
        {
            var runner = new ScriptRunner(ScriptsDir, srcDb, dstDb, dstConnStr,
                courseCode: string.IsNullOrEmpty(courseCode) ? null : courseCode,
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

    private static async Task TestConnection(string connStr, string label)
    {
        try
        {
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            MessageBox.Show($"Kết nối {label} thành công!", "", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Kết nối {label} thất bại: {ex.Message}", "", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Save / Load ──

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
        btnCopyToDst.IsEnabled = enabled;
        btnCopyToSrc.IsEnabled = enabled;
        btnSaveConn.IsEnabled = enabled;
        btnLoadConn.IsEnabled = enabled;
        txtOldSoCode.IsEnabled = enabled;
        txtNewSoCode.IsEnabled = enabled;
        txtCourseCode.IsEnabled = enabled;
        btnPreview.IsEnabled = enabled;
        btnSync.IsEnabled = enabled;
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
