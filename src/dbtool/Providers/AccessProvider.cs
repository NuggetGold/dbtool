using System.Data.Common;
using System.Data.OleDb;

namespace DbTool.Core;

public class AccessProvider : DbProvider
{
    private bool _isExcel;

    public bool IsExcelFile => _isExcel;

    private string File => Cfg.GetValueOrDefault("file", string.Empty);

    public override ProviderKind Kind => ProviderKind.Access;
    public override string Title => _isExcel ? "Excel (через ACE)" : "Access (через ACE/Jet)";
    public override bool ReadOnly => _isExcel;
    public override bool CanDeleteRows => !_isExcel;

    public override void Open(Dictionary<string, string> cfg)
    {
        Cfg = cfg;
        var path = cfg.GetValueOrDefault("file", string.Empty);
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            throw new FileNotFoundException("Файл Access/Excel не найден: " + path);

        var ext = Path.GetExtension(path).ToLowerInvariant();
        _isExcel = ext is ".xlsx" or ".xls" or ".xlsb";

        var attempts = new List<string>();
        if (_isExcel)
        {
            attempts.Add("Provider=Microsoft.ACE.OLEDB.16.0;Data Source=" + path + ";Extended Properties=\"Excel 12.0 Xml;HDR=YES;IMEX=0\"");
            attempts.Add("Provider=Microsoft.ACE.OLEDB.12.0;Data Source=" + path + ";Extended Properties=\"Excel 8.0;HDR=YES;IMEX=0\"");
        }
        else if (ext == ".accdb")
        {
            attempts.Add("Provider=Microsoft.ACE.OLEDB.16.0;Data Source=" + path + ";");
            attempts.Add("Provider=Microsoft.ACE.OLEDB.12.0;Data Source=" + path + ";");
        }
        else
        {
            attempts.Add("Provider=Microsoft.ACE.OLEDB.16.0;Data Source=" + path + ";");
            attempts.Add("Provider=Microsoft.ACE.OLEDB.12.0;Data Source=" + path + ";");
            attempts.Add("Provider=Microsoft.Jet.OLEDB.4.0;Data Source=" + path + ";");
        }

        OleDbConnection open = null!;
        Exception last = new InvalidOperationException("Драйверы ACE/Jet не найдены.");
        foreach (var cs in attempts)
        {
            try
            {
                var cnn = new OleDbConnection(cs);
                cnn.Open();
                open = cnn;
                break;
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        if (open is null)
        {
            throw new InvalidOperationException(
                "Не удалось открыть файл Access/Excel. Установите драйвер Microsoft Access Database Engine (ACE), совместимый по разрядности с программой." +
                " Последняя ошибка: " + last.Message);
        }

        Cnn = open;
    }

    public override string QuoteIdent(string name) => "[" + name + "]";

    protected override DbCommand NewCmd(string sql) => new OleDbCommand(sql, (OleDbConnection)Cnn);

    protected override string ParamName(int ordinal) => "?";

    protected override DbParameter NewParam(string name, DbColumn? col, object? value)
    {
        value = value ?? DBNull.Value;

        if (col is not null && col.Kind == ColumnKind.Bool && value is short or int or long)
            value = Convert.ToInt64(value) != 0L;

        if (col is not null && col.Kind == ColumnKind.Date && value is string s)
            value = DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : s;

        return new OleDbParameter { Value = value };
    }

    protected override string ApplyLimit(string sql, int limit)
        => limit > 0 ? sql.Insert(7, "TOP " + limit + " ") : sql;

    public override string ServerInfo
    {
        get
        {
            var size = new System.IO.FileInfo(File).Length / 1024.0;
            return string.Format("OleDb {0}, файл: {1}, размер: {2:F1} КБ, объектов: {3}, режим: {4}",
                Cnn.ServerVersion, Path.GetFileName(File), size, GetTables().Count,
                _isExcel ? "только просмотр" : "полный редактор");
        }
    }

    public override List<string> GetTables()
    {
        var schema = ((OleDbConnection)Cnn).GetOleDbSchemaTable(OleDbSchemaGuid.Tables, new object?[] { null, null, null, "TABLE" });
        var result = new List<string>();
        if (schema is null)
            return result;
        foreach (DataRow row in schema.Rows)
        {
            var name = row["TABLE_NAME"].ToString() ?? string.Empty;
            if (name.StartsWith("MSys", StringComparison.OrdinalIgnoreCase))
                continue;
            if (_isExcel && !name.EndsWith("$", StringComparison.Ordinal))
                continue;
            result.Add(name);
        }
        return result;
    }

    public override List<DbColumn> GetColumns(string table)
    {
        var schema = ((OleDbConnection)Cnn).GetOleDbSchemaTable(OleDbSchemaGuid.Columns, new object?[] { null, null, table });
        var cols = new List<DbColumn>();
        var pk = GetPrimaryKeys(table);

        if (schema is null)
            return cols;

        var sorted = schema.AsEnumerable().OrderBy(r => Convert.ToInt32(r["ORDINAL_POSITION"]));
        foreach (var row in sorted)
        {
            var name = row["COLUMN_NAME"].ToString() ?? string.Empty;
            var dataType = Convert.ToInt32(row["DATA_TYPE"]);
            var col = new DbColumn
            {
                Name = name,
                NativeType = ((OleDbType)dataType).ToString(),
                Kind = MapKind(dataType),
                IsPrimary = pk.Contains(name),
                IsNullable = string.Equals(row["IS_NULLABLE"].ToString(), "True", StringComparison.OrdinalIgnoreCase),
                Default = row["COLUMN_DEFAULT"] == DBNull.Value ? null : row["COLUMN_DEFAULT"].ToString()
            };
            if (col.IsPrimary)
                col.IsNullable = false;
            cols.Add(col);
        }

        return cols;
    }

    private HashSet<string> GetPrimaryKeys(string table)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var schema = ((OleDbConnection)Cnn).GetOleDbSchemaTable(OleDbSchemaGuid.Primary_Keys, new object?[] { null, null, table });
            if (schema is null)
                return set;
            foreach (DataRow row in schema.Rows)
                set.Add(row["PK_COLUMN_NAME"].ToString() ?? string.Empty);
        }
        catch (Exception ex)
        {
            Log.Warn("Схема ключей Access не прочитана (" + table + "): " + ex.Message);
        }
        return set;
    }

