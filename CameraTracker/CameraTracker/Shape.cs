using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.Json.Serialization;

namespace CameraTracker
{
    /// <summary>
    /// Абстрактный базовый класс для всех графических примитивов в редакторе.
    /// Обеспечивает единый интерфейс для отрисовки, обработки событий мыши,
    /// сериализации и интеграции с PropertyGrid.
    /// </summary>
    /// <remarks>
    /// Класс реализует паттерн "Шаблонный метод" через абстрактные методы
    /// Contains() и Draw(), которые должны быть переопределены в наследниках.
    /// 
    /// Атрибуты JsonDerivedType обеспечивают полиморфную десериализацию:
    /// при загрузке JSON сериализатор определяет конкретный тип фигуры
    /// по строковому идентификатору ("rect", "circle" и т.д.).
    /// </remarks>
    [JsonDerivedType(typeof(RectShape), "rect")]
    [JsonDerivedType(typeof(CircleShape), "circle")]
    [JsonDerivedType(typeof(TriangleShape), "triangle")]
    [JsonDerivedType(typeof(PolylineShape), "polyline")]
    [JsonDerivedType(typeof(CameraShape), "camera")]
    public abstract class Shape
    {
        /// <summary>
        /// Позиция левого верхнего угла ограничивающего прямоугольника фигуры.
        /// </summary>
        /// <remarks>
        /// Свойство скрыто из PropertyGrid через [Browsable(false)], так как
        /// для редактирования предоставляются отдельные свойства X и Y с
        /// локализованными названиями и категоризацией.
        /// </remarks>
        [Browsable(false)]
        public Point Location { get; set; }

        /// <summary>
        /// Размеры фигуры в пикселях (ширина и высота).
        /// </summary>
        /// <remarks>
        /// Для всех фигур используется единая система координат на основе
        /// ограничивающего прямоугольника (bounding box), что упрощает
        /// реализацию изменения размера и привязки к сетке.
        /// </remarks>
        [Browsable(false)]
        public Size Size { get; set; }

        /// <summary>
        /// Цвет заливки фигуры. По умолчанию — полностью прозрачный.
        /// </summary>
        /// <remarks>
        /// Свойство скрыто из PropertyGrid; для редактирования используется
        /// обёртка Color с локализованным отображением.
        /// </remarks>
        [Browsable(false)]
        public Color FillColor { get; set; } = Color.Transparent;

        /// <summary>
        /// Угол поворота фигуры в градусах относительно центра.
        /// </summary>
        /// <remarks>
        /// Положительные значения соответствуют повороту по часовой стрелке.
        /// Поворот применяется через матричные трансформации GDI+ в методе Draw().
        /// </remarks>
        [Category("Трансформация"), DisplayName("Угол")]
        public float Angle { get; set; } = 0;

        private float _borderThickness = 2f;

        /// <summary>
        /// Толщина линии обводки в пикселях.
        /// </summary>
        /// <remarks>
        /// Значение 0 отключает отрисовку обводки. Изменение свойства
        /// автоматически вызывает событие OnUpdated для перерисовки холста.
        /// </remarks>
        [Category("Вид"), DisplayName("Толщина обводки"), Description("Толщина линии обводки")]
        public virtual float BorderThickness
        {
            get => _borderThickness;
            set { _borderThickness = value; OnUpdated?.Invoke(); }
        }

        private Color _borderColor = Color.Black;

        /// <summary>
        /// Цвет линии обводки фигуры.
        /// </summary>
        [Category("Вид"), DisplayName("Цвет обводки"), Description("Цвет линии обводки")]
        public virtual Color BorderColor
        {
            get => _borderColor;
            set { _borderColor = value; OnUpdated?.Invoke(); }
        }

