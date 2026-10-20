using System.Text;
using DbTool.Core;
using DbTool.Forms;

namespace DbTool;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            Log.Error("Ошибка интерфейса", e.Exception);
            MessageBox.Show(e.Exception.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Log.Error("Критическая ошибка вне интерфейса", ex);
        };

        Log.Info("Запуск dbtool");
        try
        {
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            Log.Error("Критическое падение", ex);
            MessageBox.Show(ex.Message, "Критическое падение", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Log.Info("Программа закрыта");
        }
    }
}
