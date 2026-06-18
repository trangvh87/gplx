using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Data.SqlClient;
using Gplx.Core.DbSync;
using Gplx.WpfApp.DbSync;

namespace Gplx.WpfApp;

public partial class MainWindow : Window
{
    private static readonly Dictionary<string, List<string>> TableKeys = new()
    {
        ["DM_DonViGTVT"] = new List<string> { "MaDV" },
        ["DM_DVHC"] = new List<string> { "MaDvhc", "MaDVQL" },
        ["DM_HangDT"] = new List<string> { "MaHangDT" },
        ["KhoaHoc"] = new List<string> { "MaKH" },
        ["BaoCaoI"] = new List<string> { "MaBCI" },
        ["BaoCaoII"] = new List<string> { "MaBCII" },
        ["GiaoVien"] = new List<string> { "MaGV" },
        ["LichHoc"] = new List<string> { "MaLichHoc" },
        ["NguoiLX"] = new List<string> { "MaDK" },
        ["NguoiLX_HoSo"] = new List<string> { "MaDK" },
        ["NguoiLXHS_GiayTo"] = new List<string> { "MaGT", "MaDK" },
        ["NguoiLX_GPLX"] = new List<string> { "MaDK" },
        ["XeTap"] = new List<string> { "BienSoXe" }
    };

    private static readonly string[] AllTableNames =
    {
        "DM_DonViGTVT", "DM_DVHC", "DM_HangDT", "KhoaHoc",
        "BaoCaoI", "BaoCaoII", "GiaoVien", "LichHoc",
        "NguoiLX", "NguoiLX_HoSo", "NguoiLXHS_GiayTo",
        "NguoiLX_GPLX", "XeTap"
    };

