using System.Globalization;
using DbTool.Core;

namespace DbTool.Forms;

public class MainForm : Form
{
    private DbProvider? provider;
    private SqlConsoleForm? sqlForm;

    private string currentTable = string.Empty;
    private List<DbColumn>? currentCols;
    private DataTable? currentData;

    private Button btnConnect = null!, btnDisconnect = null!, btnConvert = null!, btnSql = null!, btnFix = null!, btnTreeRefresh = null!, btnRefresh = null!, btnAdd = null!, btnDelete = null!, btnCsv = null!, btnApplyFilter = null!, btnClearFilter = null!;
    private TreeView treeTables = null!;
    private TabControl tabs = null!;
    private DataGridView gridData = null!, gridStructure = null!;
    private TextBox tbFilter = null!;
    private Label lbDataInfo = null!, lbStatus = null!;
    private RichTextBox logBox = null!;

    public MainForm()
    {
        Text = "dbtool — редактор баз данных";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1220;
        Height = 830;
        MinimumSize = new Size(1020, 660);

        BuildMenu();
        BuildToolbar();
        BuildCenter();

        Load += (_, _) => { SetUiState(false); Log.Info("Подсказка: двойной клик по таблице слева открывает её; ПКМ по данным — NULL, копирование, CSV."); };
        FormClosed += (_, _) => { Log.OnLine -= AppendLine; sqlForm?.Close(); provider?.Dispose(); };
        Log.OnLine += AppendLine;
        Log.Info("Готов к работе. Подключите базу.");
    }

    private void BuildMenu()
    {
        var menu = new MenuStrip { Dock = DockStyle.Top };

        var database = new ToolStripMenuItem("База");
        database.DropDownItems.Add(new ToolStripMenuItem("Подключить…", null, (_, _) => Connect()) { ShortcutKeys = Keys.Control | Keys.O });
        database.DropDownItems.Add(new ToolStripMenuItem("Отключить", null, (_, _) => Disconnect()));
        database.DropDownItems.Add(new ToolStripSeparator());
        database.DropDownItems.Add(new ToolStripMenuItem("Исправить БД", null, (_, _) => FixDatabase()));
        database.DropDownItems.Add(new ToolStripMenuItem("Конвертация…", null, (_, _) => OpenConvert()));
        database.DropDownItems.Add(new ToolStripSeparator());
        database.DropDownItems.Add(new ToolStripMenuItem("Выход", null, (_, _) => Close()));

        var data = new ToolStripMenuItem("Данные");
        data.DropDownItems.Add(new ToolStripMenuItem("Обновить таблицу", null, (_, _) => LoadCurrent()));
        data.DropDownItems.Add(new ToolStripMenuItem("Добавить строку", null, (_, _) => AddRow()));
        data.DropDownItems.Add(new ToolStripMenuItem("Удалить строку", null, (_, _) => DeleteRow()));
        data.DropDownItems.Add(new ToolStripSeparator());
        data.DropDownItems.Add(new ToolStripMenuItem("Экспорт CSV…", null, (_, _) => ExportGrid()));

        var tools = new ToolStripMenuItem("Инструменты");
        tools.DropDownItems.Add(new ToolStripMenuItem("SQL-консоль", null, (_, _) => OpenSql()));
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add(new ToolStripMenuItem("Сохранить журнал…", null, (_, _) => SaveLog()));

        var help = new ToolStripMenuItem("Справка");
        help.DropDownItems.Add(new ToolStripMenuItem("О программе", null, (_, _) =>
            MessageBox.Show(this,
                "dbtool — универсальный редактор баз данных.\n\nSQLite, MySQL/MariaDB, PostgreSQL, Access/Excel.\nРедактор строк, SQL-консоль, конвертация между базами, журнал действий, кнопка «Исправить БД».\n\nЖурнал пишется в dbtool-log.txt рядом с программой.",
                "О программе", MessageBoxButtons.OK, MessageBoxIcon.Information)));

        menu.Items.Add(database);
        menu.Items.Add(data);
        menu.Items.Add(tools);
        menu.Items.Add(help);
        MainMenuStrip = menu;
        Controls.Add(menu);
    }

