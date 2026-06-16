# Gplx - Ứng dụng đồng bộ dữ liệu

Ứng dụng Windows Forms (.NET 8) cho phép đồng bộ dữ liệu từ thư mục nguồn sang thư mục đích.

## Yêu cầu

- Windows 10/11
- [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0)

## Cấu trúc project

```
src/Gplx.SyncApp/
├── Program.cs               # Entry point
├── Form1.cs                 # Giao diện người dùng
├── Form1.Designer.cs        # Layout controls
├── Gplx.SyncApp.csproj      # Cấu hình project
└── Sync/
    ├── ISyncSource.cs       # Interface nguồn dữ liệu
    ├── ISyncDestination.cs  # Interface đích dữ liệu
    ├── IDataRecord.cs       # Bản ghi dữ liệu
    ├── SyncEngine.cs        # Engine đồng bộ
    ├── SyncResult.cs        # Kết quả đồng bộ
    ├── FileSyncSource.cs    # Đọc dữ liệu từ thư mục
    ├── FileSyncDestination.cs # Ghi dữ liệu vào thư mục
    └── FileDataRecord.cs    # Bản ghi dạng file
```

## Cách chạy

```bash
dotnet run --project src/Gplx.SyncApp -c Release
```

## Cách sử dụng

1. Chọn **Thư mục nguồn** (chứa dữ liệu cần đồng bộ)
2. Chọn **Thư mục đích** (nơi dữ liệu sẽ được sao chép đến)
3. Nhấn **Đồng bộ**
4. Theo dõi tiến trình trong cửa sổ nhật ký

## Kiến trúc

Ứng dụng áp dụng mô hình hướng giao diện (interface-based design):

- `ISyncSource` - định nghĩa nguồn dữ liệu
- `ISyncDestination` - định nghĩa đích dữ liệu
- `SyncEngine` - nhận nguồn và đích, thực hiện đồng bộ (thêm mới, cập nhật, ghi nhận lỗi)

Có thể mở rộng bằng cách implement `ISyncSource`/`ISyncDestination` với các loại nguồn và đích khác nhau (CSV, JSON, API, database...).

## Build

```bash
dotnet build src/Gplx.SyncApp -c Release
```

File thực thi được tạo tại `src/Gplx.SyncApp/bin/Release/net8.0-windows/Gplx.SyncApp.exe`.
