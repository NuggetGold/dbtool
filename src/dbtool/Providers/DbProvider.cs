using System.Data.Common;
using System.Text;

namespace DbTool.Core;

public abstract class DbProvider : IDisposable
{
    protected DbConnection Cnn = null!;
    protected Dictionary<string, string> Cfg { get; set; } = new();

    public abstract ProviderKind Kind { get; }
    public abstract string Title { get; }

    public bool IsConnected => Cnn is not null && Cnn.State == ConnectionState.Open;

    public abstract void Open(Dictionary<string, string> cfg);

    public virtual void Close()
    {
        readerCmd?.Dispose();
        readerCmd = null;
        Cnn?.Close();
        Cnn?.Dispose();
        Cnn = null!;
    }

    public void Dispose() => Close();

    public string? CfgSafe(string key)
        => Cfg.TryGetValue(key, out var value) ? value : null;

    public abstract string QuoteIdent(string name);

    protected abstract DbCommand NewCmd(string sql);

    protected abstract DbParameter NewParam(string name, DbColumn? col, object? value);

    protected virtual string ParamName(int ordinal) => "@p" + ordinal;

    protected virtual object? ToParamValue(DbColumn? col, object? value)
    {
        if (value is null)
            return DBNull.Value;

        if (value is Guid g)
            return g.ToString();

        if (value is byte[] bytes && (col is null || col.Kind != ColumnKind.Bytes))
            return Convert.ToBase64String(bytes);

        return value;
    }

    public virtual bool ReadOnly { get; } = false;

    public virtual bool CanDeleteRows { get; } = true;

    public abstract string ServerInfo { get; }

    public abstract List<string> GetTables();

    public abstract List<DbColumn> GetColumns(string table);

    protected abstract string ApplyLimit(string sql, int limit);

    public virtual void DropTable(string table)
    {
        var cmd = NewCmd("DROP TABLE IF EXISTS " + QuoteIdent(table));
        try
        {
            cmd.ExecuteNonQuery();
        }
        finally
        {
            cmd.Dispose();
        }
    }

    public abstract string BuildCreateTableSql(string table, List<DbColumn> cols);

    public virtual void PostImportFix(string table, List<DbColumn> cols)
    {
    }

    public abstract void FixDatabase(FixReport report);

    public virtual string? RecoverTo(string targetPath) => null;

    public long CountRows(string table, string? where)
    {
        var sql = "SELECT COUNT(*) FROM " + QuoteIdent(table);
        if (!string.IsNullOrWhiteSpace(where))
            sql += " WHERE " + where;
        var cmd = NewCmd(sql);
        try
        {
            var value = cmd.ExecuteScalar();
            return Convert.ToInt64(value ?? 0L);
        }
        finally
        {
            cmd.Dispose();
        }
    }

    public DataTable LoadRows(string table, string? where, int limit, out bool truncated)
    {
        var orderCols = GetColumns(table).Where(c => c.IsPrimary).Select(c => QuoteIdent(c.Name)).ToList();
        var sb = new StringBuilder("SELECT * FROM ").Append(QuoteIdent(table));
        if (!string.IsNullOrWhiteSpace(where))
            sb.Append(" WHERE ").Append(where);
        if (orderCols.Count > 0)
            sb.Append(" ORDER BY ").Append(string.Join(", ", orderCols));
        var sql = ApplyLimit(sb.ToString(), limit);

        using var cmd = NewCmd(sql);
        using var reader = cmd.ExecuteReader();
        var result = new DataTable();
        result.Load(reader);
        truncated = limit > 0 && result.Rows.Count >= limit;
        return result;
    }

    private DbCommand? readerCmd;

    public DbDataReader OpenReader(string table)
    {
        readerCmd?.Dispose();
        readerCmd = NewCmd("SELECT * FROM " + QuoteIdent(table));
        return readerCmd.ExecuteReader();
    }

    public void CreateTable(string table, List<DbColumn> cols)
    {
        var cmd = NewCmd(BuildCreateTableSql(table, cols));
        try
        {
            cmd.ExecuteNonQuery();
        }
        finally
        {
            cmd.Dispose();
        }
    }

    public DbResult Execute(string sql)
    {
        var head = SqlSafety.HeadKeyword(sql);
        var usesReader = head is "SELECT" or "PRAGMA" or "SHOW" or "DESCRIBE" or "DESC" or "EXPLAIN";
        var cmd = NewCmd(sql);
        try
        {
            if (usesReader)
            {
                using var reader = cmd.ExecuteReader();
                var data = new DataTable();
                data.Load(reader);
                return new DbResult { Data = data, RowsAffected = data.Rows.Count, Note = "строк: " + data.Rows.Count };
            }

            var affected = cmd.ExecuteNonQuery();
            return new DbResult { RowsAffected = affected, Note = affected < 0 ? "выполнено" : "затронуто строк: " + affected };
        }
        finally
        {
            cmd.Dispose();
        }
    }