        /// <summary>
        /// Делегат для уведомления о изменении свойств фигуры.
        /// </summary>
        /// <remarks>
        /// Используется для связи фигуры с холстом (GridCanvas). При изменении
        /// любого свойства, влияющего на визуальное представление, вызывается
        /// этот делегат для инициирования перерисовки.
        /// 
        /// Свойство исключено из сериализации ([JsonIgnore]) и PropertyGrid
        /// ([Browsable(false)]), так как является служебным механизмом.
        /// </remarks>
        [Browsable(false)]
        [JsonIgnore] public Action? OnUpdated { get; set; }

        /// <summary>
        /// Определяет, содержится ли указанная точка внутри фигуры.
        /// </summary>
        /// <param name="p">Точка в мировых координатах для проверки.</param>
        /// <returns>True, если точка находится внутри фигуры; иначе False.</returns>
        /// <remarks>
        /// Абстрактный метод, реализуемый в наследниках согласно геометрии фигуры.
        /// Используется для обработки кликов мыши и выделения объектов.
        /// </remarks>
        public abstract bool Contains(Point p);

        /// <summary>
        /// Отрисовывает фигуру на указанном графическом контексте.
        /// </summary>
        /// <param name="g">Объект Graphics для выполнения операций рисования.</param>
        /// <remarks>
        /// Реализация должна учитывать текущие трансформации (поворот) и
        /// корректно управлять состоянием Graphics через Save()/Restore()
        /// для изоляции трансформаций конкретной фигуры.
        /// </remarks>
        public abstract void Draw(Graphics g);

        /// <summary>
        /// Возвращает коллекцию ключевых точек фигуры для привязки (snapping).
        /// </summary>
        /// <returns>Перечисление точек в мировых координатах.</returns>
        /// <remarks>
        /// Для прямоугольника — четыре угла; для круга — центр; для ломаной —
        /// все вершины. Точки возвращаются с учётом текущего поворота фигуры,
        /// что обеспечивает корректную привязку при вращении объектов.
        /// </remarks>
        public virtual IEnumerable<Point> GetVertices() => Enumerable.Empty<Point>();

        /// <summary>
        /// Вычисляет смещение от позиции фигуры до ближайшей вершины.
        /// </summary>
        /// <param name="mousePos">Позиция курсора в мировых координатах.</param>
        /// <returns>Вектор смещения от Location до ближайшей вершины.</returns>
        /// <remarks>
        /// Используется при перетаскивании фигур с привязкой (Ctrl) для
        /// определения, какая именно вершина должна "прилипать" к целевой точке.
        /// </remarks>
        public virtual Point GetSnapOffset(Point mousePos) => Point.Empty;

        // =====================================================================
        // Свойства-обёртки для интеграции с PropertyGrid
        // =====================================================================
        // Предоставляют локализованные названия и категоризацию свойств,
        // при этом исключены из JSON-сериализации во избежание дублирования
        // данных с базовыми свойствами Location, Size, FillColor.

        [Category("Позиция"), DisplayName("X")]
        [JsonIgnore]
        public int X { get => Location.X; set { Location = new Point(value, Location.Y); OnUpdated?.Invoke(); } }

        [Category("Позиция"), DisplayName("Y")]
        [JsonIgnore]
        public int Y { get => Location.Y; set { Location = new Point(Location.X, value); OnUpdated?.Invoke(); } }

        [Category("Размер"), DisplayName("Ширина")]
        [JsonIgnore]
        public int Width { get => Size.Width; set { Size = new Size(value, Size.Height); OnUpdated?.Invoke(); } }

        [Category("Размер"), DisplayName("Высота")]
        [JsonIgnore]
        public int Height { get => Size.Height; set { Size = new Size(Size.Width, value); OnUpdated?.Invoke(); } }

        [Category("Вид"), DisplayName("Цвет заливки")]
        [JsonIgnore]
        public Color Color { get => FillColor; set { FillColor = value; OnUpdated?.Invoke(); } }
    }

