using System.Diagnostics;
using DbTool.Core;

namespace DbTool.Forms;

public class SqlConsoleForm : Form
{
    private readonly DbProvider provider;
    private readonly List<string> history = new();
    private ComboBox cmbTable = null!;
    private ComboBox cmbTemplate = null!;
    private ComboBox cmbHistory = null!;
    private TextBox txtSql = null!;
    private DataGridView grid = null!;
    private Label lbStatus = null!;
    private Button btnRun = null!;
    private Button btnExport = null!;

    private static string HistoryPath => Path.Combine(AppContext.BaseDirectory, "dbtool-history.txt");

    public SqlConsoleForm(DbProvider provider)
    {
        this.provider = provider;
        Text = "SQL-консоль — " + provider.Title;
        Width = 900;
        Height = 660;
        StartPosition = FormStartPosition.CenterParent;

        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(6, 4, 6, 2) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cmbTable = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbTable.SelectedIndexChanged += (_, _) => RebuildTemplates();
        cmbTemplate = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbTemplate.SelectedIndexChanged += (_, _) => ApplyTemplate();
        cmbHistory = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbHistory.SelectedIndexChanged += (_, _) =>
        {
            if (cmbHistory.SelectedIndex >= 0)
                txtSql.Text = history[history.Count - 1 - cmbHistory.SelectedIndex];
        };
        top.Controls.Add(new Label { Text = "Таблица:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        top.Controls.Add(cmbTable, 1, 0);
        top.Controls.Add(new Label { Text = "Шаблон:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        top.Controls.Add(cmbTemplate, 1, 1);
        top.Controls.Add(new Label { Text = "История:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        top.Controls.Add(cmbHistory, 1, 2);

        txtSql = new TextBox
        {
            Dock = DockStyle.Top,
            Multiline = true,
            Height = 170,
            Font = new Font("Consolas", 10),
            ScrollBars = ScrollBars.Vertical
        };

        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(6) };
        btnRun = new Button { Text = "Выполнить (F5)", Width = 140 };
        btnRun.Click += (_, _) => Run();
        btnExport = new Button { Text = "Экспорт CSV…", Width = 130 };
        btnExport.Click += (_, _) => ExportResult();
        var btnClear = new Button { Text = "Очистить", Width = 90 };
        btnClear.Click += (_, _) => txtSql.Clear();
        tools.Controls.Add(btnRun);
        tools.Controls.Add(btnExport);
        tools.Controls.Add(btnClear);

        grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None
        };

        lbStatus = new Label { Dock = DockStyle.Fill, Text = string.Empty, Padding = new Padding(6, 2, 0, 0) };

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 174));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        root.Controls.Add(top, 0, 0);
        root.Controls.Add(txtSql, 0, 1);
        root.Controls.Add(tools, 0, 2);
        root.Controls.Add(grid, 0, 3);
        root.Controls.Add(lbStatus, 0, 4);

        Controls.Add(root);
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F5)
            {
                e.Handled = true;
                Run();
            }
        };
        FormClosing += (_, _) => SaveHistory();
        LoadHistory();
    }

    public void SetTables(List<string> tables)
    {
        cmbTable.Items.Clear();
        cmbTable.Items.Add("— не выбрана —");
        foreach (var t in tables)
            cmbTable.Items.Add(t);
        cmbTable.SelectedIndex = 0;
    }

    private const int RunLimit = 5000;

    private string PrepareCommand(string sql, out bool capped)
    {
        capped = false;

        if (SqlSafety.HeadKeyword(sql) != "SELECT")
            return sql;

        if (provider.Kind == ProviderKind.Access)
            return sql;

        var words = SqlSafety.StripLiterals(sql)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim(';', '(', ')').ToUpperInvariant())
            .ToList();

        if (words.Any(w => w is "LIMIT" or "OFFSET" or "FETCH" or "TOP"))
            return sql;

