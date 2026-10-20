using System.Data.Common;
using Npgsql;
using NpgsqlTypes;

namespace DbTool.Core;

public class PostgresProvider : DbProvider
{
    public override ProviderKind Kind => ProviderKind.Postgres;
    public override string Title => "PostgreSQL";

    public override void Open(Dictionary<string, string> cfg)
    {
        Cfg = cfg;
        var cs = new NpgsqlConnectionStringBuilder
        {
            Host = cfg.GetValueOrDefault("host", "localhost"),
            Port = int.TryParse(cfg.GetValueOrDefault("port", "5432"), out var p) ? p : 5432,
            Username = cfg.GetValueOrDefault("user", "postgres"),
            Password = cfg.GetValueOrDefault("password", string.Empty),
            Database = cfg.GetValueOrDefault("database", string.Empty),
            Timeout = 15
        };

        if (string.IsNullOrWhiteSpace(cs.Database))
            throw new InvalidOperationException("Не указано имя базы данных PostgreSQL.");

        Cnn = new NpgsqlConnection(cs.ConnectionString);
        Cnn.Open();
    }

    public override string QuoteIdent(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

    protected override DbCommand NewCmd(string sql) => new NpgsqlCommand(sql, (NpgsqlConnection)Cnn);

    protected override DbParameter NewParam(string name, DbColumn? col, object? value)
    {
        value = value ?? DBNull.Value;

        if (col is not null)
            value = Normalize(col, value);

        return new NpgsqlParameter(name, ToNpgsqlType(col)) { Value = value };
    }

    private static NpgsqlDbType ToNpgsqlType(DbColumn? col)
    {
        if (col is null)
            return NpgsqlDbType.Text;

        return (col.NativeType ?? string.Empty).ToLowerInvariant() switch
        {
            "timestamptz" or "timestamp with time zone" => NpgsqlDbType.TimestampTz,
            "timestamp" or "timestamp without time zone" => NpgsqlDbType.Timestamp,
            "date" => NpgsqlDbType.Date,
            "time" or "time without time zone" => NpgsqlDbType.Time,
            "timetz" or "time with time zone" => NpgsqlDbType.TimeTz,
            "interval" => NpgsqlDbType.Interval,
            "uuid" => NpgsqlDbType.Uuid,
            "bytea" => NpgsqlDbType.Bytea,
            "bool" or "boolean" => NpgsqlDbType.Boolean,
            "int2" or "smallint" => NpgsqlDbType.Smallint,
            "int4" or "integer" => NpgsqlDbType.Integer,
            "int8" or "bigint" => NpgsqlDbType.Bigint,
            "numeric" or "decimal" => NpgsqlDbType.Numeric,
            "float4" or "real" => NpgsqlDbType.Real,
            "float8" or "double precision" => NpgsqlDbType.Double,
            _ => FallbackType(col.Kind)
        };
    }

    private static NpgsqlDbType FallbackType(ColumnKind kind) => kind switch
    {
        ColumnKind.Text or ColumnKind.Unknown => NpgsqlDbType.Text,
        ColumnKind.Int => NpgsqlDbType.Integer,
        ColumnKind.Long => NpgsqlDbType.Bigint,
        ColumnKind.Double => NpgsqlDbType.Double,
        ColumnKind.Decimal => NpgsqlDbType.Numeric,
        ColumnKind.Bool => NpgsqlDbType.Boolean,
        ColumnKind.Date => NpgsqlDbType.Timestamp,
        ColumnKind.Bytes => NpgsqlDbType.Bytea,
        ColumnKind.Guid => NpgsqlDbType.Uuid,
        _ => NpgsqlDbType.Text
    };

    private static object Normalize(DbColumn col, object value)
    {
        if (col.Kind == ColumnKind.Guid && value is string s1)
            return Guid.TryParse(s1, out var g) ? g : value;

        if (col.Kind == ColumnKind.Date && value is string s2)
            return ParseDateValue(col.NativeType ?? string.Empty, s2);

        if (col.Kind == ColumnKind.Bool && value is short or int or long)
            return Convert.ToInt64(value) != 0L;

        if (col.Kind == ColumnKind.Decimal && value is double or float)
            return Convert.ToDecimal(value, CultureInfo.InvariantCulture);

        if (col.Kind == ColumnKind.Long && value is int or short)
            return Convert.ToInt64(value);

        if (col.Kind == ColumnKind.Int && value is long l && l is >= int.MinValue and <= int.MaxValue)
            return Convert.ToInt32(l);

        if (col.Kind is ColumnKind.Text or ColumnKind.Unknown && value is not string && value is not byte[])
            return value.ToString() ?? string.Empty;

        return value;
    }

    private static object ParseDateValue(string native, string text)
    {
        var t = native.ToLowerInvariant();

        if (t.Contains("interval"))
        {
            if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var span))
                return span;
            return text;
        }