    /// <summary>
    /// Реализация прямоугольной фигуры с поддержкой поворота и обводки.
    /// </summary>
    public class RectShape : Shape
    {
        /// <summary>
        /// Проверяет принадлежность точки прямоугольнику.
        /// </summary>
        /// <remarks>
        /// Проверка выполняется в локальных координатах без учёта поворота,
        /// что обеспечивает производительность при обработке событий мыши.
        /// Для точного hit-testing с учётом вращения требуется дополнительная
        /// трансформация координат, которая в текущей реализации опущена.
        /// </remarks>
        public override bool Contains(Point p)
        {
            return new Rectangle(Location, Size).Contains(p);
        }

        /// <summary>
        /// Отрисовывает прямоугольник с применением трансформаций.
        /// </summary>
        /// <remarks>
        /// Алгоритм отрисовки:
        /// 1. Сохраняется текущее состояние Graphics (Save)
        /// 2. Применяется последовательность трансформаций:
        ///    - Перенос в центр фигуры
        ///    - Поворот на заданный угол
        ///    - Обратный перенос в исходную позицию
        /// 3. Выполняется заливка и отрисовка обводки
        /// 4. Восстанавливается исходное состояние (Restore)
        /// 
        /// Использование try/finally гарантирует корректное восстановление
        /// состояния Graphics даже при возникновении исключений.
        /// </remarks>
        public override void Draw(Graphics g)
        {
            GraphicsState state = g.Save();
            try
            {
                float cx = Location.X + Size.Width / 2f;
                float cy = Location.Y + Size.Height / 2f;

                // Применяем поворот относительно центра фигуры
                g.TranslateTransform(cx, cy);
                g.RotateTransform(Angle);
                g.TranslateTransform(-cx, -cy);

                Rectangle rect = new Rectangle(Location, Size);

                using var fillBrush = new SolidBrush(FillColor);
                g.FillRectangle(fillBrush, rect);

                if (BorderThickness > 0)
                {
                    using var borderPen = new Pen(BorderColor, BorderThickness);
                    g.DrawRectangle(borderPen, rect);
                }
            }
            finally { g.Restore(state); }
        }

        /// <summary>
        /// Возвращает координаты четырёх углов прямоугольника с учётом поворота.
        /// </summary>
        /// <remarks>
        /// Вычисления выполняются через матрицу поворота 2D:
        /// x' = x·cos(θ) - y·sin(θ)
        /// y' = x·sin(θ) + y·cos(θ)
        /// 
        /// Результат округляется до целых координат для совместимости с
        /// системой привязки и отрисовки.
        /// </remarks>
        public override System.Collections.Generic.IEnumerable<Point> GetVertices()
        {
            PointF[] corners = new PointF[]
            {
                new PointF(Location.X, Location.Y),
                new PointF(Location.X + Size.Width, Location.Y),
                new PointF(Location.X + Size.Width, Location.Y + Size.Height),
                new PointF(Location.X, Location.Y + Size.Height)
            };

            if (Math.Abs(Angle) > 0.1f)
            {
                PointF center = new PointF(Location.X + Size.Width / 2f, Location.Y + Size.Height / 2f);
                float rad = Angle * (float)Math.PI / 180f;
                float cos = (float)Math.Cos(rad);
                float sin = (float)Math.Sin(rad);

                for (int i = 0; i < 4; i++)
                {
                    float dx = corners[i].X - center.X;
                    float dy = corners[i].Y - center.Y;
                    corners[i].X = center.X + dx * cos - dy * sin;
                    corners[i].Y = center.Y + dx * sin + dy * cos;
                }
            }

            foreach (var corner in corners)
                yield return Point.Round(corner);
        }

        /// <summary>
        /// Вычисляет смещение до ближайшей вершины для привязки.
        /// </summary>
        /// <remarks>
        /// Метод использует повёрнутые координаты вершин, возвращаемые
        /// GetVertices(), что обеспечивает корректную привязку независимо
        /// от угла поворота фигуры.
        /// </remarks>
        public override Point GetSnapOffset(Point mousePos)
        {
            var vertices = GetVertices().ToList();
            Point nearest = Location;
            float minDist = float.MaxValue;

            foreach (var v in vertices)
            {
                float dist = Distance(mousePos, v);
                if (dist < minDist) { minDist = dist; nearest = v; }
            }

            return new Point(nearest.X - Location.X, nearest.Y - Location.Y);
        }

