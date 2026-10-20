using System.Data.Common;
using MySqlConnector;

namespace DbTool.Core;

public class MySqlProvider : DbProvider
{
    public override ProviderKind Kind => ProviderKind.MySql;
    public override string Title => "MySQL / MariaDB";

    public override void Open(Dictionary<string, string> cfg)
    {
        Cfg = cfg;
        var cs = new MySqlConnectionStringBuilder
        {
            Server = cfg.GetValueOrDefault("host", "localhost"),
            Port = uint.TryParse(cfg.GetValueOrDefault("port", "3306"), out var p) ? p : 3306u,
            UserID = cfg.GetValueOrDefault("user", "root"),
            Password = cfg.GetValueOrDefault("password", string.Empty),
            Database = cfg.GetValueOrDefault("database", string.Empty),
            ConnectionTimeout = 15,
            DefaultCommandTimeout = 120
        };

        if (string.IsNullOrWhiteSpace(cs.Database))
            throw new InvalidOperationException("Не указано имя базы данных MySQL.");

        Cnn = new MySqlConnection(cs.ConnectionString);
        Cnn.Open();
    }

    public override string QuoteIdent(string name) => "`" + name.Replace("`", "``") + "`";

    protected override DbCommand NewCmd(string sql) => new MySqlCommand(sql, (MySqlConnection)Cnn);

    protected override DbParameter NewParam(string name, DbColumn? col, object? value)
        => new MySqlParameter(name, value ?? DBNull.Value);

    protected override string ApplyLimit(string sql, int limit)
        => limit > 0 ? sql + " LIMIT " + limit : sql;

    private static string DbSchema => "DATABASE()";

    public override string ServerInfo
    {
        get
        {
            var version = Execute("SELECT VERSION()").Data!.Rows[0][0].ToString();
            var charset = Execute("SELECT @@character_set_database").Data!.Rows[0][0].ToString();
            var size = Execute("SELECT IFNULL(SUM(data_length + index_length), 0)/1048576 FROM information_schema.tables WHERE table_schema = DATABASE()")
                .Data!.Rows[0][0];
            var currentDb = Execute("SELECT DATABASE()").Data!.Rows[0][0].ToString();
            return string.Format("MySQL {0}, база: {1}, кодировка: {2}, размер: {3:F1} МБ, таблиц: {4}",
                version, currentDb, charset, Convert.ToDouble(size), GetTables().Count);
        }
    }

    public override List<string> GetTables()
    {
        var data = Execute("SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE() AND table_type = 'BASE TABLE' ORDER BY table_name").Data!;
        return data.AsEnumerable().Select(r => r[0].ToString()!).ToList();
    }

    public override List<DbColumn> GetColumns(string table)
    {
        var sql = "SELECT COLUMN_NAME, DATA_TYPE, COLUMN_TYPE, IS_NULLABLE, COLUMN_KEY, COLUMN_DEFAULT, EXTRA, ORDINAL_POSITION " +
                  "FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = @t ORDER BY ORDINAL_POSITION";
        var cmd = NewCmd(sql);
        cmd.Parameters.Add(NewParam("@t", null, table));
        using var reader = cmd.ExecuteReader();
        var raw = new DataTable();
        raw.Load(reader);
        cmd.Dispose();

        var cols = raw.AsEnumerable().Select(r =>
        {
            var columnType = r["COLUMN_TYPE"].ToString() ?? string.Empty;
            var kind = columnType.Equals("tinyint(1)", StringComparison.OrdinalIgnoreCase)
                ? ColumnKind.Bool
                : DbColumn.ParseKind(r["DATA_TYPE"].ToString() ?? string.Empty);
            return new DbColumn
            {
                Name = r["COLUMN_NAME"].ToString() ?? string.Empty,
                NativeType = columnType,
                Kind = kind,
                IsPrimary = string.Equals(r["COLUMN_KEY"].ToString(), "PRI", StringComparison.OrdinalIgnoreCase),
                IsNullable = string.Equals(r["IS_NULLABLE"].ToString(), "YES", StringComparison.OrdinalIgnoreCase),
                Default = r["COLUMN_DEFAULT"] == DBNull.Value ? null : r["COLUMN_DEFAULT"].ToString(),
                IsAutoIncrement = (r["EXTRA"].ToString() ?? string.Empty).Contains("auto_increment", StringComparison.OrdinalIgnoreCase)
            };
        }).ToList();

        return cols;
    }

