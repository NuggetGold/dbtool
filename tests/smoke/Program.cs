using System.Data;
using System.Text;
using DbTool.Core;

internal static class Program
{
    private static int failCount;

    private static void Check(bool condition, string name)
    {
        if (condition)
        {
            Console.WriteLine("OK    " + name);
        }
        else
        {
            failCount++;
            Console.WriteLine("FAIL  " + name);
        }
    }

    private static void Main()
    {
        var baseDir = AppContext.BaseDirectory;
        var dir = Path.Combine(baseDir, "..");
        var srcFile = Path.GetFullPath(dir + "\\..", Path.Combine(baseDir));

        var original = Console.OutputEncoding;
        Console.OutputEncoding = Encoding.UTF8;

        var tempDir = Path.Combine(Path.GetTempPath(), "dbtool-smoke-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        var sourceDb = Path.Combine(tempDir, "source.db");
        var targetDb = Path.Combine(tempDir, "target.db");

        try
        {
            CreateSource(sourceDb);

            using (var provider = new DbTool.Core.SqliteProvider())
            {
                provider.Open(new Dictionary<string, string> { { "file", sourceDb } });

                Check(provider.IsConnected, "подключение к SQLite");

                var tables = provider.GetTables();
                Check(tables.Contains("users") && tables.Contains("orders"), "список таблиц содержит users/orders");

                var usersCols = provider.GetColumns("users");
                var idCol = usersCols.First(c => c.Name == "id");
                Check(idCol.IsPrimary && idCol.IsAutoIncrement && idCol.Kind == ColumnKind.Int, "users.id: PK + автоцелое");

                var info = provider.ServerInfo;
                Check(info.Contains("SQLite"), "ServerInfo: " + info);

                var rows = provider.LoadRows("users", null, 5000, out var truncated);
                Check(rows.Rows.Count == 3 && !truncated, "LoadRows users: 3 строки без усечения, получено " + rows.Rows.Count);

                var filtered = provider.LoadRows("users", "id >= 2", 5000, out _);
                Check(filtered.Rows.Count == 2, "LoadRows с WHERE: 2 строки, получено " + filtered.Rows.Count);

                var count = provider.CountRows("users", null);
                Check(count == 3, "CountRows users = 3");

                provider.InsertRow("users", usersCols, new Dictionary<string, object?>
                {
                    { "name", "Кирилл" },
                    { "age", 40 },
                    { "score", 1.5 },
                    { "is_active", 1 }
                });
                Check(provider.CountRows("users", null) == 4, "InsertRow: строк стало 4");

                var both = provider.LoadRows("users", "score = 1.5", 5000, out _);
                Check(both.Rows.Count == 1, "вставленная строка найдена по score");

                var inserted = provider.LoadRows("users", "score = 1.5", 5000, out _).Rows[0];
                var keyIds = new Dictionary<string, object?> { { "id", inserted["id"] } };
                var updated = provider.UpdateCell("users", usersCols, keyIds, "name", "Кирилл-б");
                Check(updated == 1, "UpdateCell вернул затронутые строки: 1 (получено " + updated + ")");
                var renamed = provider.LoadRows("users", "score = 1.5", 5000, out _).Rows[0];
                Check(renamed["name"].ToString() == "Кирилл-б", "UpdateCell изменил имя");

                var missing = provider.UpdateCell("users", usersCols, new Dictionary<string, object?> { { "id", -5 } }, "name", "x");
                Check(missing == 0, "UpdateCell по отсутствующему ключу вернул 0 (получено " + missing + ")");

                provider.UpdateCell("users", usersCols, keyIds, "age", null);
                var nulled = provider.LoadRows("users", "score = 1.5", 5000, out _).Rows[0];
                Check(nulled["age"] is DBNull, "UpdateCell NULL в age");

                var removed = provider.DeleteRow("users", usersCols, keyIds);
                Check(removed == 1, "DeleteRow вернул затронутые строки: 1 (получено " + removed + ")");
                Check(provider.CountRows("users", null) == 3, "DeleteRow: вернулось 3 строки");

                var select = provider.Execute("SELECT COUNT(*) AS total FROM users");
                Check(select.Data is not null && Convert.ToInt64(select.Data.Rows[0]["total"]) == 3, "SQL-консоль: SELECT отработал");

                var vacuum = provider.Execute("VACUUM");
                Check(vacuum.Data is null, "SQL-консоль: VACUUM без результата");

                Console.WriteLine();
                Console.WriteLine("Исправить БД (SQLite):");
                var report = new FixReport();
                provider.FixDatabase(report);
                foreach (var line in report.Lines)
                    Console.WriteLine("   " + line);
                Check(!report.HasErrors, "FixDatabase: ошибок в целой базе нет");

                provider.Close();
            }

            Console.WriteLine();
            Console.WriteLine("Конвертация SQLite -> SQLite:");
            using (var source = new DbTool.Core.SqliteProvider())
            using (var target = new DbTool.Core.SqliteProvider())
            {
                source.Open(new Dictionary<string, string> { { "file", sourceDb } });
                target.Open(new Dictionary<string, string> { { "file", targetDb }, { "create", "1" } });

                var progress = new List<string>();
                var failures = DbConverter.ConvertTables(
                    source,
                    target,
                    source.GetTables(),
                    (table, copied, index, total) => progress.Add(table + " " + copied + " " + index + "/" + total),
                    () => false);
                Check(failures.Count == 0, "конвертация без отказов");

                var targetTables = target.GetTables();
                Check(targetTables.Contains("users") && targetTables.Contains("orders"), "в целевой базе созданы таблицы");

                var targetUsers = target.LoadRows("users", null, 5000, out _);
                var sourceUsers = source.LoadRows("users", null, 5000, out _);
                Check(targetUsers.Rows.Count == sourceUsers.Rows.Count, "число строк users совпадает: " + targetUsers.Rows.Count);

                var rerun = DbConverter.ConvertTables(
                    source,
                    target,
                    new[] { "users" },
                    (_, _, _, _) => { },
                    () => false);
                Check(rerun.Count == 0 && target.CountRows("users", null) == sourceUsers.Rows.Count,
                    "повторная конвертация перезаписывает таблицу без задвоения");

                var nullAge = false;
                foreach (DataRow row in targetUsers.Rows)
                    if (row["age"] is DBNull)
                        nullAge = true;
                Check(nullAge, "NULL-значение age перенеслось в цель");

                target.Close();
                source.Close();
            }

            Console.WriteLine();
            Console.WriteLine(failCount == 0 ? "СМОУК: всё исправно" : "СМОУК: failed " + failCount + " проверок");
            Console.OutputEncoding = original;
            Environment.ExitCode = failCount == 0 ? 0 : 1;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static void CreateSource(string path)
    {
        using var cnn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path);
        cnn.Open();

        using (var cmd = cnn.CreateCommand())
        {
            cmd.CommandText =
                "CREATE TABLE users (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " +
                "name TEXT NOT NULL, " +
                "age INTEGER NULL, " +
                "score REAL, " +
                "is_active INTEGER, " +
                "pic BLOB, " +
                "tags TEXT);";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = cnn.CreateCommand())
        {
            cmd.CommandText =
                "INSERT INTO users (name, age, score, is_active) VALUES ('Анна', 25, 10.5, 1);" +
                "INSERT INTO users (name, age, score, is_active) VALUES ('Борис', NULL, 0, 0);" +
                "INSERT INTO users (name, age, score, is_active) VALUES ('Вера', 35, 3.25, 1)";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = cnn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE orders (id INTEGER PRIMARY KEY, user_id INTEGER, total REAL, note TEXT)";
            cmd.ExecuteNonQuery();
        }
    }
}