    private void BuildToolbar()
    {
        var bar = new TableLayoutPanel { Dock = DockStyle.Top, Height = 76, ColumnCount = 1, RowCount = 2, Padding = new Padding(6, 6, 6, 4) };
        bar.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };

        btnConnect = new Button { Text = "Подключить…", Width = 126 };
        btnConnect.Click += (_, _) => Connect();
        btnDisconnect = new Button { Text = "Отключить", Width = 100, Enabled = false };
        btnDisconnect.Click += (_, _) => Disconnect();
        btnSql = new Button { Text = "SQL-консоль", Width = 110, Enabled = false };
        btnSql.Click += (_, _) => OpenSql();
        btnConvert = new Button { Text = "Конвертация…", Width = 122, Enabled = false };
        btnConvert.Click += (_, _) => OpenConvert();
        btnFix = new Button { Text = "Исправить БД", Width = 124, Enabled = false };
        btnFix.Click += (_, _) => FixDatabase();
        lbStatus = new Label { AutoSize = false, Width = 470, Height = 30, TextAlign = ContentAlignment.MiddleLeft };

        foreach (var c in new Control[] { btnConnect, btnDisconnect, btnSql, btnConvert, btnFix })
            actions.Controls.Add(c);

        bar.Controls.Add(actions, 0, 0);
        bar.Controls.Add(lbStatus, 0, 1);
        Controls.Add(bar);
    }

    private void BuildCenter()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 260));

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 270 };
        split.Panel1.Controls.Add(BuildTreePanel());
        split.Panel2.Controls.Add(BuildTabs());
        layout.Controls.Add(split, 0, 0);
        layout.Controls.Add(BuildLogPanel(), 0, 1);
        Controls.Add(layout);
    }

    private Control BuildTreePanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(4) };
        btnTreeRefresh = new Button { Text = "Обновить список", Width = 140, Enabled = false };
        btnTreeRefresh.Click += (_, _) => RefreshTree();
        top.Controls.Add(btnTreeRefresh);

        treeTables = new TreeView { Dock = DockStyle.Fill, HideSelection = false };
        treeTables.NodeMouseDoubleClick += (_, e) => { if (e.Node is not null) PickTable(e.Node.Text); };

        panel.Controls.Add(treeTables);
        panel.Controls.Add(top);
        return panel;
    }

    private Control BuildTabs()
    {
        tabs = new TabControl { Dock = DockStyle.Fill };

        var dataPage = new TabPage("Данные");
        var dataLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        dataLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        dataLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        dataLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        dataLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4, 4, 4, 0), WrapContents = false, AutoScroll = true };
        var filterBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Padding = new Padding(4, 2, 4, 0) };
        filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        btnRefresh = new Button { Text = "Обновить", Width = 98, Enabled = false };
        btnRefresh.Click += (_, _) => LoadCurrent();
        btnAdd = new Button { Text = "+ Строка", Width = 96, Enabled = false };
        btnAdd.Click += (_, _) => AddRow();
        btnDelete = new Button { Text = "− Строка", Width = 96, Enabled = false };
        btnDelete.Click += (_, _) => DeleteRow();
        btnCsv = new Button { Text = "Экспорт CSV…", Width = 124, Enabled = false };
        btnCsv.Click += (_, _) => ExportGrid();
        tbFilter = new TextBox { Dock = DockStyle.Fill, MinimumSize = new Size(120, 0) };
        tbFilter.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyFilter(); } };
        btnApplyFilter = new Button { Text = "Фильтр", Width = 86, Enabled = false };
        btnApplyFilter.Click += (_, _) => ApplyFilter();
        btnClearFilter = new Button { Text = "Сброс", Width = 80, Enabled = false };
        btnClearFilter.Click += (_, _) => { tbFilter.Text = string.Empty; ApplyFilter(); };
        lbDataInfo = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, Padding = new Padding(6, 6, 0, 0) };

        toolbar.Controls.Add(btnRefresh);
        toolbar.Controls.Add(btnAdd);
        toolbar.Controls.Add(btnDelete);
        toolbar.Controls.Add(btnCsv);
        filterBar.Controls.Add(new Label { Text = "WHERE:", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(4, 0, 0, 0) }, 0, 0);
        filterBar.Controls.Add(tbFilter, 1, 0);
        filterBar.Controls.Add(btnApplyFilter, 2, 0);
        filterBar.Controls.Add(btnClearFilter, 3, 0);

        dataLayout.Controls.Add(toolbar, 0, 0);
        dataLayout.Controls.Add(filterBar, 0, 1);
        dataLayout.Controls.Add(lbDataInfo, 0, 2);

        gridData = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            MultiSelect = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None
        };
        gridData.CellFormatting += GridCellFormatting;
        gridData.CellBeginEdit += GridCellBeginEdit;
        gridData.CellEndEdit += GridCellEndEdit;
        gridData.DataError += GridDataError;
        gridData.KeyDown += GridKeyDown;
        gridData.ContextMenuStrip = BuildGridMenu();

        dataLayout.Controls.Add(gridData, 0, 3);
        dataPage.Controls.Add(dataLayout);

        var structurePage = new TabPage("Структура");
        gridStructure = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None
        };
        foreach (var (name, header) in new[]
                 {
                     ("name", "Колонка"), ("type", "Тип"), ("nullable", "NULL разрешён"),
                     ("pk", "Первичный ключ"), ("auto", "Автозаполнение"), ("def", "По умолчанию"), ("kind", "Тип значения при правке")
                 })
            gridStructure.Columns.Add(name, header);

        structurePage.Controls.Add(gridStructure);

        tabs.Controls.Add(dataPage);
        tabs.Controls.Add(structurePage);
        return tabs;
    }

    private ContextMenuStrip BuildGridMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Сделать NULL", null, (_, _) => SetCellNull()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Копировать значение", null, (_, _) => CopyCell()));
        menu.Items.Add(new ToolStripMenuItem("Копировать строку", null, (_, _) => CopyCurrentRow()));
        menu.Items.Add(new ToolStripMenuItem("Копировать все строки", null, (_, _) => CopyAllRows()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Экспорт CSV…", null, (_, _) => ExportGrid()));
        return menu;
    }

    private Control BuildLogPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        logBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new Font("Consolas", 9),
            BackColor = Color.FromArgb(250, 250, 250),
            WordWrap = false,
            DetectUrls = false
        };
        var side = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(4) };
        var btnSave = new Button { Text = "Сохранить журнал…", Width = 142 };
        btnSave.Click += (_, _) => SaveLog();
        var btnClear = new Button { Text = "Очистить показ", Width = 142 };
        btnClear.Click += (_, _) => logBox.Clear();
        side.Controls.Add(btnSave);
        side.Controls.Add(btnClear);

        var top = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Журнал (также пишется в dbtool-log.txt рядом с программой)",
            Padding = new Padding(4, 2, 0, 0)
        };
        panel.Controls.Add(top, 0, 0);
        panel.SetColumnSpan(top, 2);
        panel.Controls.Add(logBox, 0, 1);
        panel.Controls.Add(side, 1, 1);
        return panel;
    }

    private void AppendLine(string line)
    {
        if (!logBox.IsHandleCreated) return;
        if (logBox.InvokeRequired) { logBox.BeginInvoke(() => AppendInternal(line)); return; }
        AppendInternal(line);
    }

    private void AppendInternal(string line)
    {
        logBox.AppendText(line + Environment.NewLine);
        if (logBox.TextLength > 400_000)
        {
            logBox.Select(0, 160_000);
            logBox.SelectedText = string.Empty;
        }
        logBox.SelectionStart = logBox.TextLength;
        logBox.ScrollToCaret();
    }

    private void SaveLog()
    {
        using var dlg = new SaveFileDialog
        {
            FileName = "dbtool-log-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt",
            Filter = "Текстовые файлы|*.txt|Все файлы|*.*"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            Log.SaveAs(dlg.FileName);
            Log.Info("Журнал сохранён: " + dlg.FileName);
        }
        catch (Exception ex)
        {
            Log.Error("Журнал не сохранён", ex);
            MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SetUiState(bool busy)
    {
        var connected = provider is not null && provider.IsConnected;
        var canData = connected && currentData is not null && !busy;

        Cursor = busy ? Cursors.AppStarting : Cursors.Default;
        btnConnect.Enabled = !busy;
        btnDisconnect.Enabled = connected && !busy;
        btnSql.Enabled = connected && !busy;
        btnConvert.Enabled = connected && !busy;
        btnFix.Enabled = connected && !busy;
        btnTreeRefresh.Enabled = connected && !busy;
        treeTables.Enabled = connected && !busy;
        tabs.Enabled = connected && !busy;

        btnRefresh.Enabled = canData;
        btnAdd.Enabled = canData && provider is not null && !provider.ReadOnly;
        btnDelete.Enabled = canData && provider is not null && provider.CanDeleteRows && !provider.ReadOnly;
        btnCsv.Enabled = canData;
        btnApplyFilter.Enabled = canData;
        btnClearFilter.Enabled = canData;
        tbFilter.Enabled = canData;
    }

    private void Connect()
    {
        using var form = new ConnectForm();
        if (form.ShowDialog(this) != DialogResult.OK || form.Provider is null)
            return;
        sqlForm?.Close();
        sqlForm = null;
        provider?.Dispose();
        provider = form.Provider;
        currentTable = string.Empty;
        currentCols = null;
        currentData = null;
        gridData.DataSource = null;
        gridStructure.Rows.Clear();
        Log.Info("Подключено: " + provider.Title);
        try { lbStatus.Text = provider.ServerInfo; }
        catch (Exception ex) { lbStatus.Text = provider.Title; Log.Warn("Сведения о сервере недоступны: " + ex.Message); }
        if (provider.ReadOnly)
            Log.Warn("Источник в режиме «только просмотр» (Excel): правка и удаление строк отключены.");
        SetUiState(false);
        RefreshTree();
    }

    private void Disconnect()
    {
        sqlForm?.Close();
        sqlForm = null;
        currentData = null;
        currentCols = null;
        currentTable = string.Empty;
        gridData.DataSource = null;
        gridStructure.Rows.Clear();
        treeTables.Nodes.Clear();
        lbDataInfo.Text = " ";
        lbStatus.Text = "Не подключено";
        provider?.Dispose();
        provider = null;
        SetUiState(false);
        Log.Info("Отключено");
    }

    private void RefreshTree()
    {
        if (provider is null) return;
        var p = provider;
        SetUiState(true);
        Task.Run(p.GetTables)
            .ContinueWith(t =>
            {
                SetUiState(false);
                if (t.Exception is not null)
                {
                    Log.Error("Список таблиц не получен", t.Exception.InnerException ?? t.Exception);
                    return;
                }
                treeTables.BeginUpdate();
                treeTables.Nodes.Clear();
                foreach (var name in t.Result!)
                    treeTables.Nodes.Add(name);
                treeTables.EndUpdate();
                Log.Info("Таблиц в базе: " + t.Result!.Count);
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void PickTable(string table) => LoadTable(table, FilterText());

    private string? FilterText()
    {
        var text = tbFilter.Text.Trim();
        return text.Length == 0 ? null : text;
    }

    private void LoadCurrent()
    {
        if (currentTable.Length > 0)
            LoadTable(currentTable, FilterText());
    }

    private void ApplyFilter()
    {
        if (currentTable.Length > 0)
            LoadTable(currentTable, FilterText());
    }

    private void LoadTable(string table, string? where)
    {
        if (provider is null || !provider.IsConnected)
            return;

        var p = provider;
        SetUiState(true);
        Log.Info("Загрузка: " + table + (where is null ? string.Empty : " (WHERE " + where + ")"));

        Task.Run(() =>
        {
            var cols = p.GetColumns(table);
            var data = p.LoadRows(table, where, 5000, out var truncated);
            return (cols, data, truncated);
        })
        .ContinueWith(t =>
        {
            SetUiState(false);
            if (t.Exception is not null)
            {
                Log.Error("Загрузка таблицы " + table, t.Exception.InnerException ?? t.Exception);
                MessageBox.Show(this, (t.Exception.InnerException ?? t.Exception).Message, "Ошибка загрузки",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var (cols, data, truncated) = t.Result;
            currentCols = cols;
            currentData = data;
            currentTable = table;
            gridData.DataSource = data;
            FillStructure(cols);
            lbDataInfo.Text = string.Format("Таблица: {0}   строк: {1}{2}", table, data.Rows.Count, truncated ? "   (показаны первые 5000)" : string.Empty);

            if (!cols.Any(c => c.IsPrimary))
                Log.Warn("Таблица " + table + " без первичного ключа: правка/удаление строк сопоставляется по значению всех колонок.");
            if (truncated)
                Log.Info("Ограничение просмотра: показано 5000 строк из " + table + ". Смотрите всё через SQL-консоль или экспорт.");
            Log.Info("Загружено строк: " + data.Rows.Count);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void FillStructure(List<DbColumn> cols)
    {
        gridStructure.Rows.Clear();
        foreach (var col in cols)
        {
            gridStructure.Rows.Add(
                col.Name,
                string.IsNullOrEmpty(col.NativeType) ? "?" : col.NativeType,
                col.IsNullable ? "да" : "нет",
                col.IsPrimary ? "да" : "нет",
                col.IsAutoIncrement ? "да" : "нет",
                col.Default ?? "—",
                KindHint(col.Kind));
        }
    }

    private static string KindHint(ColumnKind kind) => kind switch
    {
        ColumnKind.Int => "целое",
        ColumnKind.Long => "большое целое",
        ColumnKind.Double => "дробное",
        ColumnKind.Decimal => "число с точкой",
        ColumnKind.Bool => "true / false",
        ColumnKind.Date => "дата/время",
        ColumnKind.Guid => "GUID",
        ColumnKind.Bytes => "BLOB (правка закрыта)",
        _ => "текст"
    };

    private void GridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.Value is null || e.Value is not DBNull)
        {
            if (e.Value is byte[])
            {
                var bytes = (byte[])e.Value!;
                e.Value = "BLOB " + bytes.Length + " байт";
            }
            return;
        }

        e.Value = "NULL";
        if (e.CellStyle is not null)
        {
            e.CellStyle.ForeColor = Color.Gray;
            e.CellStyle.BackColor = Color.FromArgb(246, 246, 246);
        }
    }

    private void GridCellBeginEdit(object? sender, DataGridViewCellCancelEventArgs e)
    {
        if (provider is null || currentCols is null || provider.ReadOnly)
        {
            e.Cancel = true;
            return;
        }

        var colName = DataColName(e.ColumnIndex);
        var col = FindCol(colName);
        if (col is null || col.IsAutoIncrement || col.IsBytes)
        {
            if (col is not null)
                Log.Info("Колонка " + col.Name + " закрыта для правки: " + KindHint(col.Kind));
            e.Cancel = true;
        }
    }

    private void GridCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (provider is null || currentCols is null || currentData is null)
            return;

        var rowView = gridData.Rows[e.RowIndex].DataBoundItem as DataRowView;
        if (rowView is null)
            return;
        var row = rowView.Row;
        var colName = DataColName(e.ColumnIndex);
        var col = FindCol(colName);
        if (col is null || col.IsAutoIncrement || col.IsBytes)
            return;

        object? original;
        if (row.HasVersion(DataRowVersion.Original))
        {
            var old = row[colName, DataRowVersion.Original];
            original = old is DBNull ? null : old;
        }
        else
        {
            var seen = row[colName];
            original = seen is DBNull ? null : seen;
        }

        try
        {
            var newValue = ValueParser.Parse(col, gridData[e.ColumnIndex, e.RowIndex].Value as string);

            if (string.Equals(InvariantText(newValue), InvariantText(original), StringComparison.Ordinal))
            {
                row.AcceptChanges();
                return;
            }

            var affected = provider.UpdateCell(currentTable, currentCols, BuildRowKeys(row), colName, newValue);
            if (affected == 0)
                throw new InvalidOperationException("Строка не найдена в базе по ключу. Обновите таблицу и повторите правку.");

            row[colName] = newValue ?? DBNull.Value;
            row.AcceptChanges();
            Log.Info("Изменение сохранено: " + currentTable + "." + colName);
        }
        catch (Exception ex)
        {
            row.RejectChanges();
            Log.Error("Правка не сохранена: " + currentTable + "." + colName, ex);
            MessageBox.Show(this, ex.Message, "Правка не сохранена", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string InvariantText(object? value)
    {
        if (value is null || value is DBNull)
            return string.Empty;
        if (value is bool flag)
            return flag ? "true" : "false";
        if (value is DateTime dt)
            return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        if (value is double d)
            return d.ToString("R", CultureInfo.InvariantCulture);
        if (value is float f)
            return f.ToString("R", CultureInfo.InvariantCulture);
        if (value is decimal m)
            return m.ToString(CultureInfo.InvariantCulture);
        if (value is byte[] bytes)
            return "0x" + Convert.ToHexString(bytes);
        return value.ToString() ?? string.Empty;
    }

    private void GridDataError(object? sender, DataGridViewDataErrorEventArgs e)
    {
        e.ThrowException = false;
    }

    private void GridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.C)
        {
            CopySelectedCells();
            e.Handled = true;
        }
    }

    private void SetCellNull()
    {
        if (provider is null || currentCols is null || currentTable.Length == 0)
            return;

        if (provider.ReadOnly)
        {
            Log.Warn("Источник открыт только для просмотра: NULL установить нельзя.");
            return;
        }

        var cell = gridData.CurrentCell;
        if (cell is null)
            return;

        var rowView = gridData.Rows[cell.RowIndex].DataBoundItem as DataRowView;
        if (rowView is null)
            return;

        var colName = DataColName(cell.ColumnIndex);
        var col = FindCol(colName);
        if (col is null || col.IsPrimary || col.IsAutoIncrement || col.IsBytes)
        {
            Log.Warn("Колонка " + colName + " не может принимать NULL (ключ или тип BLOB).");
            return;
        }

        try
        {
            var affected = provider.UpdateCell(currentTable, currentCols, BuildRowKeys(rowView.Row), colName, null);
            if (affected == 0)
                throw new InvalidOperationException("Строка не найдена в базе по ключу. Обновите таблицу и повторите.");

            rowView.Row[colName] = DBNull.Value;
            rowView.Row.AcceptChanges();
            Log.Info("Установлен NULL: " + currentTable + "." + colName);
        }
        catch (Exception ex)
        {
            Log.Error("NULL не установлен", ex);
            MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void CopyCell()
    {
        var cell = gridData.CurrentCell;
        if (cell is null)
            return;
        SetClipboard(ToText(gridData[cell.ColumnIndex, cell.RowIndex].Value));
    }

    private void CopyCurrentRow()
    {
        var rowView = gridData.CurrentRow?.DataBoundItem as DataRowView;
        if (rowView is null)
            return;
        var values = rowView.Row.ItemArray.Select(CellText);
        SetClipboard(string.Join("\t", values));
    }

    private void CopyAllRows()
    {
        if (currentData is null || currentData.Columns.Count == 0)
            return;

        const int cap = 10000;
        if (currentData.Rows.Count > cap)
        {
            Log.Warn("Копирование всех строк ограничено " + cap + " строк.");
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join("\t", currentData.Columns.Cast<DataColumn>().Select(c => c.ColumnName)));
        foreach (DataRow row in currentData.Rows)
            sb.AppendLine(string.Join("\t", row.ItemArray.Select(CellText)));
        SetClipboard(sb.ToString());
        Log.Info("Скопировано строк: " + currentData.Rows.Count);
    }

    private void CopySelectedCells()
    {
        if (gridData.SelectedCells.Count == 0 || gridData.SelectedCells.Count > 4000)
        {
            CopyCurrentRow();
            return;
        }

        var sb = new System.Text.StringBuilder();
        foreach (var group in gridData.SelectedCells.Cast<DataGridViewCell>()
                     .GroupBy(c => c.RowIndex).OrderBy(g => g.Key))
        {
            sb.AppendLine(string.Join("\t", group.OrderBy(c => c.ColumnIndex).Select(c => CellText(c.Value))));
        }
        SetClipboard(sb.ToString());
    }

    private static string CellText(object? value)
    {
        var text = ToText(value);
        return text.Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\t", "    ");
    }

    private static string ToText(object? value)
    {
        if (value is null || value is DBNull)
            return string.Empty;
        if (value is byte[] bytes)
            return "0x" + Convert.ToHexString(bytes);
        if (value is DateTime dt)
            return dt.ToString("yyyy-MM-dd HH:mm:ss");
        return value.ToString() ?? string.Empty;
    }

    private static void SetClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch (Exception ex) { Log.Warn("Буфер обмена недоступен: " + ex.Message); }
    }

    private void AddRow()
    {
        if (provider is null || currentCols is null || currentTable.Length == 0)
            return;

        var values = RowDialog.Show(this, currentTable, currentCols);
        if (values is null)
            return;

        var p = provider;
        var table = currentTable;
        var cols = currentCols;
        SetUiState(true);
        Task.Run(() => p.InsertRow(table, cols, values))
            .ContinueWith(t =>
            {
                SetUiState(false);
                if (t.Exception is not null)
                {
                    Log.Error("Строка не добавлена", t.Exception.InnerException ?? t.Exception);
                    MessageBox.Show(this, (t.Exception.InnerException ?? t.Exception).Message, "Ошибка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                Log.Info("Строка добавлена в " + table);
                LoadCurrent();
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void DeleteRow()
    {
        if (provider is null || currentCols is null || currentData is null || currentTable.Length == 0)
            return;

        if (!provider.CanDeleteRows)
        {
            Log.Warn("Драйвер не поддерживает удаление строк в этом источнике.");
            return;
        }

        var rows = gridData.SelectedRows.Cast<DataGridViewRow>()
            .Where(r => r.DataBoundItem is DataRowView)
            .Select(r => ((DataRowView)r.DataBoundItem!).Row)
            .Distinct()
            .ToList();

        if (rows.Count == 0 && gridData.CurrentRow is { DataBoundItem: DataRowView drv })
            rows.Add(drv.Row);

        if (rows.Count == 0)
        {
            Log.Info("Удаление: строки не выбраны");
            return;
        }

        if (currentCols.Count(c => c.IsPrimary) == 0)
        {
            var strict = MessageBox.Show(this,
                "У таблицы нет первичного ключа: строки ищутся в базе по полному совпадению всех колонок.\n" +
                "Если данные строки изменились после загрузки, она не будет удалена.\n\nПродолжить?",
                "Удаление без ключа", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (strict != DialogResult.Yes)
                return;
        }

        var answer = MessageBox.Show(this, "Удалить строк: " + rows.Count + "?", "Подтверждение",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
            return;

        var p = provider;
        var table = currentTable;
        var cols = currentCols;
        SetUiState(true);

        Task.Run(() =>
        {
            var done = new List<DataRow>();
            foreach (var row in rows)
            {
                var affected = p.DeleteRow(table, cols!, BuildRowKeys(row));
                if (affected > 0)
                    done.Add(row);
                else
                    Log.Warn("Удаление: строка не найдена в базе по ключу, пропущена");
            }
            return done;
        })
        .ContinueWith(t =>
        {
            SetUiState(false);
            var done = new List<DataRow>();
            if (t.Exception is not null)
            {
                var inner = t.Exception.InnerException ?? t.Exception;
                Log.Error("Удаление не выполнено", inner);
                MessageBox.Show(this, inner.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                done = t.Result!;
            }

            if (done.Count > 0)
            {
                foreach (var row in done)
                    currentData!.Rows.Remove(row);
                currentData!.AcceptChanges();
            }

            if (done.Count < rows.Count || t.Exception is not null)
            {
                Log.Warn(string.Format("Удалено строк: {0} из {1}", done.Count, rows.Count));
                MessageBox.Show(this,
                    "Удалено строк: " + done.Count + " из " + rows.Count +
                    ".\nНайденные по ключу строки не удались. Обновите таблицу.",
                    "Удаление", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                Log.Info("Удалено строк: " + done.Count);
            }

            lbDataInfo.Text = string.Format("Таблица: {0}   строк: {1}", currentTable, currentData!.Rows.Count);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void ExportGrid()
    {
        if (currentTable.Length == 0)
            return;
        if (gridData.DataSource is not DataTable data || data.Rows.Count == 0)
        {
            MessageBox.Show(this, "Нет строк для экспорта.", "dbtool", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ExportDialog.Export(this, data, currentTable + ".csv");
    }

    private void OpenSql()
    {
        if (provider is null)
            return;

        if (sqlForm is null || sqlForm.IsDisposed)
            sqlForm = new SqlConsoleForm(provider);
        sqlForm.Show(this);
        sqlForm.Activate();

        var p = provider;
        Task.Run(p.GetTables)
            .ContinueWith(t =>
            {
                if (t.Exception is not null)
                    return;
                if (sqlForm is not null && !sqlForm.IsDisposed)
                    sqlForm.SetTables(t.Result!);
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void OpenConvert()
    {
        if (provider is null)
            return;
        using var form = new ConvertForm(provider);
        form.ShowDialog(this);
    }

    private void FixDatabase()
    {
        if (provider is null)
            return;

        var p = provider;
        SetUiState(true);
        Task.Run(() =>
            {
                var report = new FixReport();
                try
                {
                    p.FixDatabase(report);
                }
                catch (Exception ex)
                {
                    report.Fail("Проверка не выполнена: " + ex.Message);
                }
                return report;
            })
            .ContinueWith(t =>
            {
                SetUiState(false);
                var report = t.Result;

                if (report.NeedsRecovery && p is SqliteProvider sp)
                {
                    var targetPath = sp.FilePath + ".fixed.db";
                    var answer = MessageBox.Show(this,
                        "База SQLite повреждена (integrity_check нашёл проблемы).\nВосстановить читаемые таблицы в новый файл?\n\n" + targetPath,
                        "Восстановление базы", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (answer == DialogResult.Yes)
                    {
                        SetUiState(true);
                        Task.Run(() => sp.RecoverTo(targetPath))
                            .ContinueWith(r =>
                            {
                                SetUiState(false);
                                if (r.Exception is not null)
                                    Log.Error("Восстановление не выполнено", r.Exception.InnerException ?? r.Exception);
                                else
                                    Log.Info("Восстанованная база создана: " + r.Result);
                                MessageBox.Show(this, "Восстановление завершено, подробности в журнале.", "dbtool",
                                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }, TaskScheduler.FromCurrentSynchronizationContext());
                        return;
                    }
                }

                MessageBox.Show(this,
                    report.HasErrors ? "Найдены проблемы, подробности в журнале." : "Проверка завершена, проблем не найдено.",
                    "Исправить БД", MessageBoxButtons.OK, report.HasErrors ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private DbColumn? FindCol(string name)
        => currentCols?.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    private string DataColName(int index)
    {
        var column = gridData.Columns[index];
        return string.IsNullOrEmpty(column.DataPropertyName) ? column.Name : column.DataPropertyName;
    }

    private Dictionary<string, object?> BuildRowKeys(DataRow row)
    {
        var result = new Dictionary<string, object?>();
        if (currentCols is null)
            return result;
        var keyCols = currentCols.Where(c => c.IsPrimary).ToList();
        if (keyCols.Count == 0)
            keyCols = currentCols;
        foreach (var col in keyCols)
        {
            if (!row.Table.Columns.Contains(col.Name))
                throw new InvalidOperationException("Колонка " + col.Name + " отсутствует в загруженных данных.");
            result[col.Name] = row[col.Name] is DBNull ? null : row[col.Name];
        }
        return result;
    }
}
