namespace DbTool.Core;

public static class SqlSplitter
{
    public static List<string> Split(string sql)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inSingle = false, inDouble = false, inBacktick = false, inBracket = false, lineComment = false, blockComment = false;

        for (int i = 0; i < sql.Length; i++)
        {
            char c = sql[i];
            char next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (lineComment)
            {
                if (c == '\n' || c == '\r')
                {
                    lineComment = false;
                    current.Append(c);
                }
                continue;
            }

            if (blockComment)
            {
                if (c == '*' && next == '/')
                {
                    blockComment = false;
                    i++;
                }
                continue;
            }

            if (!inSingle && !inDouble && !inBacktick && !inBracket)
            {
                if (c == '-' && next == '-')
                {
                    lineComment = true;
                    continue;
                }

                if (c == '#' && (next is ' ' or '\t' or '\n' or '\r' or '\0'))
                {
                    lineComment = true;
                    continue;
                }

                if (c == '/' && next == '*')
                {
                    blockComment = true;
                    i++;
                    continue;
                }
            }

            if (c == '\'' && !inDouble && !inBacktick && !inBracket)
            {
                if (inSingle && next == '\'')
                {
                    current.Append('\'').Append('\'');
                    i++;
                    continue;
                }
                inSingle = !inSingle;
            }
            else if (c == '"' && !inSingle && !inBacktick)
            {
                inDouble = !inDouble;
            }
            else if (c == '`' && !inSingle && !inDouble)
            {
                inBacktick = !inBacktick;
            }
            else if (c == '[' && !inSingle && !inDouble && !inBacktick)
            {
                inBracket = true;
            }
            else if (c == ']' && inBracket)
            {
                inBracket = false;
            }

            if (c == ';' && !inSingle && !inDouble && !inBacktick && !inBracket)
            {
                var piece = current.ToString().Trim();
                if (piece.Length > 0)
                    result.Add(piece);
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        var tail = current.ToString().Trim();
        if (tail.Length > 0)
            result.Add(tail);

        return result;
    }
}

public static class SqlSafety
{
    public static string StripLiterals(string sql)
    {
        var sb = new StringBuilder();
        bool inSingle = false, inDouble = false, lineComment = false, blockComment = false;

        for (int i = 0; i < sql.Length; i++)
        {
            char c = sql[i];
            char next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (lineComment)
            {
                if (c == '\n' || c == '\r') lineComment = false;
                continue;
            }

            if (blockComment)
            {
                if (c == '*' && next == '/')
                {
                    blockComment = false;
                    i++;
                }
                continue;
            }

            if (!inSingle && !inDouble)
            {
                if (c == '-' && next == '-')
                {
                    lineComment = true;
                    continue;
                }

                if (c == '#' && (next is ' ' or '\t' or '\n' or '\r' or '\0'))
                {
                    lineComment = true;
                    continue;
                }

                if (c == '/' && next == '*')
                {
                    blockComment = true;
                    i++;
                    continue;
                }
            }

            if (c == '\'')
            {
                if (inSingle && next == '\'')
                {
                    i++;
                    continue;
                }
                inSingle = !inSingle;
                continue;
            }

            if (c == '"')
            {
                inDouble = !inDouble;
                continue;
            }

            if (inSingle || inDouble)
                sb.Append(' ');
            else
                sb.Append(c);
        }

        return sb.ToString();
    }

    public static bool IsDangerous(string sql, out string reason)
    {
        reason = string.Empty;
        var clean = StripLiterals(sql);
        var words = clean
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.TrimStart('(', '[').TrimEnd(')', ']', ',').ToUpperInvariant())
            .ToList();

        if (words.Count == 0)
            return false;

        var head = words[0];

        bool hasWhere = words.Any(w => w == "WHERE");

        if (head == "DELETE" && !hasWhere)
        {
            reason = "DELETE без WHERE удалит все строки таблицы";
            return true;
        }

        if (head == "UPDATE" && !hasWhere)
        {
            reason = "UPDATE без WHERE изменит все строки таблицы";
            return true;
        }

        if (head == "TRUNCATE")
        {
            reason = "TRUNCATE очистит таблицу целиком";
            return true;
        }

        if (head == "DROP")
        {
            reason = "DROP удалит таблицу или объект";
            return true;
        }

        if (head == "ALTER")
        {
            reason = "ALTER изменит структуру таблицы";
            return true;
        }

        return false;
    }

    public static string HeadKeyword(string sql)
    {
        var text = sql.TrimStart();

        while (true)
        {
            if (text.StartsWith("--", StringComparison.Ordinal))
            {
                var idx = text.IndexOf('\n');
                text = idx < 0 ? string.Empty : text[(idx + 1)..].TrimStart();
                continue;
            }

            if (text.StartsWith("/*", StringComparison.Ordinal))
            {
                var end = text.IndexOf("*/", StringComparison.Ordinal);
                text = end < 0 ? string.Empty : text[(end + 2)..].TrimStart();
                continue;
            }

            break;
        }

        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch) || ch == '(')
                break;
            sb.Append(ch);
        }
        return sb.ToString().TrimEnd(';').ToUpperInvariant();
    }
}
