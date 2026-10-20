using System.Text;

namespace DbTool.Core;

public static class Log
{
    private static readonly object Gate = new();
    private static event Action<string>? Story;

    public static string LogFilePath { get; } =
        Path.Combine(AppContext.BaseDirectory, "dbtool-log.txt");

    public static event Action<string> OnLine
    {
        add => Story += value;
        remove => Story -= value;
    }

    public static void Info(string message) => Push("INFO", message);

    public static void Warn(string message) => Push("WARN", message);

    public static void Error(string message) => Push("ERR ", message);

    public static void Error(string message, Exception ex)
        => Push("ERR ", message + ": " + ex.ToString());

    private static void Push(string level, string message)
    {
        var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + message;
        lock (Gate)
        {
            Story?.Invoke(line);
            try
            {
                File.AppendAllText(LogFilePath, line + Environment.NewLine, new UTF8Encoding(false));
            }
            catch
            {
            }
        }
    }

    public static void SaveAs(string path)
    {
        lock (Gate)
        {
            if (File.Exists(LogFilePath))
                File.Copy(LogFilePath, path, true);
            else
                File.WriteAllText(path, string.Empty);
        }
    }
}
