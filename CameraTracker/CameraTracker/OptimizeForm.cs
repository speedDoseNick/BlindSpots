using System;
using System.Windows.Forms;

public partial class OptimizeForm : Form
{
    public int Generations => (int)nudGenerations.Value;
    public int Population => (int)nudPopulation.Value;
    public float PosRange => (float)nudPosRange.Value;
    public float AngleRange => (float)nudAngleRange.Value;
    public int RayCount => (int)nudRayCount.Value;
    public int OverlapPenalty => (int)nudOverlapPenalty.Value;
    public int UnBoundPenalty => (int)nudUnBoundPenalty.Value;

    public OptimizeForm()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        this.Text = "Оптимизация";
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.StartPosition = FormStartPosition.CenterParent;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.Width = 360;
        this.Height = 380;

        var lbl = new Label() { Left = 12, Top = 10, Width = 320, Text = "Укажите параметры оптимизации:" };
        this.Controls.Add(lbl);

        int y = 40;

        void AddLabel(string text)
        {
            this.Controls.Add(new Label() { Left = 12, Top = y, Width = 160, Text = text });
        }

        void AddControl(Control c)
        {
            c.Left = 180; c.Top = y - 3; c.Width = 140;
            this.Controls.Add(c);
            y += 36;
        }

        nudGenerations = new NumericUpDown() { Minimum = 1, Maximum = 100000, Value = 200 };
        AddLabel("Поколений:");
        AddControl(nudGenerations);

        nudPopulation = new NumericUpDown() { Minimum = 1, Maximum = 10000, Value = 50 };
        AddLabel("Размер популяции:");
        AddControl(nudPopulation);

        nudPosRange = new NumericUpDown() { Minimum = 0, Maximum = 10000, DecimalPlaces = 1, Increment = 1, Value = 125 };
        AddLabel("Диапазон смещения (px):");
        AddControl(nudPosRange);

        nudAngleRange = new NumericUpDown() { Minimum = 0, Maximum = 360, DecimalPlaces = 1, Increment = 1, Value = 180 };
        AddLabel("Диапазон угла (deg):");
        AddControl(nudAngleRange);

        nudRayCount = new NumericUpDown() { Minimum = 1, Maximum = 10000, Value = 360 };
        AddLabel("Количество лучей:");
        AddControl(nudRayCount);

        nudOverlapPenalty = new NumericUpDown() { Minimum = 0, Maximum = 1000000, Value = 1000 };
        AddLabel("Штраф за пересечение полей зрения:");
        AddControl(nudOverlapPenalty);

        nudUnBoundPenalty = new NumericUpDown() { Minimum = 0, Maximum = 1000000, Value = 125 };
        AddLabel("Штраф за выход за границы:");
        AddControl(nudUnBoundPenalty);

        var btnOk = new Button() { Text = "OK", DialogResult = DialogResult.OK, Left = 80, Width = 80, Top = y + 6 };
        var btnCancel = new Button() { Text = "Отмена", DialogResult = DialogResult.Cancel, Left = 180, Width = 80, Top = y + 6 };

        this.Controls.Add(btnOk);
        this.Controls.Add(btnCancel);

        this.AcceptButton = btnOk;
        this.CancelButton = btnCancel;
    }

    // controls exposed as fields for InitializeComponent
    private NumericUpDown nudGenerations;
    private NumericUpDown nudPopulation;
    private NumericUpDown nudPosRange;
    private NumericUpDown nudAngleRange;
    private NumericUpDown nudRayCount;
    private NumericUpDown nudOverlapPenalty;
    private NumericUpDown nudUnBoundPenalty;
}
