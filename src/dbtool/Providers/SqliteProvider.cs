using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace DbTool.Core;

public class SqliteProvider : DbProvider
{
    private string _file = string.Empty;

    public string FilePath => _file;

    public override ProviderKind Kind => ProviderKind.Sqlite;
    public override string Title => "SQLite";

    public override void Open(Dictionary<string, string> cfg)
    {
        Cfg = cfg;
        _file = cfg.TryGetValue("file", out var f) ? f : string.Empty;

        var create = cfg.TryGetValue("create", out var c) && (c == "1" || c == "true");
        if (string.IsNullOrWhiteSpace(_file))
            throw new FileNotFoundException("Не указан файл базы SQLite.");
        if (!File.Exists(_file) && !create)
            throw new FileNotFoundException("Файл базы SQLite не найден: " + _file);

        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = _file,
            Mode = File.Exists(_file) ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        };

        Cnn = new SqliteConnection(cs.ToString());
        Cnn.Open();
    }

    public override string QuoteIdent(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

    protected override DbCommand NewCmd(string sql) => new SqliteCommand(sql, (SqliteConnection)Cnn);

    protected override DbParameter NewParam(string name, DbColumn? col, object? value)
        => new SqliteParameter(name, value ?? DBNull.Value);

    protected override string ApplyLimit(string sql, int limit)
        => limit > 0 ? sql + " LIMIT " + limit : sql;

    public override string ServerInfo
    {
        get
        {
            var version = Execute("SELECT sqlite_version()").Data!.Rows[0][0].ToString();
            var size = new FileInfo(_file).Length / 1024.0;
            return string.Format("SQLite {0}, файл: {1}, размер: {2:F1} КБ, таблиц: {3}",
                version, Path.GetFileName(_file), size, GetTables().Count);
        }
    }

    public override List<string> GetTables()
    {
        var data = Execute("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name").Data!;
        return data.AsEnumerable().Select(r => r[0].ToString()!).ToList();
    }

    public override List<DbColumn> GetColumns(string table)
    {
        var data = Execute("PRAGMA table_info(" + QuoteIdent(table) + ")").Data!;
        var cols = new List<DbColumn>();
        foreach (DataRow row in data.AsEnumerable())
        {
            var col = new DbColumn
            {
                Name = row["name"].ToString() ?? string.Empty,
                NativeType = row["type"].ToString() ?? string.Empty,
                Kind = DbColumn.ParseKind(row["type"].ToString() ?? string.Empty),
                IsPrimary = Convert.ToInt32(row["pk"]) > 0,
                IsNullable = Convert.ToInt32(row["notnull"]) == 0,
                Default = row["dflt_value"] == DBNull.Value ? null : row["dflt_value"].ToString()
            };
            cols.Add(col);
        }

        if (cols.Count(c => c.IsPrimary) == 1)
        {
            var pk = cols.First(c => c.IsPrimary);
            if (pk.Kind == ColumnKind.Int)
                pk.IsAutoIncrement = true;
        }

        return cols;
    }

    public override string BuildCreateTableSql(string table, List<DbColumn> cols)
    {
        var parts = new List<string>();
        foreach (var col in cols)
        {
            var isSingleAutoPk = col.IsPrimary && col.IsAutoIncrement && cols.Count(c => c.IsPrimary) == 1;
            var type = isSingleAutoPk ? "INTEGER" : MapType(col.Kind);
            var part = QuoteIdent(col.Name) + " " + type;
            if (!col.IsNullable && !(col.IsPrimary && col.IsAutoIncrement))
                part += " NOT NULL";
            parts.Add(part);
        }

        var autoPk = cols.Count(c => c.IsPrimary && c.IsAutoIncrement) == 1;
        var pkCols = cols.Where(c => c.IsPrimary).ToList();
        if (!autoPk && pkCols.Count > 0)
            parts.Add("PRIMARY KEY (" + string.Join(", ", pkCols.Select(c => QuoteIdent(c.Name))) + ")");

        return "CREATE TABLE " + QuoteIdent(table) + " (" + string.Join(", ", parts) + ")";
    }

    private static string MapType(ColumnKind kind) => kind switch
    {
        ColumnKind.Int or ColumnKind.Long => "INTEGER",
        ColumnKind.Double => "REAL",
        ColumnKind.Decimal => "NUMERIC",
        ColumnKind.Bool => "INTEGER",
        ColumnKind.Date => "TEXT",
        ColumnKind.Bytes => "BLOB",
        _ => "TEXT"
    };

    public override void FixDatabase(FixReport report)
    {
        try
        {
            using var chk = NewCmd("PRAGMA wal_checkpoint(TRUNCATE)");
            chk.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            report.Fail("wal_checkpoint не выполнен: " + ex.Message);
        }

        var bak = _file + ".bak";
        try
        {
            File.Copy(_file, bak, true);
            report.Add("Резервная копия создана: " + bak);
        }
        catch (Exception ex)
        {
            report.Fail("Резервная копия не создана: " + ex.Message);
        }

        var integrity = Execute("PRAGMA integrity_check").Data!;
        var issues = integrity.AsEnumerable()
            .Select(r => r[0].ToString() ?? string.Empty)
            .Where(s => !string.Equals(s, "ok", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (issues.Count == 0)
            report.Add(" integrity_check: проблем не найдено");
        else
        {
            foreach (var issue in issues.Take(50))
                report.Fail("integrity_check: " + issue);
            report.NeedsRecovery = true;
        }

        var fk = Execute("PRAGMA foreign_key_check").Data!;
        if (fk.Rows.Count == 0)
            report.Add("Foreign keys: нарушений нет");
        else
            report.Fail(string.Format("Foreign keys: нарушено ссылок: {0} (сиротские ключи)", fk.Rows.Count));

        try
        {
            using var vacuum = NewCmd("VACUUM");
            vacuum.ExecuteNonQuery();
            report.Add("VACUUM выполнен");
        }
        catch (Exception ex)
        {
            report.Fail("VACUUM не выполнен: " + ex.Message);
        }

        try
        {
            using var reindex = NewCmd("REINDEX");
            reindex.ExecuteNonQuery();
            report.Add("REINDEX выполнен");
        }
        catch (Exception ex)
        {
            report.Fail("REINDEX не выполнен: " + ex.Message);
        }
    }

    public override string? RecoverTo(string targetPath)
    {
        if (File.Exists(targetPath))
            throw new InvalidOperationException("Файл уже существует: " + targetPath);

        FixedCopy(_file, targetPath);
        return targetPath;
    }

    private void FixedCopy(string source, string target)
    {
        var srcBuilder = new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadOnly };
        using var src = new SqliteConnection(srcBuilder.ToString());
        src.Open();

        var tables = DataTableReader(
            new SqliteCommand("SELECT name, sql FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name", src));

        var dstBuilder = new SqliteConnectionStringBuilder { DataSource = target, Mode = SqliteOpenMode.ReadWriteCreate };
        using var dst = new SqliteConnection(dstBuilder.ToString());
        dst.Open();

        foreach (DataRow t in tables.Rows)
        {
            var name = t[0].ToString()!;
            var schema = t[1].ToString();
            if (string.IsNullOrWhiteSpace(schema))
                continue;

            try
            {
                using var create = new SqliteCommand(schema, dst);
                create.ExecuteNonQuery();

                var quoted = "\"" + name.Replace("\"", "\"\"") + "\"";
                using var read = new SqliteCommand("SELECT * FROM " + quoted, src);
                using var reader = read.ExecuteReader();
                var rows = new DataTable();
                rows.Load(reader);

                if (rows.Rows.Count == 0)
                {
                    Log.Info("Восстановление: " + name + ": строки не читаются, перенесена только схема");
                }
                else
                {
                    InsertAll(dst, name, rows);
                    Log.Info(string.Format("Восстановление: {0}: перенесено строк {1}", name, rows.Rows.Count));
                }
            }
            catch (Exception ex)
            {
                Log.Error("Восстановление: таблица " + name + " пропущена", ex);
            }
        }

        using var vacuum = new SqliteCommand("VACUUM", dst);
        vacuum.ExecuteNonQuery();
    }

    private static DataTable DataTableReader(SqliteCommand cmd)
    {
        using var r = cmd.ExecuteReader();
        var t = new DataTable();
        t.Load(r);
        return t;
    }

    private static void InsertAll(SqliteConnection dst, string table, DataTable rows)
    {
        var quoted = "\"" + table.Replace("\"", "\"\"") + "\"";
        var cols = rows.Columns.Cast<DataColumn>().Select(c => "\"" + c.ColumnName.Replace("\"", "\"\"") + "\"").ToList();
        var sql = "INSERT INTO " + quoted + " (" + string.Join(", ", cols) + ") VALUES (" +
                  string.Join(", ", cols.Select(c => "@p" + cols.IndexOf(c))) + ")";

        using var tx = dst.BeginTransaction();
        using var insert = new SqliteCommand(sql, (SqliteConnection)dst, (SqliteTransaction)tx);
        foreach (var col in cols)
            insert.Parameters.Add(new SqliteParameter("@p" + cols.IndexOf(col), DBNull.Value));

        foreach (DataRow row in rows.Rows)
        {
            for (int i = 0; i < cols.Count; i++)
                insert.Parameters[i].Value = row[i] ?? DBNull.Value;
            insert.ExecuteNonQuery();
        }

        tx.Commit();
    }
}