        private float Distance(Point p1, Point p2) =>
            (float)Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
    }

    /// <summary>
    /// Реализация ломаной линии (полилинии) с произвольным количеством вершин.
    /// </summary>
    /// <remarks>
    /// Класс поддерживает динамическое добавление, удаление и перемещение
    /// вершин, а также привязку вершин к другим объектам сцены.
    /// </remarks>
    public class PolylineShape : Shape
    {
        /// <summary>
        /// Коллекция точек для сериализации в JSON.
        /// </summary>
        /// <remarks>
        /// Атрибут JsonPropertyName("Points") обеспечивает читаемое имя
        /// свойства в выходном JSON. Browsable(false) скрывает свойство
        /// из PropertyGrid, так как редактирование точек осуществляется
        /// через интерактивное перетаскивание на холсте.
        /// </remarks>
        [JsonPropertyName("Points")]
        [Browsable(false)]
        public List<Point> PointsData
        {
            get => _points;
            set { _points.Clear(); if (value != null) _points.AddRange(value); }
        }

        private readonly List<Point> _points = new();

        /// <summary>
        /// Возвращает вершины полилинии для системы привязки.
        /// </summary>
        public override IEnumerable<Point> GetVertices() => _points;

        /// <summary>
        /// Публичное только для чтения представление точек.
        /// </summary>
        /// <remarks>
        /// Возвращает IReadOnlyList для предотвращения модификации
        /// коллекции извне без использования специализированных методов.
        /// </remarks>
        [Browsable(false)]
        [JsonIgnore]
        public IReadOnlyList<Point> Points => _points.AsReadOnly();

        /// <summary>
        /// Переопределённые свойства обводки скрыты, так как полилиния
        /// использует собственное свойство Thickness для толщины линии.
        /// </summary>
        [Browsable(false)]
        public override float BorderThickness { get => base.BorderThickness; set => base.BorderThickness = value; }

        [Browsable(false)]
        public override Color BorderColor { get => base.BorderColor; set => base.BorderColor = value; }

        [Category("Вид"), DisplayName("Толщина линии")]
        public float Thickness { get; set; } = 6f;

        public PolylineShape()
        {
            FillColor = Color.Black;
        }

        public PolylineShape(Point start, Point end) : this()
        {
            _points.Add(start);
            _points.Add(end);
        }

        /// <summary>
        /// Добавляет новую точку в конец полилинии.
        /// </summary>
        public void AddPoint(Point p) { _points.Add(p); OnUpdated?.Invoke(); }

        /// <summary>
        /// Вставляет точку после указанного индекса.
        /// </summary>
        /// <param name="afterIndex">Индекс точки, после которой выполняется вставка.</param>
        /// <remarks>
        /// Вставка возможна только между существующими точками (не в начало
        /// и не после последней), что сохраняет топологию ломаной.
        /// </remarks>
        public void InsertPoint(int afterIndex, Point p)
        {
            if (afterIndex < 0 || afterIndex >= _points.Count - 1) return;
            _points.Insert(afterIndex + 1, p);
            OnUpdated?.Invoke();
        }

        /// <summary>
        /// Обновляет координаты точки по индексу.
        /// </summary>
        public void SetPoint(int index, Point p)
        {
            if (index >= 0 && index < _points.Count) { _points[index] = p; OnUpdated?.Invoke(); }
        }

