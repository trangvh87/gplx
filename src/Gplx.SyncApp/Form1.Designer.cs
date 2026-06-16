namespace Gplx.SyncApp;

partial class Form1
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        tabControl = new TabControl();
        tabFile = new TabPage();
        tabDb = new TabPage();
        txtLog = new RichTextBox();

        // File tab controls
        lblSource = new Label();
        txtSource = new TextBox();
        btnBrowseSource = new Button();
        lblDestination = new Label();
        txtDestination = new TextBox();
        btnBrowseDest = new Button();
        btnFileSync = new Button();

        // DB tab controls
        grpSrc = new GroupBox();
        lblSrcServer = new Label();
        txtSrcServer = new TextBox();
        lblSrcUser = new Label();
        txtSrcUser = new TextBox();
        lblSrcPass = new Label();
        txtSrcPass = new TextBox();
        lblSrcDb = new Label();
        txtSrcDb = new TextBox();
        btnTestSrc = new Button();

        grpDst = new GroupBox();
        lblDstServer = new Label();
        txtDstServer = new TextBox();
        lblDstUser = new Label();
        txtDstUser = new TextBox();
        lblDstPass = new Label();
        txtDstPass = new TextBox();
        lblDstDb = new Label();
        txtDstDb = new TextBox();
        btnTestDst = new Button();

        lblTables = new Label();
        clbTables = new CheckedListBox();
        btnSelectAll = new Button();
        btnDeselectAll = new Button();
        lblBatchSize = new Label();
        numBatchSize = new NumericUpDown();
        btnDbSync = new Button();
        btnCopyToDst = new Button();
        btnCopyToSrc = new Button();
        btnSaveConn = new Button();
        btnLoadConn = new Button();

        SuspendLayout();

        // tabControl
        tabControl.Location = new Point(12, 12);
        tabControl.Size = new Size(760, 340);
        tabControl.TabIndex = 0;
        tabControl.TabPages.Add(tabFile);
        tabControl.TabPages.Add(tabDb);

        // tabFile
        tabFile.Text = "Đồng bộ File";
        tabFile.Controls.Add(lblSource);
        tabFile.Controls.Add(txtSource);
        tabFile.Controls.Add(btnBrowseSource);
        tabFile.Controls.Add(lblDestination);
        tabFile.Controls.Add(txtDestination);
        tabFile.Controls.Add(btnBrowseDest);
        tabFile.Controls.Add(btnFileSync);

        lblSource.Text = "Thư mục nguồn:";
        lblSource.Location = new Point(12, 20);
        lblSource.Size = new Size(100, 23);

        txtSource.Location = new Point(118, 17);
        txtSource.Size = new Size(440, 23);

        btnBrowseSource.Text = "...";
        btnBrowseSource.Location = new Point(564, 16);
        btnBrowseSource.Size = new Size(36, 26);
        btnBrowseSource.Click += BtnBrowseSource_Click;

        lblDestination.Text = "Thư mục đích:";
        lblDestination.Location = new Point(12, 58);
        lblDestination.Size = new Size(100, 23);

        txtDestination.Location = new Point(118, 55);
        txtDestination.Size = new Size(440, 23);

        btnBrowseDest.Text = "...";
        btnBrowseDest.Location = new Point(564, 54);
        btnBrowseDest.Size = new Size(36, 26);
        btnBrowseDest.Click += BtnBrowseDest_Click;

        btnFileSync.Text = "Đồng bộ";
        btnFileSync.Location = new Point(620, 16);
        btnFileSync.Size = new Size(100, 64);
        btnFileSync.Click += BtnFileSync_Click;

        // tabDb
        tabDb.Text = "Đồng bộ DB";
        tabDb.Controls.Add(grpSrc);
        tabDb.Controls.Add(grpDst);
        tabDb.Controls.Add(lblTables);
        tabDb.Controls.Add(clbTables);
        tabDb.Controls.Add(btnSelectAll);
        tabDb.Controls.Add(btnDeselectAll);
        tabDb.Controls.Add(lblBatchSize);
        tabDb.Controls.Add(numBatchSize);
        tabDb.Controls.Add(btnSaveConn);
        tabDb.Controls.Add(btnLoadConn);
        tabDb.Controls.Add(btnDbSync);

        // ── Source GroupBox ──
        grpSrc.Text = "Nguồn (Source)";
        grpSrc.Location = new Point(6, 6);
        grpSrc.Size = new Size(370, 110);
        grpSrc.Controls.Add(lblSrcServer);
        grpSrc.Controls.Add(txtSrcServer);
        grpSrc.Controls.Add(lblSrcUser);
        grpSrc.Controls.Add(txtSrcUser);
        grpSrc.Controls.Add(lblSrcPass);
        grpSrc.Controls.Add(txtSrcPass);
        grpSrc.Controls.Add(lblSrcDb);
        grpSrc.Controls.Add(txtSrcDb);
        grpSrc.Controls.Add(btnTestSrc);
        grpSrc.Controls.Add(btnCopyToDst);

        lblSrcServer.Text = "Server:";
        lblSrcServer.Location = new Point(10, 23);
        lblSrcServer.Size = new Size(60, 23);

        txtSrcServer.Location = new Point(72, 20);
        txtSrcServer.Size = new Size(190, 23);
        txtSrcServer.Text = ".";

        lblSrcUser.Text = "User:";
        lblSrcUser.Location = new Point(10, 52);
        lblSrcUser.Size = new Size(60, 23);

        txtSrcUser.Location = new Point(72, 49);
        txtSrcUser.Size = new Size(90, 23);
        txtSrcUser.Text = "sa";

        lblSrcPass.Text = "Pass:";
        lblSrcPass.Location = new Point(168, 52);
        lblSrcPass.Size = new Size(38, 23);

        txtSrcPass.Location = new Point(206, 49);
        txtSrcPass.Size = new Size(56, 23);
        txtSrcPass.UseSystemPasswordChar = true;

        lblSrcDb.Text = "Database:";
        lblSrcDb.Location = new Point(10, 81);
        lblSrcDb.Size = new Size(60, 23);

        txtSrcDb.Location = new Point(72, 78);
        txtSrcDb.Size = new Size(190, 23);
        txtSrcDb.Text = "GPLX_CSDT_64004_Cu";

        btnTestSrc.Text = "Kiểm tra";
        btnTestSrc.Location = new Point(280, 18);
        btnTestSrc.Size = new Size(75, 26);
        btnTestSrc.Click += BtnTestSrc_Click;

        btnCopyToDst.Text = ">";
        btnCopyToDst.Location = new Point(280, 50);
        btnCopyToDst.Size = new Size(36, 23);
        btnCopyToDst.Click += BtnCopyToDst_Click;

        // ── Destination GroupBox ──
        grpDst.Text = "Đích (Destination)";
        grpDst.Location = new Point(384, 6);
        grpDst.Size = new Size(370, 110);
        grpDst.Controls.Add(lblDstServer);
        grpDst.Controls.Add(txtDstServer);
        grpDst.Controls.Add(lblDstUser);
        grpDst.Controls.Add(txtDstUser);
        grpDst.Controls.Add(lblDstPass);
        grpDst.Controls.Add(txtDstPass);
        grpDst.Controls.Add(lblDstDb);
        grpDst.Controls.Add(txtDstDb);
        grpDst.Controls.Add(btnTestDst);
        grpDst.Controls.Add(btnCopyToSrc);

        lblDstServer.Text = "Server:";
        lblDstServer.Location = new Point(10, 23);
        lblDstServer.Size = new Size(60, 23);

        txtDstServer.Location = new Point(72, 20);
        txtDstServer.Size = new Size(190, 23);
        txtDstServer.Text = ".";

        lblDstUser.Text = "User:";
        lblDstUser.Location = new Point(10, 52);
        lblDstUser.Size = new Size(60, 23);

        txtDstUser.Location = new Point(72, 49);
        txtDstUser.Size = new Size(90, 23);
        txtDstUser.Text = "sa";

        lblDstPass.Text = "Pass:";
        lblDstPass.Location = new Point(168, 52);
        lblDstPass.Size = new Size(38, 23);

        txtDstPass.Location = new Point(206, 49);
        txtDstPass.Size = new Size(56, 23);
        txtDstPass.UseSystemPasswordChar = true;

        lblDstDb.Text = "Database:";
        lblDstDb.Location = new Point(10, 81);
        lblDstDb.Size = new Size(60, 23);

        txtDstDb.Location = new Point(72, 78);
        txtDstDb.Size = new Size(190, 23);
        txtDstDb.Text = "GPLX_CDB_CSDT_v2";

        btnTestDst.Text = "Kiểm tra";
        btnTestDst.Location = new Point(280, 18);
        btnTestDst.Size = new Size(75, 26);
        btnTestDst.Click += BtnTestDst_Click;

        btnCopyToSrc.Text = "<";
        btnCopyToSrc.Location = new Point(280, 50);
        btnCopyToSrc.Size = new Size(36, 23);
        btnCopyToSrc.Click += BtnCopyToSrc_Click;

        lblTables.Text = "Bảng cần đồng bộ:";
        lblTables.Location = new Point(12, 126);
        lblTables.Size = new Size(120, 23);

        clbTables.Location = new Point(118, 126);
        clbTables.Size = new Size(520, 150);
        clbTables.CheckOnClick = true;
        clbTables.Items.AddRange([
            "DM_DonViGTVT",
            "DM_DVHC",
            "DM_HangDT",
            "KhoaHoc",
            "BaoCaoI",
            "BaoCaoII",
            "GiaoVien",
            "LichHoc",
            "NguoiLX",
            "NguoiLX_HoSo",
            "NguoiLXHS_GiayTo",
            "NguoiLX_GPLX",
            "XeTap"
        ]);
        for (int i = 0; i < clbTables.Items.Count; i++)
            clbTables.SetItemChecked(i, true);

        btnSelectAll.Text = "Chọn hết";
        btnSelectAll.Location = new Point(644, 126);
        btnSelectAll.Size = new Size(55, 26);
        btnSelectAll.Click += (_, _) => SetAllChecked(true);

        btnDeselectAll.Text = "Bỏ hết";
        btnDeselectAll.Location = new Point(700, 126);
        btnDeselectAll.Size = new Size(50, 26);
        btnDeselectAll.Click += (_, _) => SetAllChecked(false);

        lblBatchSize.Text = "Batch size:";
        lblBatchSize.Location = new Point(12, 286);
        lblBatchSize.Size = new Size(100, 23);

        numBatchSize.Location = new Point(118, 283);
        numBatchSize.Size = new Size(100, 23);
        numBatchSize.Minimum = 1000;
        numBatchSize.Maximum = 500000;
        numBatchSize.Value = 50000;
        numBatchSize.Increment = 10000;

        btnDbSync.Text = "Đồng bộ DB";
        btnDbSync.Location = new Point(620, 260);
        btnDbSync.Size = new Size(130, 40);
        btnDbSync.Click += BtnDbSync_Click;

        btnSaveConn.Text = "Lưu kết nối";
        btnSaveConn.Location = new Point(230, 283);
        btnSaveConn.Size = new Size(90, 26);
        btnSaveConn.Click += BtnSaveConn_Click;

        btnLoadConn.Text = "Tải kết nối";
        btnLoadConn.Location = new Point(330, 283);
        btnLoadConn.Size = new Size(90, 26);
        btnLoadConn.Click += BtnLoadConn_Click;

        // txtLog (shared)
        txtLog.Location = new Point(12, 358);
        txtLog.Size = new Size(760, 200);
        txtLog.ReadOnly = true;
        txtLog.BackColor = Color.FromArgb(30, 30, 30);
        txtLog.ForeColor = Color.Lime;
        txtLog.Font = new Font("Consolas", 9.75f);
        txtLog.WordWrap = false;

        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(784, 570);
        Controls.AddRange([tabControl, txtLog]);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Gplx - Đồng bộ dữ liệu";
        ResumeLayout(false);
        PerformLayout();
    }

    private TabControl tabControl;
    private TabPage tabFile;
    private TabPage tabDb;
    private RichTextBox txtLog;

    private Label lblSource;
    private TextBox txtSource;
    private Button btnBrowseSource;
    private Label lblDestination;
    private TextBox txtDestination;
    private Button btnBrowseDest;
    private Button btnFileSync;

    private GroupBox grpSrc;
    private Label lblSrcServer;
    private TextBox txtSrcServer;
    private Label lblSrcUser;
    private TextBox txtSrcUser;
    private Label lblSrcPass;
    private TextBox txtSrcPass;
    private Label lblSrcDb;
    private TextBox txtSrcDb;
    private Button btnTestSrc;

    private GroupBox grpDst;
    private Label lblDstServer;
    private TextBox txtDstServer;
    private Label lblDstUser;
    private TextBox txtDstUser;
    private Label lblDstPass;
    private TextBox txtDstPass;
    private Label lblDstDb;
    private TextBox txtDstDb;
    private Button btnTestDst;
    private Label lblTables;
    private CheckedListBox clbTables;
    private Button btnSelectAll;
    private Button btnDeselectAll;
    private Label lblBatchSize;
    private NumericUpDown numBatchSize;
    private Button btnDbSync;
    private Button btnCopyToDst;
    private Button btnCopyToSrc;
    private Button btnSaveConn;
    private Button btnLoadConn;
}