        if (t.Equals("date", StringComparison.OrdinalIgnoreCase))
        {
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return d.Date;
            return text;
        }

        if (t.StartsWith("time", StringComparison.Ordinal))
        {
            if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var ts))
                return ts;
            return text;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            if (t.Contains("tz") && dt.Kind == DateTimeKind.Unspecified)
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            return dt;
        }

        return text;
    }

    protected override string ApplyLimit(string sql, int limit)
        => limit > 0 ? sql + " LIMIT " + limit : sql;

    private static string SchemaFilter => "table_schema = CURRENT_SCHEMA()";

    public override string ServerInfo
    {
        get
        {
            var version = Execute("SELECT version()").Data!.Rows[0][0].ToString();
            if (version is not null && version.Length > 60)
                version = version[..60];
            var encoding = Execute("SELECT current_setting('server_encoding')").Data!.Rows[0][0].ToString();
            var size = Execute("SELECT pg_database_size(current_database())").Data!.Rows[0][0];
            return string.Format("PostgreSQL, кодировка: {0}, размер: {1:F1} МБ, таблиц: {2}. Версия: {3}",
                encoding, Convert.ToInt64(size) / 1048576.0, GetTables().Count, version);
        }
    }

    public override List<string> GetTables()
    {
        var data = Execute("SELECT table_name FROM information_schema.tables WHERE " + SchemaFilter + " AND table_type = 'BASE TABLE' ORDER BY table_name").Data!;
        return data.AsEnumerable().Select(r => r[0].ToString()!).ToList();
    }

    public override List<DbColumn> GetColumns(string table)
    {
        var sql = "SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT, ORDINAL_POSITION " +
                  "FROM information_schema.columns WHERE " + SchemaFilter + " AND table_name = @t ORDER BY ORDINAL_POSITION";
        var cmd = NewCmd(sql);
        cmd.Parameters.Add(NewParam("@t", null, table));
        using var reader = cmd.ExecuteReader();
        var raw = new DataTable();
        raw.Load(reader);
        cmd.Dispose();

        var pk = new HashSet<string>(GetPrimaryColumns(table), StringComparer.Ordinal);
        var identity = new HashSet<string>(GetIdentityColumns(table), StringComparer.Ordinal);

        var cols = raw.AsEnumerable().Select(r =>
        {
            var native = r["DATA_TYPE"].ToString() ?? string.Empty;
            var name = r["COLUMN_NAME"].ToString() ?? string.Empty;
            var col = new DbColumn
            {
                Name = name,
                NativeType = native,
                Kind = MapKind(native),
                IsPrimary = pk.Contains(name),
                IsNullable = string.Equals(r["IS_NULLABLE"].ToString(), "YES", StringComparison.OrdinalIgnoreCase),
                Default = r["COLUMN_DEFAULT"] == DBNull.Value ? null : r["COLUMN_DEFAULT"].ToString()
            };
            col.IsAutoIncrement = identity.Contains(name) ||
                                  (col.Default ?? string.Empty).Contains("nextval", StringComparison.OrdinalIgnoreCase);
            return col;
        }).ToList();

        return cols;
    }

    private List<string> GetPrimaryColumns(string table)
    {
        var cmd = NewCmd(
            "SELECT a.attname FROM pg_index i " +
            "JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey) " +
            "WHERE i.indisprimary AND i.indrelid = to_regclass(@t)::oid");
        cmd.Parameters.Add(NewParam("@t", null, table));
        using var reader = cmd.ExecuteReader();
        var raw = new DataTable();
        raw.Load(reader);
        cmd.Dispose();
        return raw.AsEnumerable().Select(r => r[0].ToString()!).ToList();
    }

    private List<string> GetIdentityColumns(string table)
    {
        var cmd = NewCmd(
            "SELECT a.attname FROM pg_attribute a " +
            "WHERE a.attrelid = to_regclass(@t)::oid AND a.attidentity <> ''");
        cmd.Parameters.Add(NewParam("@t", null, table));
        using var reader = cmd.ExecuteReader();
        var raw = new DataTable();
        raw.Load(reader);
        cmd.Dispose();
        return raw.AsEnumerable().Select(r => r[0].ToString()!).ToList();
    }

    private static ColumnKind MapKind(string native) => native switch
    {
        "int2" or "smallint" => ColumnKind.Int,
        "int4" or "integer" => ColumnKind.Int,
        "int8" or "bigint" => ColumnKind.Long,
        "numeric" or "decimal" => ColumnKind.Decimal,
        "float4" or "real" => ColumnKind.Double,
        "float8" or "double precision" => ColumnKind.Double,
        "bool" or "boolean" => ColumnKind.Bool,
        "bytea" => ColumnKind.Bytes,
        "uuid" => ColumnKind.Guid,
        "timestamp" or "timestamptz" or "date" or "time" or "interval" => ColumnKind.Date,
        _ => ColumnKind.Text
    };

    public override string BuildCreateTableSql(string table, List<DbColumn> cols)
    {
        var singlePk = cols.Count(c => c.IsPrimary) == 1;
        var pkCols = cols.Where(c => c.IsPrimary).ToList();
        var parts = new List<string>();

        foreach (var col in cols)
        {
            var isAutoPk = singlePk && col.IsPrimary && col.IsAutoIncrement;
            var bareType = MapType(col.Kind);
            var type = isAutoPk ? (col.Kind == ColumnKind.Long ? "BIGSERIAL" : "SERIAL") : bareType;
            var part = QuoteIdent(col.Name) + " " + type;
            if (!col.IsNullable && col.Default is null && !isAutoPk || !col.IsNullable && isAutoPk)
                part += " NOT NULL";
            parts.Add(part);
        }

        if (!(singlePk && pkCols.FirstOrDefault()?.IsAutoIncrement == true) && pkCols.Count > 0)
            parts.Add("PRIMARY KEY (" + string.Join(", ", pkCols.Select(c => QuoteIdent(c.Name))) + ")");

        return "CREATE TABLE " + QuoteIdent(table) + " (" + string.Join(", ", parts) + ")";
    }

    private static string MapType(ColumnKind kind) => kind switch
    {
        ColumnKind.Text => "TEXT",
        ColumnKind.Int => "INTEGER",
        ColumnKind.Long => "BIGINT",
        ColumnKind.Double => "DOUBLE PRECISION",
        ColumnKind.Decimal => "NUMERIC(38,10)",
        ColumnKind.Bool => "BOOLEAN",
        ColumnKind.Date => "TIMESTAMP",
        ColumnKind.Bytes => "BYTEA",
        ColumnKind.Guid => "UUID",
        _ => "TEXT"
    };

    public override void PostImportFix(string table, List<DbColumn> cols)
    {
        var pk = cols.FirstOrDefault(c => c.IsPrimary);
        if (pk is null || !pk.IsAutoIncrement || cols.Count(c => c.IsPrimary) != 1)
            return;

        try
        {
            var seq = Execute("SELECT pg_get_serial_sequence(" + Literal(table) + ", " + Literal(pk.Name) + ")").Data!.Rows[0][0].ToString();
            if (string.IsNullOrEmpty(seq))
                return;

            Execute("SELECT setval(" + Literal(seq!) + ", (SELECT COALESCE(MAX(" + QuoteIdent(pk.Name) + "), 1) FROM " + QuoteIdent(table) + "))");
            Log.Info("PostgreSQL: последовательность " + seq + " выровнена по таблице " + table);
        }
        catch (Exception ex)
        {
            Log.Error("PostgreSQL: не удалось выровнять последовательность для " + table, ex);
        }
    }

    private static string Literal(string value) => "'" + (value ?? string.Empty).Replace("'", "''") + "'";

    public override void FixDatabase(FixReport report)
    {
        try
        {
            Execute("VACUUM ANALYZE");
            report.Add("VACUUM ANALYZE выполнен по всей базе");
        }
        catch (Exception ex)
        {
            report.Fail("VACUUM ANALYZE не выполнен: " + ex.Message);
        }

        try
        {
            var data = Execute("SELECT indexrelid::regclass AS name FROM pg_index WHERE NOT indisvalid AND indrelid::regclass::text NOT LIKE 'pg.%'").Data!;
            if (data.Rows.Count == 0)
                report.Add("Невалидных индексов нет");
            else
                foreach (DataRow row in data.AsEnumerable())
                    report.Fail("Невалидный индекс (требуется REINDEX): " + row[0]);
        }
        catch (Exception ex)
        {
            report.Fail("Проверка индексов не выполнена: " + ex.Message);
        }
    }
}
