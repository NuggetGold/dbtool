using System.Text;
using DbTool.Core;

namespace DbTool.Forms;

public class ExportDialog : Form
{
    private TextBox tbPath = null!;
    private ComboBox cmbDelimiter = null!;
    private ComboBox cmbEncoding = null!;

    public string FilePath { get; private set; } = string.Empty;
    public char Delimiter { get; private set; } = ';';
    public Encoding EncodingValue { get; private set; } = new UTF8Encoding(true);

    public ExportDialog(string suggestedName)
    {
        Text = "Экспорт CSV";
        Width = 560;
        Height = 220;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));

        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        grid.Controls.Add(new Label { Text = "Куда сохранить:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        var flowPath = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        flowPath.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        flowPath.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        tbPath = new TextBox { Dock = DockStyle.Fill, Text = suggestedName };
        var browse = new Button { Text = "…", Width = 34 };
        browse.Click += (_, _) =>
        {
            using var dlg = new SaveFileDialog { FileName = suggestedName, Filter = "CSV-файлы|*.csv|Все файлы|*.*" };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                tbPath.Text = dlg.FileName;
        };
        browse.Dock = DockStyle.Fill;
        flowPath.Controls.Add(tbPath, 0, 0);
        flowPath.Controls.Add(browse, 1, 0);
        grid.Controls.Add(flowPath, 1, 0);

        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        grid.Controls.Add(new Label { Text = "Разделитель:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        cmbDelimiter = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbDelimiter.Items.AddRange(new object[] { "точка с запятой (;)", "запятая (,)", "табуляция" });
        cmbDelimiter.SelectedIndex = 0;
        grid.Controls.Add(cmbDelimiter, 1, 1);

        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        grid.Controls.Add(new Label { Text = "Кодировка:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        cmbEncoding = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbEncoding.Items.AddRange(new object[] { "UTF-8 с BOM (Excel)", "UTF-8", "Windows-1251" });
        cmbEncoding.SelectedIndex = 0;
        grid.Controls.Add(cmbEncoding, 1, 2);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(6)
        };
        var btnCancel = new Button { Text = "Отмена", Width = 100, DialogResult = DialogResult.Cancel };
        var btnOk = new Button { Text = "Экспорт", Width = 120 };
        btnOk.Click += (_, _) => AcceptMe();
        bottom.Controls.Add(btnCancel);
        bottom.Controls.Add(btnOk);

        Controls.Add(grid);
        Controls.Add(bottom);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private void AcceptMe()
    {
        if (string.IsNullOrWhiteSpace(tbPath.Text))
        {
            MessageBox.Show("Укажите путь к файлу.", "dbtool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            EncodingValue = cmbEncoding.SelectedIndex switch
            {
                1 => new UTF8Encoding(false),
                2 => Encoding.GetEncoding(1251),
                _ => new UTF8Encoding(true)
            };
            Delimiter = cmbDelimiter.SelectedIndex switch
            {
                1 => ',',
                2 => '\t',
                _ => ';'
            };
            FilePath = tbPath.Text;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Кодировка недоступна: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public static void Export(IWin32Window owner, DataTable data, string suggestedName)
    {
        using var dlg = new ExportDialog(suggestedName);
        if (dlg.ShowDialog(owner) != DialogResult.OK)
            return;

        try
        {
            Csv.Save(data, dlg.FilePath, dlg.Delimiter, dlg.EncodingValue);
            Log.Info(string.Format("Экспорт CSV: {0} строк -> {1}", data.Rows.Count, dlg.FilePath));
        }
        catch (Exception ex)
        {
            Log.Error("Экспорт CSV не выполнен", ex);
            MessageBox.Show(ex.Message, "Ошибка экспорта", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
