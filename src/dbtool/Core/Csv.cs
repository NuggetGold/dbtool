using System.Globalization;
using System.Text;

namespace DbTool.Core;

public static class Csv
{
    public static void Save(DataTable data, string path, char delimiter, Encoding encoding)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(delimiter, data.Columns.Cast<DataColumn>().Select(c => Escape(c.ColumnName, delimiter))));
        foreach (DataRow row in data.Rows)
            sb.AppendLine(string.Join(delimiter, row.ItemArray.Select(v => Escape(ToText(v), delimiter))));
        File.WriteAllText(path, sb.ToString(), encoding);
    }

    private static string ToText(object? value)
    {
        if (value is null || value is DBNull)
            return string.Empty;

        if (value is byte[] bytes)
            return "0x" + Convert.ToHexString(bytes);

        if (value is DateTime dt)
            return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        if (value is decimal dec)
            return dec.ToString(CultureInfo.InvariantCulture);

        if (value is double d)
            return d.ToString("G17", CultureInfo.InvariantCulture);

        if (value is float f)
            return f.ToString("G9", CultureInfo.InvariantCulture);

        return value.ToString() ?? string.Empty;
    }

    private static string Escape(string text, char delimiter)
    {
        if (text.Contains(delimiter) || text.Contains('"') || text.Contains('\n') || text.Contains('\r'))
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        return text;
    }
}
