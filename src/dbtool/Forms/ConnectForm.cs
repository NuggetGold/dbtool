using DbTool.Core;

namespace DbTool.Forms;

public class ConnectForm : Form
{
    private static readonly ProviderKind[] Kinds = { ProviderKind.Sqlite, ProviderKind.MySql, ProviderKind.Postgres, ProviderKind.Access };

    private ComboBox cmbKind = null!;
    private TableLayoutPanel host = null!;
    private readonly Dictionary<ProviderKind, TableLayoutPanel> panels = new();
    private readonly Dictionary<TableLayoutPanel, Dictionary<string, TextBox>> fields = new();
    private readonly Dictionary<TableLayoutPanel, Dictionary<string, CheckBox>> checks = new();

    public DbProvider? Provider { get; private set; }

    public ConnectForm()
    {
        Text = "Подключение к базе данных";
        Width = 560;
        Height = 430;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(6) };
        cmbKind = new ComboBox { Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var k in Kinds)
            cmbKind.Items.Add(ProviderFactory.DisplayName(k));
        cmbKind.SelectedIndex = 0;
        cmbKind.SelectedIndexChanged += (_, _) => ShowPanel();
        top.Controls.Add(new Label { Text = "Тип базы:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        top.Controls.Add(cmbKind);

        host = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };

        BuildSqlitePanel();
        BuildMySqlPanel();
        BuildPostgresPanel();
        BuildAccessPanel();

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var btnCancel = new Button { Text = "Отмена", Width = 110, DialogResult = DialogResult.Cancel };
        var btnOk = new Button { Text = "Подключить", Width = 130 };
        btnOk.Click += (_, _) => Connect();
        bottom.Controls.Add(btnCancel);
        bottom.Controls.Add(btnOk);

        Controls.Add(host);
        Controls.Add(top);
        Controls.Add(bottom);
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        ShowPanel();
    }

    private void ShowPanel()
    {
        var kind = Kinds[cmbKind.SelectedIndex];
        foreach ((var key, var panel) in panels)
            panel.Visible = key == kind;
    }

    private TableLayoutPanel NewPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            Padding = new Padding(8, 24, 8, 8)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        host.Controls.Add(panel);
        panels.Add((ProviderKind)panels.Count, panel);
        fields.Add(panel, new Dictionary<string, TextBox>());
        checks.Add(panel, new Dictionary<string, CheckBox>());
        panel.Visible = false;
        return panel;
    }

    private void AddRow(TableLayoutPanel panel, string label, string key, string initial, bool password = false)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        var lbl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left };
        var box = new TextBox { Dock = DockStyle.Fill, Text = initial };
        if (password)
            box.UseSystemPasswordChar = true;
        panel.Controls.Add(lbl, 0, panel.RowCount - 1);
        panel.Controls.Add(box, 1, panel.RowCount - 1);
        fields[panel][key] = box;
    }

    private void AddFileRow(TableLayoutPanel panel)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        var lbl = new Label { Text = "Файл базы:", AutoSize = true, Anchor = AnchorStyles.Left };
        var box = new TextBox { Dock = DockStyle.Fill };
        var browse = new Button { Text = "…", Width = 32, Dock = DockStyle.Right };

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill };
        flow.Controls.Add(box);
        flow.Controls.Add(browse);

        browse.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog();
            if (panel == panels[ProviderKind.Sqlite])
                dlg.Filter = "Базы SQLite|*.db;*.sqlite;*.sqlite3|Все файлы|*.*";
            else
                dlg.Filter = "Access/Excel|*.accdb;*.mdb;*.xlsx;*.xls;*.xlsb|Все файлы|*.*";
            if (dlg.ShowDialog(this) == DialogResult.OK)
                box.Text = dlg.FileName;
        };

        panel.Controls.Add(lbl, 0, panel.RowCount - 1);
        panel.Controls.Add(flow, 1, panel.RowCount - 1);
        fields[panel]["file"] = box;
    }

    private void AddCheckRow(TableLayoutPanel panel, string label, string key)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        var cb = new CheckBox { Text = label, AutoSize = true, Anchor = AnchorStyles.Left };
        panel.Controls.Add(cb, 0, panel.RowCount - 1);
        panel.SetColumnSpan(cb, 2);
        checks[panel][key] = cb;
        panel.RowCount++;
    }

    private void BuildSqlitePanel()
    {
        var panel = NewPanel();
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        var note = new Label
        {
            Text = "Укажите существующий файл базы SQLite (.db, .sqlite, .sqlite3).",
            Dock = DockStyle.Fill,
            AutoSize = false
        };
        panel.Controls.Add(note, 0, 0);
        panel.SetColumnSpan(note, 2);
        AddFileRow(panel);
        AddCheckRow(panel, "Файл может отсутствовать — создать при подключении", "createFile");
    }

    private void BuildMySqlPanel() => BuildServerPanel("host MySQL", "3306", "root");

    private void BuildPostgresPanel() => BuildServerPanel("host PostgreSQL", "5432", "postgres");

    private void BuildServerPanel(string dbLabel, string port, string user)
    {
        var panel = NewPanel();
        AddRow(panel, "Хост:", "host", "localhost");
        AddRow(panel, "Порт:", "port", port);
        AddRow(panel, dbLabel, "user", user);
        AddRow(panel, "Пароль:", "password", string.Empty, true);
        AddRow(panel, "База данных:", "database", string.Empty);
    }

    private void BuildAccessPanel()
    {
        var panel = NewPanel();
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        var note = new Label
        {
            Text = "Файлы .accdb/.mdb — полный редактор; .xlsx/.xls — только просмотр. Нужен установленный драйвер ACE.",
            Dock = DockStyle.Fill,
            AutoSize = false
        };
        panel.Controls.Add(note, 0, 0);
        panel.SetColumnSpan(note, 2);
        AddFileRow(panel);
    }

    private void Connect()
    {
        var kind = Kinds[cmbKind.SelectedIndex];
        var panel = panels[kind];
        var cfg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (fields[panel].TryGetValue("file", out var fileBox))
        {
            var path = fileBox.Text;
            if (!string.IsNullOrWhiteSpace(path) && !File.Exists(path))
            {
                if (checks[panel].TryGetValue("createFile", out var create) && create.Checked)
                    cfg["create"] = "1";
                else
                {
                    MessageBox.Show("Файл не найден: " + path, "dbtool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show("Укажите существующий файл базы.", "dbtool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            cfg["file"] = path;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(fields[panel]["database"].Text))
            {
                MessageBox.Show("Укажите имя базы данных.", "dbtool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            cfg["host"] = fields[panel]["host"].Text.Trim();
            cfg["port"] = fields[panel]["port"].Text.Trim();
            cfg["user"] = fields[panel]["user"].Text.Trim();
            cfg["password"] = fields[panel]["password"].Text;
            cfg["database"] = fields[panel]["database"].Text.Trim();
        }

        try
        {
            var provider = ProviderFactory.Create(kind);
            provider.Open(cfg);
            Provider = provider;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (FileNotFoundException ex)
        {
            Log.Error("Подключение не выполнено", ex);
            MessageBox.Show(ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            Log.Error("Подключение не выполнено", ex);
            MessageBox.Show(ex.Message, "Ошибка подключения", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
