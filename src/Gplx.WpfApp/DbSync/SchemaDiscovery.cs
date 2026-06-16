using Microsoft.Data.SqlClient;

namespace Gplx.WpfApp.DbSync;

public sealed class SchemaDiscovery
{
    public static async Task<List<ColumnInfo>> GetColumnsAsync(
        string connectionString, string schema, string tableName)
    {
        var sql = """
            SELECT
                c.COLUMN_NAME,
                c.DATA_TYPE,
                c.CHARACTER_MAXIMUM_LENGTH,
                c.NUMERIC_PRECISION,
                c.NUMERIC_SCALE,
                COLUMNPROPERTY(OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME), c.COLUMN_NAME, 'IsIdentity') AS IsIdentity,
                c.IS_NULLABLE
            FROM INFORMATION_SCHEMA.COLUMNS c
            WHERE c.TABLE_SCHEMA = @Schema AND c.TABLE_NAME = @Table
            ORDER BY c.ORDINAL_POSITION
            """;

        var columns = new List<ColumnInfo>();

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Schema", schema);
        cmd.Parameters.AddWithValue("@Table", tableName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(new ColumnInfo
            {
                Name = reader.GetString(0),
                DataType = reader.GetString(1),
                MaxLength = reader.IsDBNull(2) ? null : (int?)reader.GetInt32(2),
                Precision = reader.IsDBNull(3) ? null : (int?)reader.GetByte(3),
                Scale = reader.IsDBNull(4) ? null : (int?)reader.GetInt32(4),
                IsIdentity = reader.IsDBNull(5) ? false : reader.GetInt32(5) == 1,
                IsNullable = reader.GetString(6) == "YES"
            });
        }

        return columns;
    }
}