        /// <summary>
        /// Находит индекс сегмента, ближайшего к указанной точке.
        /// </summary>
        /// <param name="p">Точка для поиска.</param>
        /// <param name="distance">Выходной параметр: расстояние до ближайшего сегмента.</param>
        /// <returns>Индекс сегмента или -1, если полилиния пуста.</returns>
        public int FindNearestSegment(Point p, out float distance)
        {
            int nearestSegment = -1;
            distance = float.MaxValue;

            for (int i = 0; i < _points.Count - 1; i++)
            {
                float dist = DistancePointToSegment(p, _points[i], _points[i + 1]);
                if (dist < distance) { distance = dist; nearestSegment = i; }
            }
            return nearestSegment;
        }

        /// <summary>
        /// Находит индекс вершины в радиусе заданного порога.
        /// </summary>
        /// <param name="p">Точка для поиска.</param>
        /// <param name="threshold">Максимальное расстояние для считания попадания.</param>
        /// <returns>Индекс вершины или -1, если ни одна не найдена.</returns>
        public int FindNearestVertex(Point p, float threshold = 8f)
        {
            for (int i = 0; i < _points.Count; i++)
                if (Distance(p, _points[i]) <= threshold) return i;
            return -1;
        }

        /// <summary>
        /// Проверяет, находится ли точка в зоне захвата полилинии.
        /// </summary>
        /// <remarks>
        /// Зона захвата определяется как расстояние до любого сегмента,
        /// не превышающее половину толщины линии плюс допуск в 5 пикселей
        /// для удобства взаимодействия.
        /// </remarks>
        public override bool Contains(Point p)
        {
            foreach (var segment in GetSegments())
            {
                float dist = DistancePointToSegment(p, segment.Start, segment.End);
                if (dist <= (Thickness / 2 + 5)) return true;
            }
            return false;
        }

        /// <summary>
        /// Отрисовывает полилинию с закруглёнными соединениями.
        /// </summary>
        public override void Draw(Graphics g)
        {
            if (_points.Count < 2) return;

            using var pen = new Pen(FillColor, Thickness)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            g.DrawLines(pen, _points.ToArray());
        }

        /// <summary>
        /// Отрисовывает маркеры вершин для визуальной индикации.
        /// </summary>
        /// <param name="g">Графический контекст.</param>
        /// <param name="highlightedIndex">Индекс вершины для подсветки (опционально).</param>
        public void DrawVertices(Graphics g, int? highlightedIndex = null)
        {
            foreach (var point in _points)
            {
                g.FillEllipse(Brushes.White, point.X - 4, point.Y - 4, 8, 8);
                g.DrawEllipse(Pens.Blue, point.X - 4, point.Y - 4, 8, 8);
            }

            if (highlightedIndex.HasValue && highlightedIndex.Value >= 0 && highlightedIndex.Value < _points.Count)
            {
                var p = _points[highlightedIndex.Value];
                g.FillEllipse(Brushes.Yellow, p.X - 5, p.Y - 5, 10, 10);
            }
        }

        private IEnumerable<(Point Start, Point End)> GetSegments()
        {
            for (int i = 0; i < _points.Count - 1; i++)
                yield return (_points[i], _points[i + 1]);
        }

        /// <summary>
        /// Вычисляет минимальное расстояние от точки до отрезка.
        /// </summary>
        /// <remarks>
        /// Алгоритм основан на проекции точки на прямую, содержащую отрезок,
        /// с последующей проверкой, попадает ли проекция в границы отрезка.
        /// Если проекция вне отрезка, возвращается расстояние до ближайшего конца.
        /// 
        /// См.: https://en.wikipedia.org/wiki/Distance_from_a_point_to_a_line
        /// </remarks>
        private float DistancePointToSegment(Point p, Point a, Point b)
        {
            float l2 = (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
            if (l2 == 0) return Distance(p, a);

            float t = ((p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y)) / l2;
            t = Math.Max(0, Math.Min(1, t));

            float projX = a.X + t * (b.X - a.X);
            float projY = a.Y + t * (b.Y - a.Y);

            return Distance(p, new Point((int)projX, (int)projY));
        }

        private float Distance(Point p1, Point p2) =>
            (float)Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
    }

