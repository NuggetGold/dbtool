namespace DbTool.Core;

public enum ColumnKind
{
    Text,
    Int,
    Long,
    Double,
    Decimal,
    Bool,
    Date,
    Bytes,
    Guid,
    Unknown
}

public class DbColumn
{
    public string Name { get; set; } = string.Empty;
    public string NativeType { get; set; } = string.Empty;
    public ColumnKind Kind { get; set; } = ColumnKind.Text;
    public bool IsPrimary { get; set; }
    public bool IsNullable { get; set; } = true;
    public bool IsAutoIncrement { get; set; }
    public string? Default { get; set; }
    public bool IsBytes => Kind == ColumnKind.Bytes;

    public static ColumnKind ParseKind(string nativeType)
    {
        var t = (nativeType ?? string.Empty).ToLowerInvariant();

        if (t.Contains("uuid") || t.Contains("uniqueidentifier") || t.Contains("guid"))
            return ColumnKind.Guid;

        if (t.Contains("blob") || t.Contains("bytea") || t.Contains("binary") ||
            t.Contains("image") || t.Contains("longbinary") || t.Contains("ole object"))
            return ColumnKind.Bytes;

        if (t.Contains("bool") || t.Contains("yesno") || t.Contains("logical"))
            return ColumnKind.Bool;

        if (t.Contains("bigint") || t.Contains("long integer") || t.Contains("int64"))
            return ColumnKind.Long;

        if (t.Contains("int") || t.Contains("smallint") || t.Contains("tinyint") ||
            t.Contains("byte") || t.Contains("counter") || t.Contains("serial"))
            return ColumnKind.Int;

        if (t.Contains("numeric") || t.Contains("decimal") || t.Contains("currency") || t.Contains("money"))
            return ColumnKind.Decimal;

        if (t.Contains("real") || t.Contains("float") || t.Contains("double") ||
            t.Contains("single") || t.Contains("symbol"))
            return ColumnKind.Double;

        if (t.Contains("date") || t.Contains("time") || t.Contains("timestamp"))
            return ColumnKind.Date;

        if (t.Contains("char") || t.Contains("text") || t.Contains("clob") ||
            t.Contains("memo") || t.Contains("string"))
            return ColumnKind.Text;

        return ColumnKind.Unknown;
    }
}

public class DbResult
{
    public int RowsAffected { get; set; } = -1;
    public DataTable? Data { get; set; }
    public string Note { get; set; } = string.Empty;
}
