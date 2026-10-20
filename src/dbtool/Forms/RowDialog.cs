using DbTool.Core;

namespace DbTool.Forms;

public class RowDialog : Form
{
    private readonly List<DbColumn> writable;
    private readonly List<TextBox> inputs = new();
    private readonly List<CheckBox> nullBoxes = new();

    public Dictionary<string, object?> Values { get; private set; } = new();

    private RowDialog(string table, List<DbColumn> cols)
    {
        writable = cols
            .Where(c => !c.IsAutoIncrement && !c.IsBytes)
            .ToList();

        Text = "Добавить строку: " + table;
        Width = 560;
        Height = Math.Min(760, 150 + Math.Max(1, writable.Count) * 30);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var grid = new TableLayoutPanel();
        grid.Dock = DockStyle.Top;
        grid.AutoSize = true;
        grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        grid.ColumnCount = 3;
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));

        foreach (var col in writable)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            grid.Controls.Add(new Label { Text = col.Name + "  (" + KindHint(col.Kind) + ")", AutoSize = true, Anchor = AnchorStyles.Left }, 0, grid.RowCount);
            var box = new TextBox { Dock = DockStyle.Fill };
            grid.Controls.Add(box, 1, grid.RowCount);
            var cb = new CheckBox { Text = "NULL", AutoSize = true, Anchor = AnchorStyles.Left };
            grid.Controls.Add(cb, 2, grid.RowCount);
            cb.CheckedChanged += (_, _) => box.Enabled = !cb.Checked;
            box.Tag = col;
            inputs.Add(box);
            nullBoxes.Add(cb);
            grid.RowCount++;
        }

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(6)
        };
        var btnCancel = new Button { Text = "Отмена", Width = 100, DialogResult = DialogResult.Cancel };
        var btnAdd = new Button { Text = "Добавить", Width = 120 };
        btnAdd.Click += (_, _) => TryBuildValues();
        bottom.Controls.Add(btnCancel);
        bottom.Controls.Add(btnAdd);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(grid);

        Controls.Add(scroll);
        Controls.Add(bottom);
        CancelButton = btnCancel;
        AcceptButton = btnAdd;
    }

    public static Dictionary<string, object?>? Show(IWin32Window owner, string table, List<DbColumn> cols)
    {
        using var form = new RowDialog(table, cols);
        return form.ShowDialog(owner) == DialogResult.OK ? form.Values : null;
    }

    private void TryBuildValues()
    {
        var values = new Dictionary<string, object?>();
        for (int i = 0; i < inputs.Count; i++)
        {
            if (nullBoxes[i].Checked)
            {
                values[writable[i].Name] = null;
                continue;
            }

            try
            {
                values[writable[i].Name] = ValueParser.Parse(writable[i], inputs[i].Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show(@"Колонка """ + writable[i].Name + @""": " + ex.Message,
                    "Ошибка значения", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        if (values.Count == 0)
        {
            MessageBox.Show("Заполните хотя бы одну колонку или отметьте NULL.",
                "dbtool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Values = values;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static string KindHint(ColumnKind kind) => kind switch
    {
        ColumnKind.Int => "целое",
        ColumnKind.Long => "большое целое",
        ColumnKind.Double => "дробное",
        ColumnKind.Decimal => "число с точкой",
        ColumnKind.Bool => "true/false",
        ColumnKind.Date => "дата/время",
        ColumnKind.Guid => "GUID",
        _ => "текст"
    };
}