    /// <summary>
    /// Реализация эллиптической фигуры (круга/овала) с поддержкой поворота.
    /// </summary>
    /// <remarks>
    /// Несмотря на то, что визуальный вид круга не меняется при повороте,
    /// свойство Angle поддерживается для единообразия интерфейса и возможности
    /// будущей расширения функциональности.
    /// </remarks>
    public class CircleShape : Shape
    {
        /// <summary>
        /// Проверяет принадлежность точки эллипсу.
        /// </summary>
        /// <remarks>
        /// Используется каноническое уравнение эллипса:
        /// ((x-h)/a)² + ((y-k)/b)² ≤ 1
        /// где (h,k) — центр, a и b — полуоси.
        /// </remarks>
        public override bool Contains(Point p)
        {
            float cx = Location.X + Size.Width / 2f;
            float cy = Location.Y + Size.Height / 2f;
            float rx = Size.Width / 2f;
            float ry = Size.Height / 2f;
            float dx = p.X - cx;
            float dy = p.Y - cy;

            return (dx * dx) / (rx * rx) + (dy * dy) / (ry * ry) <= 1;
        }

        /// <summary>
        /// Отрисовывает эллипс с применением трансформаций.
        /// </summary>
        public override void Draw(Graphics g)
        {
            GraphicsState state = g.Save();
            try
            {
                float cx = Location.X + Size.Width / 2f;
                float cy = Location.Y + Size.Height / 2f;

                g.TranslateTransform(cx, cy);
                g.RotateTransform(Angle);
                g.TranslateTransform(-cx, -cy);

                Rectangle rect = new Rectangle(Location, Size);

                using var fillBrush = new SolidBrush(FillColor);
                g.FillEllipse(fillBrush, rect);

                if (BorderThickness > 0)
                {
                    using var borderPen = new Pen(BorderColor, BorderThickness);
                    g.DrawEllipse(borderPen, rect);
                }
            }
            finally { g.Restore(state); }
        }

        /// <summary>
        /// Возвращает центр эллипса как единственную вершину для привязки.
        /// </summary>
        public override System.Collections.Generic.IEnumerable<Point> GetVertices()
        {
            yield return new Point(Location.X + Size.Width / 2, Location.Y + Size.Height / 2);
        }

        /// <summary>
        /// Возвращает смещение от Location до центра фигуры.
        /// </summary>
        public override Point GetSnapOffset(Point mousePos)
        {
            var c = new Point(Location.X + Size.Width / 2, Location.Y + Size.Height / 2);
            return new Point(c.X - Location.X, c.Y - Location.Y);
        }
    }

    /// <summary>
    /// Реализация треугольной фигуры с поддержкой поворота.
    /// </summary>
    /// <remarks>
    /// Треугольник определяется тремя вершинами в локальных координатах
    /// относительно ограничивающего прямоугольника. При отрисовке и проверке
    /// принадлежности точки применяются матричные трансформации.
    /// </remarks>
    public class TriangleShape : Shape
    {
        /// <summary>
        /// Возвращает исходные вершины треугольника в локальных координатах.
        /// </summary>
        /// <remarks>
        /// Вершины определяются как:
        /// - Верхняя: центр по X, верх по Y
        /// - Левая нижняя: левый край, нижний край
        /// - Правая нижняя: правый край, нижний край
        /// 
        /// Такая конфигурация обеспечивает равнобедренный треугольник,
        /// ориентированный вершиной вверх.
        /// </remarks>
        private PointF[] GetRawVertices() => new PointF[]
        {
            new PointF(Location.X + Size.Width / 2f, Location.Y),
            new PointF(Location.X, Location.Y + Size.Height),
            new PointF(Location.X + Size.Width, Location.Y + Size.Height)
        };

