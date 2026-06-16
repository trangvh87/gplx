using Microsoft.Data.SqlClient;
using Gplx.SyncApp.DbSync;
using Gplx.SyncApp.Sync;

namespace Gplx.SyncApp;

public partial class Form1 : Form
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

    public Form1()
    {
        InitializeComponent();
    }

    // ── File Sync ──

    private async void BtnFileSync_Click(object sender, EventArgs e)
    {
        var src = txtSource.Text.Trim();
        var dst = txtDestination.Text.Trim();

        if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dst))
        {
            MessageBox.Show("Vui lòng chọn thư mục nguồn và đích.", "", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!Directory.Exists(src))
        {
            MessageBox.Show("Thư mục nguồn không tồn tại.", "", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (!Directory.Exists(dst)) Directory.CreateDirectory(dst);

        var engine = new SyncEngine(new FileSyncSource(src), new FileSyncDestination(dst));
        engine.OnProgress += m => AppendLog(m);

        SetFileUI(false);
        AppendLog("--- Đồng bộ file ---");

        try
        {
            var r = await engine.RunAsync();
            AppendLog($"Hoàn tất: +{r.Added} ~{r.Updated} !{r.Errors}");
        }
        catch (Exception ex)
        {
            AppendLog($"LỖI: {ex.Message}");
            MessageBox.Show(ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetFileUI(true); }
    }

    private void BtnBrowseSource_Click(object sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog();
        if (dlg.ShowDialog() == DialogResult.OK) txtSource.Text = dlg.SelectedPath;
    }

    private void BtnBrowseDest_Click(object sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog();
        if (dlg.ShowDialog() == DialogResult.OK) txtDestination.Text = dlg.SelectedPath;
    }

    // ── DB Sync ──

    private async void BtnDbSync_Click(object sender, EventArgs e)
    {
        var srcConn = BuildConnString(txtSrcServer.Text.Trim(), txtSrcUser.Text.Trim(),
            txtSrcPass.Text, txtSrcDb.Text.Trim());
        var dstConn = BuildConnString(txtDstServer.Text.Trim(), txtDstUser.Text.Trim(),
            txtDstPass.Text, txtDstDb.Text.Trim());

        var tables = new List<SyncTableConfig>();
        foreach (var item in clbTables.CheckedItems)
        {
            var name = item.ToString()!;
            tables.Add(new SyncTableConfig
            {
                SourceTable = name,
                DestTable = name,
                KeyColumns = TableKeys.GetValueOrDefault(name, [])
            });
        }

        if (tables.Count == 0)
        {
            MessageBox.Show("Chọn ít nhất một bảng.", "", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var engine = new TableSyncEngine(srcConn, dstConn, tables, (int)numBatchSize.Value);
        engine.OnProgress += m => AppendLog(m);

        SetDbUI(false);
        AppendLog("--- Đồng bộ DB ---");

        try
        {
            var results = await engine.RunAllAsync();
            var ok = results.Count(r => r.Success);
            var fail = results.Count(r => !r.Success);
            var total = results.Sum(r => r.InsertedCount);
            AppendLog($"Hoàn tất: {ok}/{results.Count} bảng, +{total} bản ghi, {fail} lỗi");
        }
        catch (Exception ex)
        {
            AppendLog($"LỖI: {ex.Message}");
        }
        finally { SetDbUI(true); }
    }

    private async void BtnTestSrc_Click(object sender, EventArgs e)
    {
        var conn = BuildConnString(txtSrcServer.Text.Trim(), txtSrcUser.Text.Trim(),
            txtSrcPass.Text, txtSrcDb.Text.Trim());
        await TestConnection(conn, "Nguồn");
    }

    private async void BtnTestDst_Click(object sender, EventArgs e)
    {
        var conn = BuildConnString(txtDstServer.Text.Trim(), txtDstUser.Text.Trim(),
            txtDstPass.Text, txtDstDb.Text.Trim());
        await TestConnection(conn, "Đích");
    }

    private static string BuildConnString(string server, string user, string pass, string database)
    {
        var userPass = string.IsNullOrEmpty(user)
            ? "Trusted_Connection=true;"
            : $"User ID={user};Password={pass};";
        return $"Server={server};Database={database};{userPass}TrustServerCertificate=true;Connection Timeout=30;";
    }

    private void BtnCopyToDst_Click(object sender, EventArgs e)
    {
        txtDstServer.Text = txtSrcServer.Text;
        txtDstUser.Text = txtSrcUser.Text;
        txtDstPass.Text = txtSrcPass.Text;
        txtDstDb.Text = txtSrcDb.Text;
    }

    private void BtnCopyToSrc_Click(object sender, EventArgs e)
    {
        txtSrcServer.Text = txtDstServer.Text;
        txtSrcUser.Text = txtDstUser.Text;
        txtSrcPass.Text = txtDstPass.Text;
        txtSrcDb.Text = txtDstDb.Text;
    }

    private void BtnSaveConn_Click(object sender, EventArgs e)
    {
        var data = new ConnectionData
        {
            SrcServer = txtSrcServer.Text,
            SrcUser = txtSrcUser.Text,
            SrcPass = txtSrcPass.Text,
            SrcDb = txtSrcDb.Text,
            DstServer = txtDstServer.Text,
            DstUser = txtDstUser.Text,
            DstPass = txtDstPass.Text,
            DstDb = txtDstDb.Text
        };
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "connections.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        AppendLog("Đã lưu cấu hình kết nối.");
    }

    private void BtnLoadConn_Click(object sender, EventArgs e)
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "connections.json");
        if (!File.Exists(path))
        {
            MessageBox.Show("Không tìm thấy tập tin cấu hình.", "", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var data = System.Text.Json.JsonSerializer.Deserialize<ConnectionData>(File.ReadAllText(path));
        if (data == null) return;
        txtSrcServer.Text = data.SrcServer;
        txtSrcUser.Text = data.SrcUser;
        txtSrcPass.Text = data.SrcPass;
        txtSrcDb.Text = data.SrcDb;
        txtDstServer.Text = data.DstServer;
        txtDstUser.Text = data.DstUser;
        txtDstPass.Text = data.DstPass;
        txtDstDb.Text = data.DstDb;
        AppendLog("Đã tải cấu hình kết nối.");
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

    private static async Task TestConnection(string connStr, string label)
    {
        try
        {
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            MessageBox.Show($"Kết nối {label} thành công!", "", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Kết nối {label} thất bại: {ex.Message}", "", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Helpers ──

    private void AppendLog(string message)
    {
        if (txtLog.InvokeRequired)
        {
            txtLog.Invoke(() => AppendLog(message));
            return;
        }
        txtLog.AppendText(message + Environment.NewLine);
        txtLog.ScrollToCaret();
    }

    private void SetFileUI(bool enabled)
    {
        if (InvokeRequired) { Invoke(() => SetFileUI(enabled)); return; }
        txtSource.Enabled = enabled;
        txtDestination.Enabled = enabled;
        btnBrowseSource.Enabled = enabled;
        btnBrowseDest.Enabled = enabled;
        btnFileSync.Enabled = enabled;
    }

    private void SetDbUI(bool enabled)
    {
        if (InvokeRequired) { Invoke(() => SetDbUI(enabled)); return; }
        txtSrcServer.Enabled = enabled;
        txtSrcUser.Enabled = enabled;
        txtSrcPass.Enabled = enabled;
        txtSrcDb.Enabled = enabled;
        txtDstServer.Enabled = enabled;
        txtDstUser.Enabled = enabled;
        txtDstPass.Enabled = enabled;
        txtDstDb.Enabled = enabled;
        btnTestSrc.Enabled = enabled;
        btnTestDst.Enabled = enabled;
        btnCopyToDst.Enabled = enabled;
        btnCopyToSrc.Enabled = enabled;
        btnSaveConn.Enabled = enabled;
        btnLoadConn.Enabled = enabled;
        clbTables.Enabled = enabled;
        btnSelectAll.Enabled = enabled;
        btnDeselectAll.Enabled = enabled;
        numBatchSize.Enabled = enabled;
        btnDbSync.Enabled = enabled;
    }

    private void SetAllChecked(bool check)
    {
        for (int i = 0; i < clbTables.Items.Count; i++)
            clbTables.SetItemChecked(i, check);
    }
}
