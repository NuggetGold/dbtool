using DbTool.Core;

namespace DbTool.Forms;

public class ConvertForm : Form
{
    private readonly DbProvider source;
    private DbProvider? target;
    private volatile bool cancelRequested;
    private bool running;

    private CheckedListBox lstTables = null!;
    private Label lbTarget = null!;
    private ProgressBar bar = null!;
    private Label lbProgress = null!;
    private Button btnStart = null!;
    private Button btnCancel = null!;
    private Button btnClose = null!;
    private Button btnTarget = null!;
    private readonly Dictionary<string, long> copiedByTable = new();
    private readonly List<string> failedTables = new();
    private long totalRows;

    public ConvertForm(DbProvider source)
    {
        this.source = source;
        Text = "Конвертация: " + source.Title + " → другая база";
        Width = 680;
        Height = 620;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(6) };
        top.Controls.Add(new Label
        {
            Text = "Базы для копирования (схема + данные). Целевые таблицы будут перезаписаны.",
            AutoSize = true,
            Padding = new Padding(0, 2, 0, 0)
        });

        var mid = new Panel { Dock = DockStyle.Fill };

        lstTables = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true
        };

        var targetPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(6) };
        btnTarget = new Button { Text = "Подключить целевую базу…", Width = 200 };
        btnTarget.Click += (_, _) => PickTarget();
        lbTarget = new Label { Text = "Цель не выбрана", AutoSize = true, Padding = new Padding(6, 8, 0, 0) };
        targetPanel.Controls.Add(btnTarget);
        targetPanel.Controls.Add(lbTarget);

        mid.Controls.Add(lstTables);
        mid.Controls.Add(targetPanel);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 96, ColumnCount = 2, RowCount = 2, Padding = new Padding(6) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        bar = new ProgressBar { Dock = DockStyle.Fill };
        lbProgress = new Label { Text = "Ожидание…", Dock = DockStyle.Top, AutoSize = false, Height = 28, Padding = new Padding(0, 6, 0, 0) };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        btnClose = new Button { Text = "Закрыть", Width = 100, DialogResult = DialogResult.Cancel };
        btnCancel = new Button { Text = "Отмена", Width = 100, Enabled = false };
        btnCancel.Click += (_, _) => cancelRequested = true;
        btnStart = new Button { Text = "Начать конвертацию", Width = 160, Enabled = false };
        btnStart.Click += (_, _) => StartConversion();
        buttons.Controls.Add(btnClose);
        buttons.Controls.Add(btnCancel);
        buttons.Controls.Add(btnStart);

        bottom.Controls.Add(lbProgress, 0, 0);
        bottom.Controls.Add(bar, 1, 0);
        bottom.SetColumnSpan(bar, 1);
        bottom.Controls.Add(buttons, 0, 1);
        bottom.SetColumnSpan(buttons, 2);

        Controls.Add(mid);
        Controls.Add(top);
        Controls.Add(bottom);
        CancelButton = btnClose;

        FormClosing += (_, e) =>
        {
            if (running)
            {
                e.Cancel = true;
                Log.Warn("Конвертация выполняется: нажмите «Отмена» и дождитесь завершения.");
            }
        };

        Load += (_, _) => LoadTables();
    }

    private void LoadTables()
    {
        lstTables.Items.Clear();
        Cursor = Cursors.WaitCursor;

        Task.Run(() => source.GetTables())
            .ContinueWith(t =>
            {
                Cursor = Cursors.Default;
                if (t.Exception is null && t.Result is not null)
                {
                    foreach (var name in t.Result)
                        lstTables.Items.Add(name, true);
                    Log.Info("Конвертация: источник " + source.Title + ", таблиц: " + t.Result.Count);
                }
                else
                {
                    Log.Error("Список таблиц источника не получен", t.Exception ?? new Exception("нет данных"));
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void PickTarget()
    {
        if (running)
            return;

        using var form = new ConnectForm();
        if (form.ShowDialog(this) != DialogResult.OK || form.Provider is null)
            return;

        if (form.Provider is AccessProvider ap && ap.IsExcelFile)
        {
            MessageBox.Show("Конвертация в Excel не поддерживается: Excel через ACE годится только для просмотра и экспорта.",
                "dbtool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            form.Provider.Dispose();
            return;
        }

        if (source.Kind == form.Provider.Kind && SameFile(source, form.Provider))
        {
            MessageBox.Show("Целевой файл совпадает с файлом источника. Укажите другой файл.",
                "dbtool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            form.Provider.Dispose();
            return;
        }

        target?.Dispose();
        target = form.Provider;
        lbTarget.Text = "Цель: " + target.Title;
        btnStart.Enabled = lstTables.CheckedItems.Count > 0;
        try
        {
            Log.Info("Конвертация: цель подключена: " + target.ServerInfo);
        }
        catch (Exception ex)
        {
            Log.Warn("Конвертация: сведения о цели недоступны: " + ex.Message);
        }
    }

    private static bool SameFile(DbProvider a, DbProvider b)
    {
        var fa = a.CfgSafe("file");
        var fb = b.CfgSafe("file");
        return fa is not null && fb is not null &&
               string.Equals(fa, fb, StringComparison.OrdinalIgnoreCase);
    }

    private void StartConversion()
    {
        if (target is null || running)
            return;

        var tables = lstTables.CheckedItems.Cast<object>().Select(o => o.ToString()!).ToList();
        if (tables.Count == 0)
        {
            MessageBox.Show("Отметьте таблицы для копирования.", "dbtool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        btnStart.Enabled = false;
        btnClose.Enabled = false;
        btnTarget.Enabled = false;
        running = true;
        Cursor = Cursors.WaitCursor;
        lstTables.Enabled = false;
        lbProgress.Text = "Подсчёт строк…";

        var counts = new Dictionary<string, long>();
        Task.Run(() =>
        {
            foreach (var t in tables)
            {
                var n = source.CountRows(t, null);
                lock (counts)
                    counts[t] = n;
            }
        })
        .ContinueWith(t =>
        {
            Cursor = Cursors.Default;
            if (t.Exception is not null)
            {
                Log.Error("Подсчёт строк не выполнен", t.Exception.InnerException ?? t.Exception);
                AbortUi();
                return;
            }

            totalRows = counts.Sum(c => c.Value);
            var listText = string.Join(Environment.NewLine, counts.Select(c => c.Key + ": " + c.Value + " строк"));
            if (MessageBox.Show(this,
                    "План конвертации:\n\n" + listText +
                    "\n\nВсего строк: " + totalRows +
                    "\n\nЦелевые таблицы будут перезаписаны. Начать?",
                    "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                AbortUi();
                return;
            }

            RunConversion(tables);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void RunConversion(IReadOnlyList<string> tables)
    {
        cancelRequested = false;
        copiedByTable.Clear();
        btnCancel.Enabled = true;
        bar.Minimum = 0;
        bar.Maximum = 100;
        bar.Value = 0;
        lbProgress.Text = "Копирование…";

        var targetRef = target!;
        var sourceRef = source;
        var sumTotal = totalRows;

        Task.Run(() =>
            DbConverter.ConvertTables(
                sourceRef,
                targetRef,
                tables,
                (table, copied, index, totalTables) => SafeInvoke(() =>
                {
                    copiedByTable[table] = copied;
                    var done = copiedByTable.Values.Sum();
                    bar.Value = sumTotal > 0
                        ? (int)Math.Min(bar.Maximum, done * bar.Maximum / sumTotal)
                        : Math.Min(bar.Maximum, done > 0 ? 1 : 0);
                    lbProgress.Text = string.Format("{0} — перенесено строк: {1}   (таблица {2} из {3})", table, copied, index, totalTables);
                }),
                () => cancelRequested))
        .ContinueWith(t =>
        {
            btnCancel.Enabled = false;
            btnClose.Enabled = true;
            btnTarget.Enabled = true;
            lstTables.Enabled = true;
            running = false;
            Cursor = Cursors.Default;

            if (t.Exception is not null)
                Log.Error("Ошибка в фоне конвертации", t.Exception.InnerException ?? t.Exception);

            failedTables.Clear();
            if (t.Exception is null)
                failedTables.AddRange(t.Result!);

            if (cancelRequested)
            {
                lbProgress.Text = "Остановлено по требованию";
                Log.Info("Конвертация остановлена пользователем");
            }
            else
            {
                if (bar.Value < bar.Maximum)
                    bar.Value = bar.Maximum;
                lbProgress.Text = "Готово. Подробности в журнале.";
            }

            MessageBox.Show(this, BuildSummary(), "dbtool",
                MessageBoxButtons.OK, failedTables.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private string BuildSummary()
    {
        var transferred = copiedByTable.Values.Sum();
        var head = cancelRequested
            ? "Конвертация остановлена: таблиц продвинуто — " + copiedByTable.Count + ", перенесено строк — " + transferred + "."
            : "Конвертация завершена: таблиц — " + copiedByTable.Count + ", перенесено строк — " + transferred + ".";

        var body = failedTables.Count > 0
            ? "\n\nНе перенесены:\n" + string.Join(Environment.NewLine, failedTables.Select(t => "— " + t))
            : string.Empty;

        return head + body + (failedTables.Count > 0 ? "\n\n" : "\n") + "Подробности — в журнале.";
    }

    private void AbortUi()
    {
        running = false;
        btnClose.Enabled = true;
        btnTarget.Enabled = true;
        btnStart.Enabled = target is not null;
        lbProgress.Text = "Ожидание…";
    }

    private void SafeInvoke(Action action)
    {
        if (IsHandleCreated && !IsDisposed)
            Invoke(action);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        target?.Dispose();
        target = null;
        base.OnFormClosed(e);
    }
}