        /// <summary>
        /// Вычисляет экранные координаты вершин с учётом поворота.
        /// </summary>
        /// <remarks>
        /// Применяет матрицу поворота 2D к каждой вершине относительно
        /// центра ограничивающего прямоугольника. При угле близком к нулю
        /// возвращает исходные координаты для оптимизации.
        /// </remarks>
        private PointF[] GetRotatedVertices()
        {
            var raw = GetRawVertices();
            if (Math.Abs(Angle) < 0.1f) return raw;

            PointF center = new PointF(Location.X + Size.Width / 2f, Location.Y + Size.Height / 2f);
            float rad = Angle * (float)Math.PI / 180f;
            float cos = (float)Math.Cos(rad);
            float sin = (float)Math.Sin(rad);

            for (int i = 0; i < 3; i++)
            {
                float dx = raw[i].X - center.X;
                float dy = raw[i].Y - center.Y;
                raw[i].X = center.X + dx * cos - dy * sin;
                raw[i].Y = center.Y + dx * sin + dy * cos;
            }
            return raw;
        }

        /// <summary>
        /// Проверяет принадлежность точки треугольнику.
        /// </summary>
        /// <remarks>
        /// Использует алгоритм барицентрических координат через проверку
        /// знаков векторных произведений. Точка находится внутри треугольника,
        /// если все три векторных произведения имеют одинаковый знак.
        /// 
        /// Преимущество метода: не требует вычисления площадей и устойчив
        /// к численным погрешностям при работе с плавающей точкой.
        /// </remarks>
        public override bool Contains(Point p)
        {
            var v = GetRotatedVertices();
            return PointInTriangle(p, v[0], v[1], v[2]);
        }

        /// <summary>
        /// Алгоритм проверки точки в треугольнике через векторные произведения.
        /// </summary>
        /// <param name="pt">Проверяемая точка.</param>
        /// <param name="v1">Первая вершина треугольника.</param>
        /// <param name="v2">Вторая вершина треугольника.</param>
        /// <param name="v3">Третья вершина треугольника.</param>
        /// <returns>True, если точка внутри или на границе треугольника.</returns>
        private bool PointInTriangle(Point pt, PointF v1, PointF v2, PointF v3)
        {
            float cross1 = (v2.X - v1.X) * (pt.Y - v1.Y) - (v2.Y - v1.Y) * (pt.X - v1.X);
            float cross2 = (v3.X - v2.X) * (pt.Y - v2.Y) - (v3.Y - v2.Y) * (pt.X - v2.X);
            float cross3 = (v1.X - v3.X) * (pt.Y - v3.Y) - (v1.Y - v3.Y) * (pt.X - v3.X);

            bool hasNeg = (cross1 < 0) || (cross2 < 0) || (cross3 < 0);
            bool hasPos = (cross1 > 0) || (cross2 > 0) || (cross3 > 0);

            return !(hasNeg && hasPos);
        }

        /// <summary>
        /// Отрисовывает треугольник с применением трансформаций.
        /// </summary>
        public override void Draw(Graphics g)
        {
            GraphicsState state = g.Save();
            try
            {
                float cx = Location.X + Size.Width / 2f;
                float cy = Location.Y + Size.Height / 2f;

                g.TranslateTransform(cx, cy);
                g.RotateTransform(Angle);
                g.TranslateTransform(-cx, -cy);

                var pts = GetRawVertices().Select(p => Point.Round(p)).ToArray();

                using var fillBrush = new SolidBrush(FillColor);
                g.FillPolygon(fillBrush, pts);

                if (BorderThickness > 0)
                {
                    using var borderPen = new Pen(BorderColor, BorderThickness);
                    g.DrawPolygon(borderPen, pts);
                }
            }
            finally { g.Restore(state); }
        }

        /// <summary>
        /// Возвращает повёрнутые вершины треугольника для системы привязки.
        /// </summary>
        public override IEnumerable<Point> GetVertices()
        {
            return GetRotatedVertices().Select(p => Point.Round(p));
        }

