namespace DbTool.Core;

public static class DbConverter
{
    private const int ChunkSize = 500;

    public static List<string> ConvertTables(
        DbProvider source,
        DbProvider target,
        IReadOnlyList<string> tables,
        Action<string, long, int, int> onProgress,
        Func<bool> isCancelled)
    {
        var failures = new List<string>();
        int total = tables.Count;
        int index = 0;

        foreach (var table in tables)
        {
            index++;
            if (isCancelled())
            {
                Log.Warn("Конвертация остановлена пользователем (таблица " + table + " не начата)");
                return failures;
            }

            var cols = source.GetColumns(table);
            var accessTarget = target.Kind == ProviderKind.Access;
            var writeCols = cols.Where(c => !(accessTarget && c.IsAutoIncrement)).ToList();
            var ordinals = new int[writeCols.Count];

            if (accessTarget && cols.Any(c => c.IsAutoIncrement))
                Log.Warn("Цель Access: автоинкрементные значения таблицы " + table +
                         " не переносятся явно, значения ключей в целевой базе сгенерируются заново");

            try
            {
                target.DropTable(table);
                target.CreateTable(table, cols);
                onProgress(table, 0, index, total);

                using var reader = source.OpenReader(table);
                for (int i = 0; i < writeCols.Count; i++)
                    ordinals[i] = reader.GetOrdinal(writeCols[i].Name);

                var buffer = new List<object?[]>(ChunkSize);
                long copied = 0;

                while (reader.Read())
                {
                    var row = new object?[writeCols.Count];
                    for (int i = 0; i < writeCols.Count; i++)
                        row[i] = reader.IsDBNull(ordinals[i]) ? null : reader.GetValue(ordinals[i]);
                    buffer.Add(row);

                    if (buffer.Count >= ChunkSize)
                    {
                        target.WriteRows(table, writeCols, buffer);
                        copied += buffer.Count;
                        buffer.Clear();
                        onProgress(table, copied, index, total);

                        if (isCancelled())
                        {
                            Log.Warn(string.Format("Конвертация остановлена: {0}: перенесено строк {1}", table, copied));
                            target.PostImportFix(table, cols);
                            return failures;
                        }
                    }
                }

                target.WriteRows(table, writeCols, buffer);
                copied += buffer.Count;
                onProgress(table, copied, index, total);
                target.PostImportFix(table, cols);
                Log.Info(string.Format("Конвертация: {0}: перенесено строк {1}", table, copied));
            }
            catch (Exception ex)
            {
                failures.Add(table);
                Log.Error("Конвертация: таблица " + table + " не перенесена", ex);
                DropBroken(target, table);
            }
        }

        Log.Info("Конвертация завершена: таблиц заявлено " + total +
                 (failures.Count == 0 ? ", все перенесены" : ", не перенесены: " + string.Join(", ", failures)));
        return failures;
    }

    private static void DropBroken(DbProvider target, string table)
    {
        try
        {
            target.DropTable(table);
            Log.Info("Конвертация: неполная таблица " + table + " удалена из целевой базы");
        }
        catch (Exception dropEx)
        {
            Log.Warn("Конвертация: неполную таблицу " + table + " не удалось удалить: " + dropEx.Message);
        }
    }
}