    private static ColumnKind MapKind(int dataType) => dataType switch
    {
        2 or 3 or 17 or 18 or 19 => ColumnKind.Int,
        4 or 5 => ColumnKind.Double,
        6 or 131 => ColumnKind.Decimal,
        7 or 133 or 134 or 135 => ColumnKind.Date,
        11 => ColumnKind.Bool,
        72 => ColumnKind.Guid,
        128 or 204 or 205 => ColumnKind.Bytes,
        _ => ColumnKind.Text
    };

    public override string BuildCreateTableSql(string table, List<DbColumn> cols)
    {
        var singlePk = cols.Count(c => c.IsPrimary) == 1;
        var parts = new List<string>();

        foreach (var col in cols)
        {
            var isCounter = singlePk && col.IsPrimary && col.IsAutoIncrement;
            var type = isCounter ? "COUNTER" : MapType(col);
            var part = QuoteIdent(col.Name) + " " + type;
            parts.Add(part);
        }

        var pkCols = cols.Where(c => c.IsPrimary).ToList();
        if (pkCols.Count > 0)
            parts.Add("CONSTRAINT __pk PRIMARY KEY (" + string.Join(", ", pkCols.Select(c => QuoteIdent(c.Name))) + ")");

        return "CREATE TABLE " + QuoteIdent(table) + " (" + string.Join(", ", parts) + ")";
    }

    private static string MapType(DbColumn col)
    {
        if (col.IsPrimary && col.Kind == ColumnKind.Text)
            return "TEXT(255)";
        return col.Kind switch
        {
            ColumnKind.Text => "LONGTEXT",
            ColumnKind.Int => "INTEGER",
            ColumnKind.Long => "DECIMAL(28,0)",
            ColumnKind.Double => "DOUBLE",
            ColumnKind.Decimal => "CURRENCY",
            ColumnKind.Bool => "YESNO",
            ColumnKind.Date => "DATETIME",
            ColumnKind.Bytes => "LONGBINARY",
            ColumnKind.Guid => "TEXT(36)",
            _ => "TEXT(255)"
        };
    }

    public override void DropTable(string table)
    {
        var exists = GetTables().Any(t => string.Equals(t, table, StringComparison.OrdinalIgnoreCase));
        if (!exists)
            return;

        var cmd = NewCmd("DROP TABLE " + QuoteIdent(table));
        cmd.ExecuteNonQuery();
        cmd.Dispose();
    }

    public override void FixDatabase(FixReport report)
    {
        if (_isExcel)
        {
            report.Add("Excel-файл: Compact&Repair неприменим, драйвер читает листы напрямую");
            return;
        }

        var path = this.File;
        var temp = path + ".repair";

        Close();

        try
        {
            if (System.IO.File.Exists(temp))
                System.IO.File.Delete(temp);

            var compacted = false;

            foreach (var progId in new[] { "JRO.JetEngine", "DAO.DBEngine.120", "DAO.DBEngine.36" })
            {
                var type = Type.GetTypeFromProgID(progId);
                if (type is null)
                    continue;

                foreach (var srcConn in ConnVariants(path))
                {
                    foreach (var dstConn in ConnVariants(temp))
                    {
                        try
                        {
                            dynamic engine = Activator.CreateInstance(type)!;
                            engine.CompactDatabase(srcConn, dstConn);
                            compacted = true;
                            break;
                        }
                        catch (Exception ex)
                        {
                            Log.Warn(progId + ": " + ex.Message);
                        }
                    }
                    if (compacted)
                        break;
                }

                if (compacted)
                    break;
            }

            if (!compacted)
            {
                report.Fail("Compact&Repair недоступен: COM-объекты JRO/DAO не найдены. Файл не изменён.");
            }
            else
            {
                System.IO.File.Move(temp, path, true);
                report.Add("Compact&Repair выполнен: " + path);
            }
        }
        catch (Exception ex)
        {
            report.Fail("Compact&Repair не выполнен: " + ex.Message);
        }
        finally
        {
            try
            {
                Open(Cfg);
            }
            catch (Exception ex)
            {
                report.Fail("Не удалось переоткрыть соединение: " + ex.Message);
            }
        }
    }

    private static string[] ConnVariants(string file) => new[]
    {
        "Provider=Microsoft.ACE.OLEDB.12.0;Data Source=" + file + ";OLE DB Services=-2;",
        "Provider=Microsoft.ACE.OLEDB.16.0;Data Source=" + file + ";OLE DB Services=-2;",
        "Provider=Microsoft.Jet.OLEDB.4.0;Data Source=" + file + ";OLE DB Services=-2;"
    };
}