        /// <summary>
        /// Вычисляет смещение до ближайшей вершины для привязки.
        /// </summary>
        public override Point GetSnapOffset(Point mousePos)
        {
            var vertices = GetRotatedVertices();
            Point nearest = Point.Round(vertices[0]);
            float minDist = float.MaxValue;

            foreach (var v in vertices)
            {
                float dist = Distance(mousePos, Point.Round(v));
                if (dist < minDist) { minDist = dist; nearest = Point.Round(v); }
            }
            return new Point(nearest.X - Location.X, nearest.Y - Location.Y);
        }

        private float Distance(Point p1, Point p2) =>
            (float)Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
    }

    public class CameraShape : Shape
    {
        //[Category("Camera")] public float Angle { get; set; } = 0f;
        [Category("Камера")]
        [JsonPropertyName("Radius")]
        public int Radius { get; set; } = 150;

        [Category("Камера")]
        [JsonPropertyName("Fov")] 
        public float Fov { get; set; } = 60f;

        public override bool Contains(Point p)
        {
            var cx = Location.X + Size.Width / 2f; var cy = Location.Y + Size.Height / 2f; var dx = p.X - cx; var dy = p.Y - cy; var dist = Math.Sqrt(dx * dx + dy * dy); if (dist > Radius) return false;
            var pointAngle = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI); float a = Angle; while (a <= -180) a += 360; while (a > 180) a -= 360; float pa = pointAngle; while (pa <= -180) pa += 360; while (pa > 180) pa -= 360;
            float diff = pa - a; while (diff <= -180) diff += 360; while (diff > 180) diff -= 360;
            return Math.Abs(diff) <= Fov / 2f;
        }
        public override void Draw(Graphics g)
        {
            var center = new PointF(Location.X + Size.Width / 2f, Location.Y + Size.Height / 2f); var startAngle = Angle - Fov / 2f;
            using (var path = new GraphicsPath())
            {                // сектор как дуга + две линии к центру                var rect = new RectangleF(center.X - Radius, center.Y - Radius, Radius * 2, Radius * 2);                path.AddPie(rect, startAngle, Fov);
                using (var brush = new SolidBrush(Color.FromArgb(80, FillColor))) g.FillPath(brush, path);
                using (var pen = new Pen(Color.FromArgb(160, FillColor), 2)) g.DrawPath(pen, path);
            }
            var markerSize = Math.Min(16, Math.Max(8, Math.Min(Size.Width, Size.Height)));            var markerRect = new Rectangle(Location.X + Size.Width / 2 - markerSize / 2,                                           Location.Y + Size.Height / 2 - markerSize / 2,                                           markerSize, markerSize);            g.FillEllipse(Brushes.DarkSlateGray, markerRect);
            // стрелка направления            var dir = new PointF(                center.X + (float)(Math.Cos(Angle * Math.PI / 180.0) * (markerSize + 8)),                center.Y + (float)(Math.Sin(Angle * Math.PI / 180.0) * (markerSize + 8))            );            using (var pen = new Pen(Color.DarkSlateGray, 2))                g.DrawLine(pen, center, dir);        }
        }
        public void DrawFov(Graphics g)
        {
            var center = new PointF(Location.X + Size.Width / 2f, Location.Y + Size.Height / 2f); var startAngle = Angle - Fov / 2f;
            using var path = new GraphicsPath(); float x = center.X - Radius; float y = center.Y - Radius; float w = Radius * 2f; float h = Radius * 2f; path.AddPie(x, y, w, h, startAngle, Fov);
            using (var brush = new SolidBrush(Color.FromArgb(80, FillColor))) g.FillPath(brush, path);
            using (var pen = new Pen(Color.FromArgb(160, FillColor), 2)) g.DrawPath(pen, path);
        }

        public override IEnumerable<Point> GetVertices()
        {
            yield return new Point(Location.X + Size.Width / 2, Location.Y + Size.Height / 2);
        }

        public override Point GetSnapOffset(Point mousePos)
        {
            var center = new Point(Location.X + Size.Width / 2, Location.Y + Size.Height / 2);
            return new Point(center.X - Location.X, center.Y - Location.Y);
        }
    }
}