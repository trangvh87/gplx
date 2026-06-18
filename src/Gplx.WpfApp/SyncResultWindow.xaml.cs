using System;
using System.Data;
using System.Windows;
using Microsoft.Data.SqlClient;

namespace Gplx.WpfApp;

public partial class SyncResultWindow : Window
{
    public SyncResultWindow(string connectionString, string courseCode)
    {
        InitializeComponent();
        txtCourseCode.Text = GetDisplayText(connectionString, courseCode);
        LoadData(connectionString, courseCode);
    }

    private static string GetDisplayText(string connStr, string courseCode)
    {
        try
        {
            using var conn = new SqlConnection(connStr);
            using var cmd = new SqlCommand(
                "SELECT ISNULL(TenKH, '') FROM KhoaHoc WHERE MaKH = @code", conn);
            cmd.Parameters.AddWithValue("@code", courseCode);
            conn.Open();
            var ten = cmd.ExecuteScalar() as string;
            if (!string.IsNullOrEmpty(ten))
                return $"{courseCode} - {ten}";
        }
        catch { }
        return courseCode;
    }

    private void LoadData(string connStr, string courseCode)
    {
        dgKhoaHoc.ItemsSource = Query(connStr,
            "SELECT * FROM KhoaHoc WHERE MaKH = @code", courseCode).DefaultView;
        dgHocVien.ItemsSource = Query(connStr,
            "SELECT N.* FROM NguoiLX N INNER JOIN NguoiLX_HoSo H ON N.MaDK = H.MaDK WHERE H.MaKhoaHoc = @code", courseCode).DefaultView;
        dgBaoCaoI.ItemsSource = Query(connStr,
            "SELECT * FROM BaoCaoI WHERE MaKH = @code", courseCode).DefaultView;
        dgBaoCaoII.ItemsSource = Query(connStr,
            "SELECT B2.* FROM BaoCaoII B2 INNER JOIN BaoCaoI B1 ON B2.MaBCI = B1.MaBCI WHERE B1.MaKH = @code", courseCode).DefaultView;
    }

    private static DataTable Query(string connStr, string sql, string courseCode)
    {
        var dt = new DataTable();
        try
        {
            using var conn = new SqlConnection(connStr);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@code", courseCode);
            using var da = new SqlDataAdapter(cmd);
            da.Fill(dt);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi truy vấn: {ex.Message}", "", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        return dt;
    }
}
