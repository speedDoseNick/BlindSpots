using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CameraTracker
{
    public partial class GridCanvas : Panel
    {
        private readonly List<Shape> _shapes = new();
        public Shape? SelectedShape { get; private set; }

        public event Action<Shape?>? SelectionChanged;
        public event Action? ShapeChanged;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int GridSize { get; set; } = 20;

        // Поля для перетаскивания
        private bool _isDragging;
        private Point _dragOffset;
        private Shape? _draggingShape;

        public GridCanvas()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Color.White;
            TabStop = true; // Чтобы контрол мог получать фокус клавиатуры
        }

        public void AddShape(Shape shape)
        {
            shape.OnUpdated = () => Invalidate();

            _shapes.Add(shape);
            SelectedShape = shape;
            SelectionChanged?.Invoke(SelectedShape);
            Invalidate();
        }

        public void DeleteSelected()
        {
            if (SelectedShape != null)
            {
                _shapes.Remove(SelectedShape);
                SelectedShape = null;
                SelectionChanged?.Invoke(null);
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus(); // Захватываем фокус для клавиатуры

            var oldSelection = SelectedShape;
            _draggingShape = null;
            _isDragging = false;

            for (int i = _shapes.Count - 1; i >= 0; i--)
            {
                if (_shapes[i].Contains(e.Location))
                {
                    SelectedShape = _shapes[i];
                    _draggingShape = SelectedShape;

                    // Запоминаем смещение курсора относительно угла фигуры
                    _dragOffset = new Point(e.X - SelectedShape.Location.X, e.Y - SelectedShape.Location.Y);
                    _isDragging = true;
                    break;
                }
            }

            if (SelectedShape != oldSelection)
                SelectionChanged?.Invoke(SelectedShape);

            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_isDragging && _draggingShape != null)
            {
                int newX = e.X - _dragOffset.X;
                int newY = e.Y - _dragOffset.Y;

                bool isShiftPressed = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;

                if (isShiftPressed)
                {
                    // Привязка к сетке
                    newX = (int)(Math.Round(newX / (double)GridSize) * GridSize);
                    newY = (int)(Math.Round(newY / (double)GridSize) * GridSize);
                }

                _draggingShape.Location = new Point(newX, newY);

                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _isDragging = false;
            _draggingShape = null;

            ShapeChanged?.Invoke();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DrawGrid(e.Graphics);
            foreach (var s in _shapes) s.Draw(e.Graphics);
            if (SelectedShape != null) DrawSelection(e.Graphics);
        }

        private void DrawGrid(Graphics g)
        {
            using var pen = new Pen(Color.LightGray, 1);
            for (int x = 0; x < ClientSize.Width; x += GridSize)
                g.DrawLine(pen, x, 0, x, ClientSize.Height);
            for (int y = 0; y < ClientSize.Height; y += GridSize)
                g.DrawLine(pen, 0, y, ClientSize.Width, y);
        }

        private void DrawSelection(Graphics g)
        {
            if (SelectedShape == null) return;
            using var pen = new Pen(Color.Blue, 2) { DashStyle = DashStyle.Dot };
            var rect = new Rectangle(SelectedShape.Location, SelectedShape.Size);
            g.DrawRectangle(pen, rect);
        }
    }
}