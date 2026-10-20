using System.Globalization;

namespace DbTool.Core;

public static class ValueParser
{
    public static object? Parse(DbColumn col, string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        switch (col.Kind)
        {
            case ColumnKind.Int:
                return int.Parse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);

            case ColumnKind.Long:
                return long.Parse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);

            case ColumnKind.Double:
                return double.Parse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);

            case ColumnKind.Decimal:
                return decimal.Parse(text.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture);

            case ColumnKind.Bool:
                return ParseBool(text);

            case ColumnKind.Date:
                return DateTime.Parse(text.Trim());

            case ColumnKind.Guid:
                return new Guid(text.Trim());

            default:
                return text;
        }
    }

    public static bool ParseBool(string text)
    {
        var s = text.Trim().ToLowerInvariant();
        return s switch
        {
            "true" or "1" or "да" or "yes" or "истина" => true,
            "false" or "0" or "нет" or "no" or "ложь" => false,
            _ => throw new FormatException("Логическое значение: true / false / 1 / 0 / да / нет")
        };
    }
}