    public override string BuildCreateTableSql(string table, List<DbColumn> cols)
    {
        var singlePkAuto = cols.Count(c => c.IsPrimary) == 1 && cols.Any(c => c.IsPrimary && c.IsAutoIncrement);
        var parts = new List<string>();

        foreach (var col in cols)
        {
            var type = col.Kind == ColumnKind.Text && col.IsPrimary ? "VARCHAR(255)" : MapType(col.Kind);
            var part = QuoteIdent(col.Name) + " " + type;
            if (col.IsAutoIncrement && singlePkAuto)
                part += " AUTO_INCREMENT";
            if (!col.IsNullable && !col.IsAutoIncrement)
                part += " NOT NULL";
            parts.Add(part);
        }

        var pkCols = cols.Where(c => c.IsPrimary).ToList();
        if (pkCols.Count > 0)
            parts.Add("PRIMARY KEY (" + string.Join(", ", pkCols.Select(c => QuoteIdent(c.Name))) + ")");

        return "CREATE TABLE " + QuoteIdent(table) + " (" + string.Join(", ", parts) + ")";
    }

    private static string MapType(ColumnKind kind) => kind switch
    {
        ColumnKind.Text => "TEXT",
        ColumnKind.Int => "INT",
        ColumnKind.Long => "BIGINT",
        ColumnKind.Double => "DOUBLE",
        ColumnKind.Decimal => "DECIMAL(38,10)",
        ColumnKind.Bool => "TINYINT(1)",
        ColumnKind.Date => "DATETIME",
        ColumnKind.Bytes => "LONGBLOB",
        ColumnKind.Guid => "CHAR(36)",
        _ => "VARCHAR(255)"
    };

    public override void FixDatabase(FixReport report)
    {
        var tables = GetTables();

        var engineData = Execute(
            "SELECT table_name, engine FROM information_schema.tables WHERE table_schema = DATABASE() AND table_type = 'BASE TABLE'").Data!;
        var engines = engineData.AsEnumerable().ToDictionary(
            r => r[0].ToString() ?? string.Empty,
            r => r[1].ToString() ?? string.Empty,
            StringComparer.OrdinalIgnoreCase);

        var brokenMyIsam = new List<string>();

        foreach (var table in tables)
        {
            var result = Execute("CHECK TABLE " + QuoteIdent(table)).Data!;
            foreach (DataRow row in result.AsEnumerable())
            {
                var msgType = row["Msg_type"].ToString() ?? string.Empty;
                var text = row["Msg_text"].ToString() ?? string.Empty;
                if (msgType.Equals("error", StringComparison.OrdinalIgnoreCase) || msgType.Equals("warn", StringComparison.OrdinalIgnoreCase))
                {
                    report.Fail("CHECK TABLE " + table + ": " + text);
                    if (msgType.Contains("error", StringComparison.OrdinalIgnoreCase) &&
                        engines.TryGetValue(table, out var engine) && engine.Equals("MyISAM", StringComparison.OrdinalIgnoreCase))
                        brokenMyIsam.Add(table);
                }
            }
        }

        report.Add("CHECK TABLE пройден по всем " + tables.Count + " таблицам");

        foreach (var table in brokenMyIsam)
        {
            try
            {
                Execute("REPAIR TABLE " + QuoteIdent(table));
                report.Add("REPAIR TABLE " + table);
            }
            catch (Exception ex)
            {
                report.Fail("REPAIR TABLE " + table + ": " + ex.Message);
            }
        }

        foreach (var table in tables)
        {
            try
            {
                Execute("ANALYZE TABLE " + QuoteIdent(table));
            }
            catch (Exception ex)
            {
                report.Fail("ANALYZE TABLE " + table + ": " + ex.Message);
            }
        }

        report.Add("ANALYZE TABLE пройден по всем таблицам");
    }
}