    public void InsertRow(string table, List<DbColumn> cols, Dictionary<string, object?> values)
    {
        var writeCols = cols
            .Where(c => !c.IsAutoIncrement && values.ContainsKey(c.Name))
            .ToList();

        if (writeCols.Count == 0)
            throw new InvalidOperationException("Нет значений для вставки: пустую строку нельзя добавить в таблицу, где все колонки автозаполнение.");

        var sql = "INSERT INTO " + QuoteIdent(table) +
                  " (" + string.Join(", ", writeCols.Select(c => QuoteIdent(c.Name))) + ")" +
                  " VALUES (" + string.Join(", ", writeCols.Select((_, i) => ParamName(i))) + ")";

        var cmd = NewCmd(sql);
        try
        {
            for (int i = 0; i < writeCols.Count; i++)
            {
                var col = writeCols[i];
                cmd.Parameters.Add(NewParam(ParamName(i), col, ToParamValue(col, values[col.Name])));
            }
            cmd.ExecuteNonQuery();
        }
        finally
        {
            cmd.Dispose();
        }
    }

    public void WriteRows(string table, List<DbColumn> cols, List<object?[]> rows)
    {
        if (rows.Count == 0)
            return;

        var names = string.Join(", ", cols.Select(c => QuoteIdent(c.Name)));
        var placeholders = string.Join(", ", cols.Select((_, i) => ParamName(i)));
        var sql = "INSERT INTO " + QuoteIdent(table) + " (" + names + ") VALUES (" + placeholders + ")";

        using var cmd = NewCmd(sql);
        using var tx = Cnn.BeginTransaction();
        cmd.Transaction = tx;

        for (int i = 0; i < cols.Count; i++)
            cmd.Parameters.Add(NewParam(ParamName(i), cols[i], DBNull.Value));

        foreach (var row in rows)
        {
            for (int i = 0; i < cols.Count; i++)
                cmd.Parameters[i].Value = ToParamValue(cols[i], row[i]);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public int UpdateCell(string table, List<DbColumn> cols, Dictionary<string, object?> keyValues, string colName, object? newValue)
    {
        var target = cols.FirstOrDefault(c => string.Equals(c.Name, colName, StringComparison.OrdinalIgnoreCase));
        var cmd = NewCmd(string.Empty);
        try
        {
            cmd.Parameters.Add(NewParam(ParamName(0), target, ToParamValue(target, newValue)));
            cmd.CommandText = "UPDATE " + QuoteIdent(table) + " SET " + QuoteIdent(colName) + " = " + ParamName(0) + " WHERE " + BuildKeyWhere(cmd, cols, keyValues, 1);
            return cmd.ExecuteNonQuery();
        }
        finally
        {
            cmd.Dispose();
        }
    }

    public int DeleteRow(string table, List<DbColumn> cols, Dictionary<string, object?> keyValues)
    {
        var cmd = NewCmd(string.Empty);
        try
        {
            cmd.CommandText = "DELETE FROM " + QuoteIdent(table) + " WHERE " + BuildKeyWhere(cmd, cols, keyValues, 0);
            return cmd.ExecuteNonQuery();
        }
        finally
        {
            cmd.Dispose();
        }
    }

    protected string BuildKeyWhere(DbCommand cmd, List<DbColumn> cols, Dictionary<string, object?> keyValues, int startOrdinal)
    {
        var parts = new List<string>();
        int ordinal = startOrdinal;
        foreach (var col in cols)
        {
            if (!keyValues.TryGetValue(col.Name, out var raw))
                continue;

            if (raw is null)
            {
                parts.Add(QuoteIdent(col.Name) + " IS NULL");
                continue;
            }

            parts.Add(QuoteIdent(col.Name) + " = " + ParamName(ordinal));
            cmd.Parameters.Add(NewParam(ParamName(ordinal), col, ToParamValue(col, raw)));
            ordinal++;
        }

        if (parts.Count == 0)
            throw new InvalidOperationException("Не удалось построить ключ строки.");
        return string.Join(" AND ", parts);
    }

    internal static Dictionary<string, object?> RowToKeyValues(List<DbColumn> cols, DataRow row)
    {
        var result = new Dictionary<string, object?>();
        foreach (var col in cols)
        {
            if (!row.Table.Columns.Contains(col.Name))
                return new Dictionary<string, object?>();
            result[col.Name] = row[col.Name] is DBNull ? null : row[col.Name];
        }
        return result;
    }
}
