using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace CameraTracker
{
    public abstract class Shape
    {
        // 👇 Добавляем это свойство. Это "крючок" для перерисовки.
        public Action? OnUpdated { get; set; }

        public Point Location { get; set; }
        public Size Size { get; set; }
        public Color FillColor { get; set; } = Color.LightBlue;

        public abstract bool Contains(Point p);
        public abstract void Draw(Graphics g);

        // 👇 Модифицируем сеттеры, чтобы вызывать OnUpdated при изменении

        [Category("Position")]
        public int X
        {
            get => Location.X;
            set
            {
                Location = new Point(value, Location.Y);
                OnUpdated?.Invoke(); // <--- Сказать канвасу "Обновись!"
            }
        }

        [Category("Position")]
        public int Y
        {
            get => Location.Y;
            set
            {
                Location = new Point(Location.X, value);
                OnUpdated?.Invoke(); // <--- Сказать канвасу "Обновись!"
            }
        }

        [Category("Size")]
        public int Width
        {
            get => Size.Width;
            set
            {
                Size = new Size(value, Size.Height);
                OnUpdated?.Invoke();
            }
        }

        [Category("Size")]
        public int Height
        {
            get => Size.Height;
            set
            {
                Size = new Size(Size.Width, value);
                OnUpdated?.Invoke();
            }
        }

        // Не забудьте про цвет
        [Category("Appearance")]
        public Color Color
        {
            get => FillColor;
            set
            {
                FillColor = value;
                OnUpdated?.Invoke();
            }
        }
    }

    // 2. Класс прямоугольника
    public class RectShape : Shape
    {
        public override bool Contains(Point p) => new Rectangle(Location, Size).Contains(p);

        public override void Draw(Graphics g) =>
            g.FillRectangle(new SolidBrush(FillColor), new Rectangle(Location, Size));
    }

    public class LineShape : Shape
    {
        // Свойство толщины линии
        [Category("Appearance")]
        public float Thickness { get; set; } = 2f;

        public override bool Contains(Point p)
        {
            // Начало (A) и Конец (B) линии
            Point start = Location;
            Point end = new Point(Location.X + Size.Width, Location.Y + Size.Height);

            // Вычисляем расстояние от точки клика до отрезка
            float dist = DistancePointToSegment(p, start, end);

            // Если расстояние меньше толщины/2 + небольшой допуск (для удобства клика)
            return dist <= (Thickness / 2 + 5);
        }

        public override void Draw(Graphics g)
        {
            Point start = Location;
            Point end = new Point(Location.X + Size.Width, Location.Y + Size.Height);

            using var pen = new Pen(FillColor, Thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(pen, start, end);
        }

        // Математика: расстояние от точки P до отрезка AB
        private float DistancePointToSegment(Point p, Point a, Point b)
        {
            float l2 = (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
            if (l2 == 0) return (float)Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));

            float t = ((p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y)) / l2;
            t = Math.Max(0, Math.Min(1, t));

            float projX = a.X + t * (b.X - a.X);
            float projY = a.Y + t * (b.Y - a.Y);

            return (float)Math.Sqrt((p.X - projX) * (p.X - projX) + (p.Y - projY) * (p.Y - projY));
        }
    }

    public class CircleShape : Shape
    {
        public override bool Contains(Point p)
        {
            // Формула эллипса: ((x-h)/a)^2 + ((y-k)/b)^2 <= 1
            float centerX = Location.X + Size.Width / 2f;
            float centerY = Location.Y + Size.Height / 2f;
            float radiusX = Size.Width / 2f;
            float radiusY = Size.Height / 2f;

            float dx = p.X - centerX;
            float dy = p.Y - centerY;

            return (dx * dx) / (radiusX * radiusX) + (dy * dy) / (radiusY * radiusY) <= 1;
        }

        public override void Draw(Graphics g)
        {
            using var brush = new SolidBrush(FillColor);
            // Рисуем эллипс внутри прямоугольника Location/Size
            g.FillEllipse(brush, new Rectangle(Location, Size));
        }
    }

}