        capped = true;
        return "SELECT * FROM (" + sql + ") AS capped LIMIT " + RunLimit;
    }

    private void RebuildTemplates()
    {
        cmbTemplate.Items.Clear();
        cmbTemplate.Items.Add("— шаблоны —");
        if (cmbTable.SelectedIndex > 0)
        {
            if (provider.Kind != ProviderKind.Access)
                cmbTemplate.Items.Add("первые 100 строк");
            else
                cmbTemplate.Items.Add("первые 100 строк (TOP)");
            cmbTemplate.Items.Add("количество строк");
            if (provider.Kind != ProviderKind.Access)
                cmbTemplate.Items.Add("структура таблицы");
        }
        cmbTemplate.SelectedIndex = 0;
    }

    private void ApplyTemplate()
    {
        if (cmbTable.SelectedIndex <= 0 || cmbTemplate.SelectedIndex <= 0)
            return;

        var table = (string)cmbTable.Items[cmbTable.SelectedIndex]!;
        txtSql.Text = provider.Kind switch
        {
            ProviderKind.Sqlite => cmbTemplate.SelectedIndex switch
            {
                1 => "SELECT * FROM " + provider.QuoteIdent(table) + " LIMIT 100",
                2 => "SELECT COUNT(*) FROM " + provider.QuoteIdent(table),
                _ => "PRAGMA table_info(" + provider.QuoteIdent(table) + ")"
            },
            ProviderKind.MySql => cmbTemplate.SelectedIndex switch
            {
                1 => "SELECT * FROM " + provider.QuoteIdent(table) + " LIMIT 100",
                2 => "SELECT COUNT(*) FROM " + provider.QuoteIdent(table),
                _ => "DESCRIBE " + provider.QuoteIdent(table)
            },
            ProviderKind.Postgres => cmbTemplate.SelectedIndex switch
            {
                1 => "SELECT * FROM " + provider.QuoteIdent(table) + " LIMIT 100",
                2 => "SELECT COUNT(*) FROM " + provider.QuoteIdent(table),
                _ => "SELECT column_name, data_type, is_nullable FROM information_schema.columns WHERE table_name = '" + table.Replace("'", "''") + "' ORDER BY ordinal_position"
            },
            _ => cmbTemplate.SelectedIndex switch
            {
                1 => "SELECT TOP 100 * FROM " + provider.QuoteIdent(table),
                2 => "SELECT COUNT(*) FROM " + provider.QuoteIdent(table),
                _ => string.Empty
            }
        };
    }

    private void Run()
    {
        if (string.IsNullOrWhiteSpace(txtSql.Text))
            return;

        var commands = SqlSplitter.Split(txtSql.Text).ToList();
        btnRun.Enabled = false;
        btnExport.Enabled = false;
        Cursor = Cursors.WaitCursor;

        var results = new List<DbResult>();
        var clock = Stopwatch.StartNew();

        Task.Run(() =>
        {
            foreach (var command in commands)
            {
                var confirmed = ConfirmDanger(command);
                if (!confirmed)
                {
                    Log.Info("SQL пропущен пользователем: " + Short(command));
                    continue;
                }

                var executed = PrepareCommand(command, out var capped);

                try
                {
                    var result = provider.Execute(executed);
                    lock (results)
                        results.Add(result);
                    Log.Info("SQL: " + Short(command) + " → " + result.Note +
                             (capped ? " (вывод ограничен первыми " + RunLimit + " строками)" : string.Empty));
                }
                catch (Exception ex)
                {
                    Log.Error("SQL ошибка: " + Short(command), ex);
                    lock (results)
                        results.Add(new DbResult { RowsAffected = -1, Note = "ошибка: " + ex.Message });
                }
            }
        })
        .ContinueWith(t =>
        {
            Cursor = Cursors.Default;
            btnRun.Enabled = true;

            clock.Stop();
            var lastData = results.Count > 0 ? results[^1].Data : null;
            grid.DataSource = lastData;
            btnExport.Enabled = lastData is not null;

            var summary = results.Count > 0 ? string.Join(", ", results.Select(r => r.Note)) : "нет команд";
            lbStatus.Text = string.Format("{0} команд за {1} мс — {2}", commands.Count, clock.ElapsedMilliseconds, summary);

            CommitHistory(txtSql.Text);

            if (t.Exception is not null)
                Log.Error("Ошибка в фоновой обработке SQL", t.Exception);
            else if (results.Count > 0 && string.IsNullOrWhiteSpace(lbStatus.Text))
                lbStatus.Text = "готово";
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private bool ConfirmDanger(string command)
    {
        if (!SqlSafety.IsDangerous(command, out var reason))
            return true;

        var answer = (bool?)Invoke(new Func<bool>(() =>
            MessageBox.Show(this,
                "Команда опасная: " + reason + "\n\n" + Short(command),
                "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes));

        return answer == true;
    }

    private static string Short(string sql)
        => sql.Length <= 100 ? sql : sql[..100] + " …";

    private void ExportResult()
    {
        if (grid.DataSource is DataTable data)
            ExportDialog.Export(this, data, "sql-result.csv");
    }

    private void LoadHistory()
    {
        try
        {
            if (File.Exists(HistoryPath))
            {
                var line = File.ReadAllText(HistoryPath);
                var items = line.Split(new[] { "§§§" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var item in items)
                    history.Add(item.Replace("\\n", Environment.NewLine));
            }
        }
        catch (Exception ex)
        {
            Log.Warn("История SQL не прочитана: " + ex.Message);
        }

        UpdateHistoryCombo();
    }

    private void CommitHistory(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return;

        var compact = sql.Trim().Replace("\r\n", "\\n").Replace("\n", "\\n");
        history.Remove(compact);
        history.Add(compact);
        while (history.Count > 40)
            history.RemoveAt(0);

        UpdateHistoryCombo();
    }

    private void SaveHistory()
    {
        try
        {
            File.WriteAllText(HistoryPath, string.Join("§§§", history.Select(h => h.Replace("\r\n", "\\n").Replace("\n", "\\n"))));
        }
        catch (Exception ex)
        {
            Log.Warn("История SQL не записана: " + ex.Message);
        }
    }

    private void UpdateHistoryCombo()
    {
        cmbHistory.Items.Clear();
        for (int i = history.Count - 1; i >= 0; i--)
        {
            var preview = history[i].Replace("\n", " ").Replace("\\n", " ");
            if (preview.Length > 90)
                preview = preview[..90] + "…";
            cmbHistory.Items.Add(preview);
        }
    }
}
