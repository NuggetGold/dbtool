namespace DbTool.Core;

public static class ProviderFactory
{
    public static DbProvider Create(ProviderKind kind) => kind switch
    {
        ProviderKind.Sqlite => new SqliteProvider(),
        ProviderKind.MySql => new MySqlProvider(),
        ProviderKind.Postgres => new PostgresProvider(),
        _ => new AccessProvider()
    };

    public static string DisplayName(ProviderKind kind) => kind switch
    {
        ProviderKind.Sqlite => "SQLite (файл .db/.sqlite)",
        ProviderKind.MySql => "MySQL / MariaDB",
        ProviderKind.Postgres => "PostgreSQL",
        _ => "Access / Excel (через ACE)"
    };

    public static string[] FieldTable => new[] { "file", "host", "port", "user", "password", "database" };
}