    private readonly DispatcherTimer _searchTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(400),
        IsEnabled = false
    };

    // removed auto-save timer: settings are saved only on explicit actions

    private sealed class CourseItem
    {
        public string Value { get; init; } = "";
        public string Display { get; init; } = "";
    }

    private bool _isBusy;
    private string _lastDstConnStr = "";
    private string _lastNewCourseCode = "";

    public MainWindow()
    {
        InitializeComponent();

        _searchTimer.Tick += async (s, e) =>
        {
            _searchTimer.Stop();
            await SearchCourseCodesAsync(cmbCourseCode.Text.Trim());
        };

        // don't hook auto-save: settings are saved explicitly on user actions
        LoadSettings();
        _ = UpdateNewCourseCodeAsync();
        txtLog.PreviewMouseLeftButtonDown += TxtLog_PreviewMouseLeftButtonDown;
    }

    // Auto-save removed: settings are loaded once at startup and saved when
    // the user explicitly performs actions (successful connection tests,
    // Preview and Execute). This avoids unexpected writes while editing.

    private string SettingsPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "connections.json");

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
            OldCsdt = txtOldCsdt.Text,
            NewCsdt = txtNewCsdt.Text,
            OldSoCode = txtOldSoCode.Text,
            NewSoCode = txtNewSoCode.Text,
            CourseCode = GetCourseCode(),
            BatchSize = txtBatchSize.Text,
            CodeMode = GetSelectedCodeMode(),
            NewCourseName = txtNewCourseName.Text,
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
            txtOldCsdt.Text = data.OldCsdt;
            txtNewCsdt.Text = data.NewCsdt;
            txtOldSoCode.Text = data.OldSoCode;
            txtNewSoCode.Text = data.NewSoCode;
            cmbCourseCode.Text = data.CourseCode;
            txtBatchSize.Text = data.BatchSize;
            txtNewCourseName.Text = data.NewCourseName;
            // restore code mode
            try
            {
                if (!string.IsNullOrEmpty(data.CodeMode) && cmbCodeMode != null)
                {
                    for (int i = 0; i < cmbCodeMode.Items.Count; i++)
                    {
                        if (cmbCodeMode.Items[i] is ComboBoxItem it && (it.Content?.ToString() ?? "") == data.CodeMode)
                        {
                            cmbCodeMode.SelectedIndex = i; break;
                        }
                    }
                }
            }
            catch { }
        }
        catch { }
    }

    // ── Preview ──

    private async void BtnPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        var srcServer = txtSrcServer.Text.Trim();
        var srcDb = txtSrcDb.Text.Trim();
        if (string.IsNullOrEmpty(srcServer) || string.IsNullOrEmpty(srcDb))
        {
            MessageBox.Show("Nhập thông tin kết nối nguồn.", "", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var oldCsdt = txtOldCsdt.Text.Trim();
        var mode = GetSelectedCodeMode();
        // If TT17 selected, prefer generated NewCsdt and update the courseCode accordingly
        var newCsdt = (await ResolveNewCsdtForRun(GetCourseCode())) ?? txtNewCsdt.Text.Trim();
        if (mode == "Tạo mới theo thông tư 17" && string.IsNullOrEmpty(newCsdt))
        {
            MessageBox.Show("Không thể sinh mã mới theo Thông tư 17.", "", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var oldSo = txtOldSoCode.Text.Trim();
        var newSo = txtNewSoCode.Text.Trim();
        // Determine which courseCode to use for querying preview data.
        // For preview we must always query the SOURCE DB using the original course (MaKH) selected
        // by the user. TT17 generation only affects what will be used for INSERT into destination,
        // but preview must show source rows tied to the selected MaKH.
        var courseCode = GetCourseCode();
        if (string.IsNullOrEmpty(oldCsdt) || string.IsNullOrEmpty(newCsdt) ||
            string.IsNullOrEmpty(oldSo) || string.IsNullOrEmpty(newSo) ||
            string.IsNullOrEmpty(courseCode))
        {
            MessageBox.Show("Nhập đầy đủ mã CSĐT cũ/mới, mã Sở cũ/mới và mã khóa học.", "",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _isBusy = true;
        Cursor = Cursors.Wait;
        SetActionButtonsEnabled(false);

        var connStr = BuildConnString(srcServer, txtSrcUser.Text.Trim(),
            pwdSrcPass.Password, srcDb);

        AppendLog("--- Xem trước dữ liệu ---");

        // Use the courseCode (which may have been overridden for TT17) for queries
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
            tabNguoiLX.Header = $"Học viên (NguoiLX) - {dtNguoiLX.Rows.Count} bản ghi";
            AppendLog($"  NguoiLX: {dtNguoiLX.Rows.Count} bản ghi");

            var dtBaoCaoI = await QueryAsync(connStr, sqlBaoCaoI, courseCode);
            dgvBaoCaoI.ItemsSource = dtBaoCaoI.DefaultView;
            tabBaoCaoI.Header = $"Báo cáo I (BaoCaoI) - {dtBaoCaoI.Rows.Count} bản ghi";
            AppendLog($"  BaoCaoI: {dtBaoCaoI.Rows.Count} bản ghi");

            var dtBaoCaoII = await QueryAsync(connStr, sqlBaoCaoII, courseCode);
            dgvBaoCaoII.ItemsSource = dtBaoCaoII.DefaultView;
            tabBaoCaoII.Header = $"Báo cáo II (BaoCaoII) - {dtBaoCaoII.Rows.Count} bản ghi";
            AppendLog($"  BaoCaoII: {dtBaoCaoII.Rows.Count} bản ghi");
            // Save settings after a successful preview operation
            try { SaveSettings(); } catch { }
        }
        catch (Exception ex)
        {
            AppendLog($"  LỖI: {ex.Message}");
        }
        finally
        {
            Cursor = Cursors.Arrow;
            SetActionButtonsEnabled(true);
            _isBusy = false;
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

    // ── Course code autocomplete ──

    private string GetCourseCode()
    {
        // Return the raw course code (MaKH) not the display text "MaKH - TenKH".
        if (cmbCourseCode.SelectedItem is CourseItem item)
            return item.Value;
        var text = cmbCourseCode.Text.Trim();
        if (string.IsNullOrEmpty(text)) return text;
        // If user left the display ("MaKH - TenKH"), extract the code part before ' - '
        var parts = text.Split(new[] { " - " }, StringSplitOptions.None);
        return parts.Length > 1 ? parts[0].Trim() : text;
    }

    private async Task UpdateNewCourseCodeAsync()
    {
        var courseCode = GetCourseCode();
        txtExistLink.Visibility = Visibility.Collapsed;
        var mode = GetSelectedCodeMode();
        if (mode == "Giữ nguyên")
        {
            // show the same course code and name as new (no change) and allow edit
            var orig = courseCode ?? "";
            txtNewCourseCode.Text = orig;
            // try to populate name from source KhoaHoc if present
            txtNewCourseName.Text = await FetchCourseNameFromSourceAsync(orig) ?? "";
            return;
        }
        string? newCsdt;
        if (mode == "Thay thế mã csdt")
            newCsdt = txtNewCsdt.Text.Trim();
        else
            newCsdt = await ResolveNewCsdtForRun(courseCode);

        if (!string.IsNullOrEmpty(newCsdt))
        {
            // If TT17 mode, generate a new course suffix (K + YY + NNNN) based on NgayTao and dest DB max + 1
            string suffix;
            if (mode == "Tạo mới theo thông tư 17")
            {
                var gen = await GenerateCourseSuffixAsync(newCsdt, courseCode);
                if (!string.IsNullOrEmpty(gen))
                    suffix = gen;
                else if (!string.IsNullOrEmpty(courseCode) && courseCode.Length > 5)
                    suffix = courseCode.Substring(5);
                else
                    suffix = "";
            }
            else
            {
                if (!string.IsNullOrEmpty(courseCode) && courseCode.Length > 5)
                    suffix = courseCode.Substring(5);
                else
                    suffix = "";
            }
            txtNewCourseCode.Text = $"{newCsdt}{suffix}";
            // prefill course name from source (fallback to empty)
            txtNewCourseName.Text = await FetchCourseNameFromSourceAsync(courseCode) ?? "";
            _ = CheckCourseExistsOnDestAsync(newCsdt + suffix);
        }
        else
        {
            txtNewCourseCode.Text = "";
            txtNewCourseCode.ToolTip = null;
        }
    }

    private async void CmbCodeMode_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Always refresh the preview display
        _ = UpdateNewCourseCodeAsync();

        // If user switched to TT17, try to auto-generate NewCsdt immediately
        if (GetSelectedCodeMode() == "Tạo mới theo thông tư 17")
        {
            try
            {
                var generated = await ResolveNewCsdtForRun(GetCourseCode());
                if (!string.IsNullOrEmpty(generated))
                {
                    txtNewCsdt.Text = generated;
                    AppendLog($"  TT17: Mã CSĐT tự sinh: {generated}");
                    await UpdateNewCourseCodeAsync();
                }
            }
            catch (Exception ex)
            {
                AppendLog($"  LỖI: Sinh mã TT17 thất bại: {ex.Message}");
            }
        }
    }

    private async void BtnGenTT17_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        var mode = GetSelectedCodeMode();
        if (mode != "Tạo mới theo thông tư 17")
        {
            AppendLog("  Lưu ý: Chỉ sinh mã TT17 khi chọn 'Tạo mới theo thông tư 17'.");
            return;
        }
        _isBusy = true;
        Cursor = Cursors.Wait;
        try
        {
            var code = await ResolveNewCsdtForRun(GetCourseCode());
            if (!string.IsNullOrEmpty(code))
            {
                txtNewCsdt.Text = code;
                AppendLog($"  TT17: Mã CSĐT sinh: {code}");
                _ = UpdateNewCourseCodeAsync();
            }
            else
            {
                AppendLog("  LỖI: Không thể sinh mã TT17.");
            }
        }
        finally
        {
            Cursor = Cursors.Arrow;
            _isBusy = false;
        }
    }

    private async Task CheckCourseExistsOnDestAsync(string newCode)
    {
        var dstServer = txtDstServer.Text.Trim();
        var dstDb = txtDstDb.Text.Trim();
        if (string.IsNullOrEmpty(dstServer) || string.IsNullOrEmpty(dstDb)) return;
        var connStr = BuildConnString(dstServer, txtDstUser.Text.Trim(),
            pwdDstPass.Password, dstDb);
        try
        {
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            using var cmd = new SqlCommand("SELECT COUNT(*) FROM KhoaHoc WHERE MaKH = @p", conn);
            cmd.Parameters.AddWithValue("@p", newCode);
            cmd.CommandTimeout = 10;
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            Dispatcher.Invoke(() =>
            {
                txtExistLink.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            });
        }
        catch { }
    }

    private async Task<string?> FetchCourseNameFromSourceAsync(string? courseCode)
    {
        try
        {
            if (string.IsNullOrEmpty(courseCode)) return null;
            var srcServer = txtSrcServer.Text.Trim();
            var srcDb = txtSrcDb.Text.Trim();
            if (string.IsNullOrEmpty(srcServer) || string.IsNullOrEmpty(srcDb)) return null;
            var srcConn = BuildConnString(srcServer, txtSrcUser.Text.Trim(), pwdSrcPass.Password, srcDb);
            using var conn = new SqlConnection(srcConn);
            await conn.OpenAsync();
            using var cmd = new SqlCommand("SELECT TOP 1 ISNULL(TenKH, '') FROM KhoaHoc WHERE MaKH = @p", conn);
            cmd.Parameters.AddWithValue("@p", courseCode);
            cmd.CommandTimeout = 10;
            var res = await cmd.ExecuteScalarAsync();
            if (res != null && res != DBNull.Value)
                return res.ToString();
            return null;
        }
        catch { return null; }
    }

    // Generate course suffix for TT17: 'K' + YY + NNNN, where YY from NgayTao and NNNN is next sequence per NewCsdt+YY
    private async Task<string?> GenerateCourseSuffixAsync(string? newCsdtBase, string? oldCourseCode)
    {
        try
        {
            var dstServer = txtDstServer.Text.Trim();
            var dstDb = txtDstDb.Text.Trim();
            if (string.IsNullOrEmpty(dstServer) || string.IsNullOrEmpty(dstDb)) return null;
            var connStr = BuildConnString(dstServer, txtDstUser.Text.Trim(), pwdDstPass.Password, dstDb);
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            // Determine date to use: NgayTao from existing course if available, else today
            string ymd = DateTime.UtcNow.ToString("yyyyMMdd");
            string yy = DateTime.UtcNow.ToString("yy");
            DateTime? srcDate = null;
            var oldCode = oldCourseCode ?? "";
            if (!string.IsNullOrEmpty(oldCode))
            {
                // try to read NgayTao from source DB KhoaHoc where MaKH = oldCourseCode
                try
                {
                    var srcServer = txtSrcServer.Text.Trim();
                    var srcDb = txtSrcDb.Text.Trim();
                    if (!string.IsNullOrEmpty(srcServer) && !string.IsNullOrEmpty(srcDb))
                    {
                        var srcConn = BuildConnString(srcServer, txtSrcUser.Text.Trim(), pwdSrcPass.Password, srcDb);
                        using var sc = new SqlConnection(srcConn);
                        await sc.OpenAsync();
                        using var cmd = new SqlCommand("SELECT TOP 1 NgayTao FROM KhoaHoc WHERE MaKH = @p", sc);
                        cmd.Parameters.AddWithValue("@p", oldCode);
                        var r = await cmd.ExecuteScalarAsync();
                        if (r != DBNull.Value && r != null)
                        {
                            if (DateTime.TryParse(r.ToString(), out var dt)) srcDate = dt;
                        }
                    }
                }
                catch { }
            }

            if (srcDate.HasValue)
            {
                ymd = srcDate.Value.ToString("yyyyMMdd");
                yy = srcDate.Value.ToString("yy");
            }

            // Compute max existing sequence for this newCsdtBase and year-yy using MaKH pattern: LEFT(MaKH,5)=newCsdtBase AND SUBSTRING(MaKH,6,2)=yy
            using var cmdMax = new SqlCommand(
                "SELECT ISNULL(MAX(TRY_CONVERT(int, RIGHT(MaKH,4))), 0) FROM KhoaHoc WHERE LEFT(ISNULL(MaKH,''),5) = @p AND SUBSTRING(ISNULL(MaKH,''),6,2) = @yy", conn);
            cmdMax.Parameters.AddWithValue("@p", newCsdtBase ?? "");
            cmdMax.Parameters.AddWithValue("@yy", yy);
            cmdMax.CommandTimeout = 10;
            var res = await cmdMax.ExecuteScalarAsync();
            int max = 0;
            if (res != DBNull.Value && res != null) { try { max = Convert.ToInt32(res); } catch { max = 0; } }
            var next = max + 1;
            if (next > 9999) return null;
            var seq = next.ToString("D4");
            return "K" + yy + seq;
        }
        catch (Exception ex)
        {
            AppendLog($"  LỖI: Sinh suffix khóa học TT17 thất bại: {ex.Message}");
            return null;
        }
    }

    private void LnkExist_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        var dstServer = txtDstServer.Text.Trim();
        var dstDb = txtDstDb.Text.Trim();
        var connStr = BuildConnString(dstServer, txtDstUser.Text.Trim(),
            pwdDstPass.Password, dstDb);
        var code = txtNewCourseCode.Text.TrimStart('→', ' ');
        var w = new SyncResultWindow(connStr, code) { Owner = this };
        w.ShowDialog();
        e.Handled = true;
    }

    private void CmbCourseCode_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Up || e.Key == System.Windows.Input.Key.Down ||
            e.Key == System.Windows.Input.Key.Left || e.Key == System.Windows.Input.Key.Right ||
            e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Escape ||
            e.Key == System.Windows.Input.Key.Tab)
            return;

        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void CmbCourseCode_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _searchTimer.Stop();
        _ = UpdateNewCourseCodeAsync();
        if (cmbCourseCode.SelectedItem is CourseItem item)
            cmbCourseCode.Text = item.Display;
    }

    private async void CmbCourseCode_DropDownOpened(object sender, EventArgs e)
    {
        var text = cmbCourseCode.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            _searchTimer.Stop();
            await SearchCourseCodesAsync(null);
            return;
        }
        if (cmbCourseCode.ItemsSource is IList<CourseItem> items
            && items.Count > 0
            && items.Any(i => i.Value.StartsWith(text, StringComparison.OrdinalIgnoreCase)))
            return;

        _searchTimer.Stop();
        await SearchCourseCodesAsync(text);
    }

    private async Task SearchCourseCodesAsync(string? search)
    {
        var srcServer = txtSrcServer.Text.Trim();
        var srcDb = txtSrcDb.Text.Trim();
        if (string.IsNullOrEmpty(srcServer) || string.IsNullOrEmpty(srcDb)) return;

        var connStr = BuildConnString(srcServer, txtSrcUser.Text.Trim(),
            pwdSrcPass.Password, srcDb);

        try
        {
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            var hasNameCol = false;
            using (var chk = new SqlCommand(
                "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'KhoaHoc') AND name = N'TenKH'", conn))
            {
                hasNameCol = Convert.ToInt32(await chk.ExecuteScalarAsync()) > 0;
            }

            var sql = hasNameCol
                ? (search == null
                    ? "SELECT TOP 50 MaKH, ISNULL(TenKH, '') FROM KhoaHoc ORDER BY MaKH DESC"
                    : "SELECT TOP 50 MaKH, ISNULL(TenKH, '') FROM KhoaHoc WHERE MaKH LIKE @p ORDER BY MaKH DESC")
                : (search == null
                    ? "SELECT TOP 50 MaKH FROM KhoaHoc ORDER BY MaKH DESC"
                    : "SELECT TOP 50 MaKH FROM KhoaHoc WHERE MaKH LIKE @p ORDER BY MaKH DESC");
            using var cmd = new SqlCommand(sql, conn);
            if (search != null)
                cmd.Parameters.AddWithValue("@p", $"%{search}%");
            cmd.CommandTimeout = 10;

            var items = new List<CourseItem>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var ma = reader.GetString(0);
                var display = hasNameCol ? $"{ma} - {reader.GetString(1)}" : ma;
                items.Add(new CourseItem { Value = ma, Display = display });
            }

            Dispatcher.Invoke(() =>
            {
                var prevText = cmbCourseCode.Text;
                cmbCourseCode.ItemsSource = items;
                cmbCourseCode.DisplayMemberPath = "Display";
                cmbCourseCode.SelectedValuePath = "Value";
                cmbCourseCode.Text = prevText;
                if (items.Count > 0)
                    cmbCourseCode.IsDropDownOpen = true;
            });
        }
        catch { }
    }

    // ── DB Sync ──

    private async void BtnDbSync_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        var sw = Stopwatch.StartNew();

        if (!int.TryParse(txtBatchSize.Text.Trim(), out var batchSize) || batchSize < 1000)
        {
            MessageBox.Show("Batch size phải >= 1000.", "", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var srcServer = txtSrcServer.Text.Trim();
        var dstServer = txtDstServer.Text.Trim();
        var srcDb = txtSrcDb.Text.Trim();
        var dstDb = txtDstDb.Text.Trim();
        if (string.IsNullOrEmpty(srcServer) || string.IsNullOrEmpty(dstServer) ||
            string.IsNullOrEmpty(srcDb) || string.IsNullOrEmpty(dstDb))
        {
            MessageBox.Show("Nhập đầy đủ thông tin kết nối.", "", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var oldCsdt = txtOldCsdt.Text.Trim();
        var mode = GetSelectedCodeMode();
        var newCsdt = await ResolveNewCsdtForRun(GetCourseCode());
        if (mode == "Tạo mới theo thông tư 17" && string.IsNullOrEmpty(newCsdt))
        {
            MessageBox.Show("Không thể sinh mã mới theo Thông tư 17.", "", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var oldSo = txtOldSoCode.Text.Trim();
        var newSoCode = txtNewSoCode.Text.Trim();
        // Use the user-editable new course code if present (the final code)
        var courseCode = string.IsNullOrWhiteSpace(txtNewCourseCode.Text)
            ? GetCourseCode()
            : txtNewCourseCode.Text.Trim();
        if (string.IsNullOrEmpty(oldCsdt) || (GetSelectedCodeMode() == "Thay thế mã csdt" && string.IsNullOrEmpty(newCsdt)) ||
            string.IsNullOrEmpty(oldSo) || string.IsNullOrEmpty(newSoCode) ||
            string.IsNullOrEmpty(courseCode))
        {
            MessageBox.Show("Nhập đầy đủ mã CSĐT cũ/mới, mã Sở cũ/mới và mã khóa học.", "",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var srcConn = BuildConnString(srcServer, txtSrcUser.Text.Trim(),
            pwdSrcPass.Password, srcDb);
        var dstConn = BuildConnString(dstServer, txtDstUser.Text.Trim(),
            pwdDstPass.Password, dstDb);
        // Save settings when the user initiates a DB sync
        try { SaveSettings(); } catch { }
        _lastDstConnStr = dstConn;
        _lastNewCourseCode = courseCode;

        var tables = AllTableNames.Select(name => new SyncTableConfig
        {
            SourceTable = name,
            DestTable = name,
            KeyColumns = TableKeys.TryGetValue(name, out var keys) ? keys : new List<string>()
        }).ToList();

        if (tables.Count == 0)
        {
            MessageBox.Show("Không có bảng để đồng bộ.", "", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var allocateCsdt = GetSelectedCodeMode() == "Tạo mới theo thông tư 17";
        var engine = new TableSyncEngine(srcConn, dstConn, tables, batchSize, 600,
            newCsdtCode: string.IsNullOrEmpty(newCsdt) ? null : newCsdt,
            newSoCode: string.IsNullOrEmpty(newSoCode) ? null : newSoCode,
            courseCode: string.IsNullOrEmpty(courseCode) ? null : courseCode,
            newCourseName: string.IsNullOrWhiteSpace(txtNewCourseName.Text) ? null : txtNewCourseName.Text.Trim(),
            allocateCsdt: allocateCsdt,
            oldCsdt: txtOldCsdt.Text.Trim());
        engine.OnProgress += m => AppendLog(m);

        SetDbUI(false);
        progressBar.Visibility = Visibility.Visible;
        txtStatus.Text = "Đang đồng bộ...";

        AppendLog("=== Đồng bộ DB CSĐT ===");
        AppendLog($"  Nguồn: {srcServer}/{srcDb}");
        AppendLog($"  Đích:  {dstServer}/{dstDb}");
            if (!string.IsNullOrEmpty(newCsdt))
                AppendLog($"  Mã CSĐT mới (đề xuất): {newCsdt}");
            if (!string.IsNullOrEmpty(newSoCode))
                AppendLog($"  Mã Sở mới: {newSoCode}");
            if (!string.IsNullOrEmpty(courseCode))
                AppendLog($"  Khóa học: {courseCode}");

        try
        {
            var results = await engine.RunAllAsync();
            sw.Stop();
            progressBar.Visibility = Visibility.Collapsed;
            var ok = results.Count(r => r.Success);
            var failCount = results.Count(r => !r.Success);
            var hvResult = results.FirstOrDefault(r => r.TableName.Contains("NguoiLX"));
            var hvInserted = hvResult?.InsertedCount ?? 0;
            AppendSyncCompleteMessage(hvInserted, failCount, sw.Elapsed.TotalSeconds, ok, results.Count);
            txtStatus.Text = failCount > 0 ? $"Hoàn tất: {failCount} lỗi" : $"Hoàn tất: +{hvInserted} học viên";
        }
        catch (Exception ex)
        {
            sw.Stop();
            progressBar.Visibility = Visibility.Collapsed;
            AppendLog($"LỖI: {ex.Message}");
            txtStatus.Text = "Lỗi";
        }
        finally { SetDbUI(true); }
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
        if (_isBusy) return;
        var label = "Nguồn";
        var server = txtSrcServer.Text.Trim();
        var db = txtSrcDb.Text.Trim();
        AppendLog($"--- Kiểm tra kết nối {label}: {server}/{db} ---");
        _isBusy = true;
        Cursor = Cursors.Wait;
        SetActionButtonsEnabled(false);
        try
        {
            var conn = BuildConnString(server, txtSrcUser.Text.Trim(),
                pwdSrcPass.Password, db);
            await TestConnection(conn, txtSrcStatus, label);
        }
        finally
        {
            Cursor = Cursors.Arrow;
            SetActionButtonsEnabled(true);
            _isBusy = false;
        }
    }

    private async void BtnTestDst_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        var label = "Đích";
        var server = txtDstServer.Text.Trim();
        var db = txtDstDb.Text.Trim();
        AppendLog($"--- Kiểm tra kết nối {label}: {server}/{db} ---");
        _isBusy = true;
        Cursor = Cursors.Wait;
        SetActionButtonsEnabled(false);
        try
        {
            var conn = BuildConnString(server, txtDstUser.Text.Trim(),
                pwdDstPass.Password, db);
            await TestConnection(conn, txtDstStatus, label);
        }
        finally
        {
            Cursor = Cursors.Arrow;
            SetActionButtonsEnabled(true);
            _isBusy = false;
        }
    }

    private async Task TestConnection(string connStr, TextBlock statusBlock, string label)
    {
        try
        {
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            statusBlock.Text = "✓ OK";
            statusBlock.Foreground = new SolidColorBrush(Color.FromRgb(0, 150, 0));
            AppendLog($"  ✓ Kết nối {label} thành công.");
            // Save settings when a connection test succeeds (user requested behavior)
            try { SaveSettings(); } catch { }
            // no preview note control any more
        }
        catch (Exception ex)
        {
            statusBlock.Text = "✗ Lỗi";
            statusBlock.Foreground = new SolidColorBrush(Color.FromRgb(200, 0, 0));
            AppendLog($"  LỖI: Kết nối {label} thất bại: {ex.Message}");
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
        var doc = txtLog.Document;
        var isError = message.StartsWith("LỖI:") || message.StartsWith("ERROR:");
        var color = isError ? Colors.Red : Color.FromRgb(0, 255, 136);
        var run = new Run(message + Environment.NewLine)
        {
            Foreground = new SolidColorBrush(color)
        };
        doc.Blocks.Add(new Paragraph(run) { Margin = new Thickness(0) });
        txtLog.ScrollToEnd();
    }

    private void AppendSyncCompleteMessage(long inserted, int failCount,
        double elapsedSec, int ok, int total)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() =>
                AppendSyncCompleteMessage(inserted, failCount, elapsedSec, ok, total));
            return;
        }
        var doc = txtLog.Document;
        var p = new Paragraph { Margin = new Thickness(0) };
        p.Inlines.Add(new Run(
            $"=== Đồng bộ hoàn tất: +{inserted} học viên ("));
        p.Inlines.Add(new Run("nhấn vào đây để xem")
        {
            Tag = "SyncLink",
            Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 255)),
            Cursor = Cursors.Hand
        });
        p.Inlines.Add(new Run(
            $"), {failCount} lỗi ({elapsedSec:F1}s) ==="));
        doc.Blocks.Add(p);
        txtLog.ScrollToEnd();
    }

    private void TxtLog_PreviewMouseLeftButtonDown(object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        var pos = txtLog.GetPositionFromPoint(e.GetPosition(txtLog), true);
        if (pos?.Parent is Run { Tag: "SyncLink" })
        {
            ShowSyncResultWindow();
            e.Handled = true;
        }
    }

    private void ShowSyncResultWindow()
    {
        var w = new SyncResultWindow(_lastDstConnStr, _lastNewCourseCode)
        {
            Owner = this
        };
        w.ShowDialog();
    }

    private void BtnCopyLog_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        _isBusy = true;
        Cursor = Cursors.Wait;
        SetActionButtonsEnabled(false);
        try
        {
            var text = new TextRange(txtLog.Document.ContentStart, txtLog.Document.ContentEnd).Text;
            Clipboard.SetText(text.TrimEnd('\r', '\n'));
            txtCopyStatus.Visibility = Visibility.Visible;
            Task.Delay(2000).ContinueWith(_ =>
                Dispatcher.Invoke(() => txtCopyStatus.Visibility = Visibility.Collapsed));
        }
        catch { }
        finally
        {
            Cursor = Cursors.Arrow;
            SetActionButtonsEnabled(true);
            _isBusy = false;
        }
    }

    private void SetActionButtonsEnabled(bool enabled)
    {
        btnTestSrc.IsEnabled = enabled;
        btnTestDst.IsEnabled = enabled;
        btnPreview.IsEnabled = enabled;
        btnDbSync.IsEnabled = enabled;
        btnCopyLog.IsEnabled = enabled;
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
        txtOldCsdt.IsEnabled = enabled;
        txtNewCsdt.IsEnabled = enabled;
        txtOldSoCode.IsEnabled = enabled;
        txtNewSoCode.IsEnabled = enabled;
        cmbCourseCode.IsEnabled = enabled;
        txtBatchSize.IsEnabled = enabled;
        SetActionButtonsEnabled(enabled);
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
        public string OldCsdt { get; set; } = "";
        public string NewCsdt { get; set; } = "";
        public string OldSoCode { get; set; } = "";
        public string NewSoCode { get; set; } = "";
        public string CourseCode { get; set; } = "";
        public string BatchSize { get; set; } = "50000";
        public string CodeMode { get; set; } = "Thay thế mã csdt";
        public string NewCourseName { get; set; } = "";
    }

    private string GetSelectedCodeMode() => cmbCodeMode?.SelectedItem is ComboBoxItem it ? it.Content?.ToString() ?? "Thay thế mã csdt" : "Thay thế mã csdt";

    private async Task<string?> ResolveNewCsdtForRun(string courseCode)
    {
        var mode = GetSelectedCodeMode();
        if (mode == "Giữ nguyên")
            return null; // don't change
        if (mode == "Thay thế mã csdt")
            return string.IsNullOrWhiteSpace(txtNewCsdt.Text) ? null : txtNewCsdt.Text.Trim();

        // Tạo mới theo TT17: read TT17 doc to validate rules, then determine province and query destination DB
        try
        {
            // Do not require an external TT17 file — generate defaults per TT17 rules.
            var old = txtOldCsdt.Text.Trim();
            if (old.Length < 2)
            {
                AppendLog("  LỖI: Mã CSĐT cũ không hợp lệ để sinh mã TT17.");
                return null;
            }
            var province = old.Substring(0, 2);
            // If user provided a new CSĐT explicitly, prefer that as base (must follow TT17 length 5)
            var userNew = txtNewCsdt.Text.Trim();
            if (!string.IsNullOrEmpty(userNew) && userNew.Length == 5)
            {
                return userNew;
            }
            var dstServer = txtDstServer.Text.Trim();
            var dstDb = txtDstDb.Text.Trim();
            // If destination connection not provided, fall back to default simple generator (province + 001)
            if (string.IsNullOrEmpty(dstServer) || string.IsNullOrEmpty(dstDb))
            {
                var fallback = province + "001";
                AppendLog($"  TT17: chưa có kết nối đích, dùng mã mặc định: {fallback}");
                return fallback;
            }
            var connStr = BuildConnString(dstServer, txtDstUser.Text.Trim(), pwdDstPass.Password, dstDb);
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            // Find max 3-digit sequence for MaCSDT with this province prefix
            var sql = "SELECT MAX(TRY_CONVERT(int, SUBSTRING(MaCSDT,3,3))) FROM KhoaHoc WHERE LEFT(ISNULL(MaCSDT,''),2) = @p AND LEN(ISNULL(MaCSDT,'')) = 5";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@p", province);
            cmd.CommandTimeout = 10;
            var res = await cmd.ExecuteScalarAsync();
            int max = 0;
            if (res != DBNull.Value && res != null)
            {
                try { max = Convert.ToInt32(res); } catch { max = 0; }
            }
            var next = max + 1;
            if (next > 999)
            {
                AppendLog($"  LỖI: Không thể cấp mã mới TT17 cho tỉnh {province}: vượt quá 999.");
                return null;
            }
            var seq = next.ToString("D3");
            return province + seq;
        }
        catch (Exception ex)
        {
            AppendLog($"  LỖI: Khi sinh mã TT17: {ex.Message}");
            return null;
        }
    }
}
