# Gplx - Hệ thống đồng bộ dữ liệu đào tạo lái xe

Hệ thống đồng bộ dữ liệu giữa các cơ sở đào tạo (CSĐT) và Sở GTVT, hỗ trợ chuyển đổi mã theo Thông tư 17/2026/TT-BXD.

## Yêu cầu

- Windows 7 SP1 / 8 / 8.1 / 10 / 11
- [.NET Framework 4.8 Runtime](https://dotnet.microsoft.com/download/dotnet-framework/net48)
- [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0) (để build)

## Cấu trúc project

```
src/
├── Gplx.Core/              # Class Library dùng chung
│   └── DbSync/
│       ├── ColumnInfo.cs
│       ├── SchemaDiscovery.cs
│       ├── SyncResult.cs
│       ├── SyncTableConfig.cs
│       └── TableSyncEngine.cs
│
├── Gplx.WpfApp/            # App CSĐT (WPF)
│   ├── App.xaml / App.xaml.cs
│   ├── MainWindow.xaml / MainWindow.xaml.cs
│   ├── DbSync/
│   │   └── TableSyncEngine.cs   # Engine với transform mã + filter khóa học
│   └── Sync/                     # Engine đồng bộ file
│
├── Gplx.SoApp/             # App Sở (WPF)
│   ├── App.xaml / App.xaml.cs
│   ├── MainWindow.xaml / MainWindow.xaml.cs
│   └── ScriptRunner.cs          # Chạy SQL scripts đồng bộ
│
└── Gplx.DbSync/            # Console app đồng bộ DB
```

## Cách chạy

### App CSĐT
```bash
scripts\run-wpf.bat
```
Nhập thông tin kết nối, mã CSĐT cũ/mới, mã khóa học → Xem trước → Đồng bộ.

### App Sở
```bash
dotnet run --project src/Gplx.SoApp -c Release
```

## Build

```bash
dotnet build src/Gplx.Core -c Release
dotnet build src/Gplx.WpfApp -c Release
dotnet build src/Gplx.SoApp -c Release
```

Output tại `src/*/bin/Release/net48/`.

## Tính năng chính

### App CSĐT (Gplx.WpfApp)
- Đồng bộ bảng bằng bulk copy với transform mã tự động theo TT 17
- Hỗ trợ nhập mã CSĐT cũ → mới (thay đổi 5 ký tự đầu MaKH, MaDK)
- Hỗ trợ nhập mã Sở cũ → mới (thay đổi MaSoGTVT)
- Lọc theo mã khóa học (chỉ đồng bộ dữ liệu liên quan đến khóa học đó)
- Xem trước dữ liệu NguoiLX, BaoCaoI, BaoCaoII trước khi đồng bộ
- Tự động thêm cột `_Cu` để lưu giá trị mã cũ

### App Sở (Gplx.SoApp)
- Đồng bộ qua SQL scripts từ thư mục `docs/script_syn_so/`
- Hỗ trợ nhập mã Sở mới
- Lọc theo mã khóa học
- Xem trước dữ liệu

## Danh sách bảng đồng bộ

| # | Bảng | Lọc khóa học? | Cách lọc | Transform MaCSDT | Transform MaKH | Transform MaKhoaHoc | Transform MaDK | Transform MaSoGTVT |
|---|---|---|---|---|---|---|---|---|
| 1 | `DM_DonViGTVT` | Không | — | — | — | — | — | — |
| 2 | `DM_DVHC` | Không | — | — | — | — | — | — |
| 3 | `DM_HangDT` | Không | — | — | — | — | — | — |
| 4 | `KhoaHoc` | Có | `MaKH = @CourseCode` | ✓ | ✓ | — | — | ✓ |
| 5 | `BaoCaoI` | Có | `MaKH = @CourseCode` | ✓ | ✓ | — | — | — |
| 6 | `BaoCaoII` | Có | `MaBCI IN (SELECT MaBCI FROM BaoCaoI WHERE MaKH = @CourseCode)` | ✓ | — | — | — | — |
| 7 | `GiaoVien` | Không | — | ✓ | — | — | — | ✓ |
| 8 | `LichHoc` | Có | `MaKH = @CourseCode` | — | ✓ | — | — | — |
| 9 | `NguoiLX` | Có | `MaDK IN (SELECT MaDK FROM NguoiLX_HoSo WHERE MaKhoaHoc = @CourseCode)` | — | — | — | ✓ | — |
| 10 | `NguoiLX_HoSo` | Có | `MaKhoaHoc = @CourseCode` | ✓ | — | ✓ | ✓ | ✓ |
| 11 | `NguoiLX_GPLX` | Có | `MaDK IN (SELECT MaDK FROM NguoiLX_HoSo WHERE MaKhoaHoc = @CourseCode)` | — | — | — | ✓ | — |
| 12 | `NguoiLXHS_GiayTo` | Có | `MaDK IN (SELECT MaDK FROM NguoiLX_HoSo WHERE MaKhoaHoc = @CourseCode)` | — | — | — | ✓ | — |
| 13 | `XeTap` | Không | — | ✓ | — | — | — | ✓ |

## Chuyển đổi mã

Các cột được transform trên bảng tạm (`##Gplx_Sync_*`) trước khi INSERT vào đích:

| Cột gốc | Công thức transform | Cột lưu giá trị cũ |
|---|---|---|
| `MaCSDT` | `@NewCsdtCode` | `MaCSDT_Cu` (varchar(6)) |
| `MaKH` | `@NewCsdtCode + SUBSTRING([MaKH], 6, LEN([MaKH]))` | `MaKH_Cu` (varchar(13)) |
| `MaKhoaHoc` | `@NewCsdtCode + SUBSTRING([MaKhoaHoc], 6, LEN([MaKhoaHoc]))` | `MaKhoaHoc_Cu` (varchar(13)) |
| `MaDK` | `@NewCsdtCode + SUBSTRING([MaDK], 6, LEN([MaDK]))` | `MaDK_Cu` (varchar(25)) |
| `MaSoGTVT` | `@NewSoCode` | `MaSoGTVT_Cu` (varchar(6)) |

Các cột `_Cu` tự động được thêm vào bảng đích bằng `ALTER TABLE … ADD … IF NOT EXISTS` nếu chưa tồn tại.

## Quy trình đồng bộ (mỗi bảng)

1. Lấy danh sách cột chung giữa nguồn và đích
2. Tạo bảng tạm `##Gplx_Sync_{Table}_{Guid}` trên đích (có thêm cột `_Cu` nếu cần)
3. `SqlBulkCopy` dữ liệu từ nguồn → bảng tạm (kèm filter khóa học nếu có)
4. Transform mã CSĐT / mã Sở trên bảng tạm
5. `INSERT INTO … SELECT … WHERE NOT EXISTS` từ bảng tạm vào đích
6. Drop bảng tạm
