namespace DbTool.Core;

public enum ProviderKind
{
    Sqlite,
    MySql,
    Postgres,
    Access
}

public class FixReport
{
    public List<string> Lines { get; } = new();
    public bool NeedsRecovery { get; set; }
    public bool HasErrors { get; private set; }

    public void Add(string line)
    {
        Lines.Add(line);
        Log.Info("Исправить БД: " + line);
    }

    public void Fail(string line)
    {
        Lines.Add(line);
        HasErrors = true;
        Log.Error("Исправить БД: " + line);
    }
}
