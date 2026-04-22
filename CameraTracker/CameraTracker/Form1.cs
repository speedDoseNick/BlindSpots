

namespace CameraTracker
{
    public partial class Form1 : Form
    {
        private readonly GridCanvas _canvas;
        private readonly PropertyGrid _propertyGrid;

        public Form1()
        {
            InitializeComponent();
            KeyPreview = true;
            KeyDown += Form1_KeyDown;

            _canvas = new GridCanvas
            {
                Dock = DockStyle.Fill
            };

            _propertyGrid = new PropertyGrid
            {
                Dock = DockStyle.Right,
                Width = 250
            };

            _canvas.SelectionChanged += shape => _propertyGrid.SelectedObject = shape;
            _propertyGrid.PropertyValueChanged += (s, e) =>
            {
                _propertyGrid.Refresh(); // Обновляем саму таблицу свойств
            };
            _canvas.ShapeChanged += () => _propertyGrid.Refresh();
            _propertyGrid.PropertyValueChanged += (s, e) =>
            {
                _canvas.Invalidate();
                _propertyGrid.Refresh();
            };

            // Кнопка "Добавить Круг"
            var btnCircle = new Button
            {
                Text = "Круг",
                Dock = DockStyle.Top,
                Height = 30
            };
            btnCircle.Click += (s, e) => _canvas.AddShape(new CircleShape
            {
                Location = new Point(100, 100),
                Size = new Size(100, 100),
                FillColor = Color.LightGreen
            });

            // Кнопка "Добавить Линию"
            var btnLine = new Button
            {
                Text = "Линия",
                Dock = DockStyle.Top,
                Height = 30
            };
            btnLine.Click += (s, e) => _canvas.AddShape(new LineShape
            {
                Location = new Point(50, 50),      // Начало
                Size = new Size(200, 150),         // Смещение (конец будет 250, 200)
                FillColor = Color.Black,
                Thickness = 5                      // Толстая стена
            });

            // Добавляем кнопки на форму (порядок важен для DockStyle.Top)
            Controls.Add(_canvas);
            Controls.Add(_propertyGrid);
            Controls.Add(btnLine);     // Сверху
            Controls.Add(btnCircle);   // Ниже

            var addBtn = new Button
            {
                Text = "Добавить квадрат",
                Dock = DockStyle.Top,
                Height = 30
            };
            addBtn.Click += (s, e) => _canvas.AddShape(new RectShape
            {
                Location = new Point(50, 50),
                Size = new Size(60, 60)
            });

            Controls.Add(_canvas);
            Controls.Add(_propertyGrid);
            Controls.Add(addBtn);

            Text = "Редактор фигур";
            Size = new Size(900, 600);
        }

        private void Form1_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                _canvas.DeleteSelected();
                e.SuppressKeyPress = true; // Чтобы не срабатывали системные звуки
            }
        }
    }
}
