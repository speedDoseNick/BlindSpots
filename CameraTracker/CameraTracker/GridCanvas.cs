using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace CameraTracker
{
    /// <summary>
    /// Интерактивный холст для редактирования графических примитивов.
    /// </summary>
    /// <remarks>
    /// Класс наследуется от Panel и предоставляет следующие возможности:
    /// - Отрисовка сетки с настраиваемым шагом
    /// - Создание, выделение, перемещение и удаление фигур
    /// - Изменение размера фигур через ручки по углам ограничивающей рамки
    /// - Поворот фигур вокруг центра через специальную ручку
    /// - Масштабирование холста колёсиком мыши с привязкой к курсору
    /// - Панорамирование холста средней кнопкой мыши
    /// - Магнитная привязка фигур к сетке (Shift) и к вершинам других фигур (Ctrl)
    /// - Работа с полилиниями: добавление вершин, перетаскивание отдельных точек
    /// - Управление порядком отрисовки фигур (слои)
    /// - Сериализация и десериализация сцены в формате JSON
    /// 
    /// Архитектурные особенности:
    /// - Используется двойная буферизация для устранения мерцания при отрисовке
    /// - Все координаты мыши конвертируются из экранных в мировые через ScreenToWorld()
    /// - Трансформации (масштаб, сдвиг) применяются к Graphics контексту в OnPaint()
    /// - Состояние взаимодействия хранится в приватных полях и сбрасывается в OnMouseUp()
    /// </remarks>
    public partial class GridCanvas : Panel
    {
        // В классе Canvas (или вашей форме/контроле)
        private Rectangle? _selectionRect = null;
        private PointF _selectionStartOffset = new PointF(75, 75);
        private PointF _selectionEndOffset = new PointF(175, 175);

        // Установка/сброс извне
        public void SetSelectionRect(Rectangle? rect)
        {
            _selectionRect = rect;
            this.Invalidate();
        }

  

        // =====================================================================
        // Поля данных
        // =====================================================================

        /// <summary>
        /// Коллекция всех фигур на холсте. Порядок в списке определяет Z-order:
        /// фигуры с меньшим индексом отрисовываются раньше (на заднем плане).
        /// </summary>
        private readonly List<Shape> _shapes = new();

        /// <summary>
        /// Ссылка на текущую выделенную фигуру. Используется для отображения
        /// управляющих элементов (рамка, ручки) и передачи в PropertyGrid.
        /// </summary>
        public Shape? SelectedShape { get; private set; }

        /// <summary>
        /// Событие, вызываемое при изменении выделенной фигуры.
        /// Подписчик обычно обновляет PropertyGrid.SelectedObject.
        /// </summary>
        public event Action<Shape?>? SelectionChanged;

        /// <summary>
        /// Событие, вызываемое при изменении свойств любой фигуры.
        /// Используется для обновления PropertyGrid и других зависимых компонентов.
        /// </summary>
        public event Action? ShapeChanged;

        /// <summary>
        /// Шаг сетки в пикселях мировых координат.
        /// </summary>
        /// <remarks>
        /// Атрибут DesignerSerializationVisibility.Hidden предотвращает
        /// сериализацию свойства в designer.cs, так как значение хранится
        /// в коде и не требует сохранения в проекте пользователя.
        /// </remarks>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int GridSize { get; set; } = 20;

        // ---------------------------------------------------------------------
        // Система масштабирования и панорамирования
        // ---------------------------------------------------------------------

        /// <summary>
        /// Текущий коэффициент масштабирования. 1.0 = 100%, значения >1 увеличивают,
        /// значения <1 уменьшают отображение.
        /// </summary>
        private float _zoom = 1.0f;

        /// <summary>
        /// Горизонтальное смещение начала координат в пикселях экрана.
        /// Положительное значение сдвигает содержимое вправо.
        /// </summary>
        private float _panX = 0f;

        /// <summary>
        /// Вертикальное смещение начала координат в пикселях экрана.
        /// Положительное значение сдвигает содержимое вниз.
        /// </summary>
        private float _panY = 0f;

        /// <summary>
        /// Минимально допустимый коэффициент масштабирования.
        /// Предотвращает чрезмерное уменьшение, при котором работа становится невозможной.
        /// </summary>
        private const float MinZoom = 0.1f;

        /// <summary>
        /// Максимально допустимый коэффициент масштабирования.
        /// Ограничивает увеличение для предотвращения потери контекста и проблем
        /// с производительностью при отрисовке.
        /// </summary>
        private const float MaxZoom = 5.0f;

        /// <summary>
        /// Флаг активного режима панорамирования (перетаскивания холста).
        /// </summary>
        private bool _isPanning;

        /// <summary>
        /// Позиция курсора в экранных координатах в момент начала панорамирования.
        /// Используется для вычисления дельты перемещения.
        /// </summary>
        private Point _panStartPos;

        // ---------------------------------------------------------------------
        // Система перетаскивания фигур
        // ---------------------------------------------------------------------

        /// <summary>
        /// Флаг активного режима перетаскивания фигуры.
        /// </summary>
        private bool _isDragging;

        /// <summary>
        /// Позиция курсора в мировых координатах в момент нажатия кнопки мыши.
        /// Используется как опорная точка для вычисления смещения при перемещении.
        /// </summary>
        private Point _dragStartPos;

        /// <summary>
        /// Позиция курсора в мировых координатах в момент нажатия.
        /// Отличается от _dragStartPos тем, что не обновляется в процессе перемещения
        /// и используется для расчёта абсолютного смещения.
        /// </summary>
        private Point _mouseDownPos;

        /// <summary>
        /// Исходная позиция перетаскиваемой фигуры в мировых координатах.
        /// Используется для вычисления новой позиции как функции от смещения курсора.
        /// </summary>
        private Point _shapeStartPos;

        /// <summary>
        /// Смещение от позиции фигуры (Location) до ближайшей вершины.
        /// Используется при привязке с зажатым Ctrl для корректного позиционирования
        /// фигуры относительно целевой вершины.
        /// </summary>
        private Point _snapOffset;

        /// <summary>
        /// Ссылка на фигуру, которая в данный момент перетаскивается.
        /// </summary>
        private Shape? _draggingShape;

        // ---------------------------------------------------------------------
        // Система работы с полилиниями
        // ---------------------------------------------------------------------

        /// <summary>
        /// Индекс вершины полилинии, которая перетаскивается.
        /// Значение -1 означает, что перетаскивается вся полилиния целиком.
        /// </summary>
        private int _dragVertexIndex = -1;

        /// <summary>
        /// Копия исходных координат вершин полилинии на момент начала перетаскивания.
        /// Используется для вычисления новых позиций вершин относительно начального состояния,
        /// что обеспечивает плавное и предсказуемое перемещение.
        /// </summary>
        private Point[] _originalPoints = Array.Empty<Point>();

        /// <summary>
        /// Флаг режима добавления новой вершины в полилинию.
        /// </summary>
        private bool _isAddingPoint;

        /// <summary>
        /// Индекс сегмента, после которого добавляется новая вершина.
        /// Используется в режиме _isAddingPoint для определения позиции вставки.
        /// </summary>
        private int _insertAfterSegment = -1;

        /// <summary>
        /// Кэш вершины, к которой в данный момент привязана перемещаемая точка.
        /// Используется для реализации гистерезиса: привязка удерживается, пока
        /// курсор не отойдёт на значительное расстояние, что устраняет "дрожание"
        /// при пограничных значениях расстояния.
        /// </summary>
        private Point? _snappedToVertex = null;

        // ---------------------------------------------------------------------
        // Система изменения размера фигур
        // ---------------------------------------------------------------------

        /// <summary>
        /// Флаг активного режима изменения размера фигуры.
        /// </summary>
        private bool _isResizing;

        /// <summary>
        /// Индекс ручки изменения размера, за которую осуществляется перетаскивание.
        /// Нумерация: 0 = верх-лево, 1 = верх-право, 2 = низ-право, 3 = низ-лево.
        /// Значение -1 означает, что режим изменения размера не активен.
        /// </summary>
        private int _resizeHandleIndex = -1;

        /// <summary>
        /// Исходные границы фигуры (Location + Size) на момент начала изменения размера.
        /// Используется как базовое состояние для вычисления новых размеров.
        /// </summary>
        private Rectangle _initialBounds;

        /// <summary>
        /// Размер управляющей ручки в пикселях экрана.
        /// </summary>
        private const int HandleSize = 8;

        /// <summary>
        /// Радиус зоны захвата управляющей ручки в пикселях экрана.
        /// Позволяет пользователю активировать ручку, кликнув рядом с её центром.
        /// </summary>
        private const int HandleThreshold = 6;

        // ---------------------------------------------------------------------
        // Система поворота фигур
        // ---------------------------------------------------------------------

        /// <summary>
        /// Флаг активного режима поворота фигуры.
        /// </summary>
        private bool _isRotating;

        /// <summary>
        /// Исходный угол поворота фигуры на момент начала операции.
        /// </summary>
        private float _initialAngle;

        /// <summary>
        /// Угол вектора от центра фигуры до курсора в момент начала поворота.
        /// Используется для вычисления относительного изменения угла.
        /// </summary>
        private float _startMouseAngle;

        /// <summary>
        /// Позиция ручки поворота в мировых координатах.
        /// Используется для hit-testing при определении, нажата ли ручка.
        /// </summary>
        private Point _rotationHandlePos;

        /// <summary>
        /// Центр текущей выделенной фигуры в мировых координатах.
        /// Используется как точка вращения для вычисления угла поворота.
        /// </summary>
        private Point _shapeCenter;

        private float _resizeHandleOffsetX = 0f;
        private float _resizeHandleOffsetY = 0f;

        // свойство полей камер
        [Category("Behavior")]
        [Description("If true, camera fields of view are drawn on top.")]
        [DefaultValue(false)]
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public bool ShowCameraFovs { get; set; } = false;



        // number of rays per camera (performance vs quality)
        private const int RayCount = 1800;


        // =====================================================================
        // Конструктор и базовые методы
        // =====================================================================

        /// <summary>
        /// Инициализирует новый экземпляр класса GridCanvas.
        /// </summary>
        /// <remarks>
        /// Настройка ключевых свойств контрола:
        /// - DoubleBuffered = true: включение двойной буферизации для плавной отрисовки
        /// - ResizeRedraw = true: автоматическая перерисовка при изменении размера
        /// - BackColor = White: установка фона для корректного отображения сетки
        /// - TabStop = true: разрешение получения фокуса клавиатуры для обработки горячих клавиш
        /// </remarks>
        /// 
        public GridCanvas()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Color.White;
            TabStop = true;
        }

        /// <summary>
        /// Добавляет новую фигуру на холст и выделяет её.
        /// </summary>
        /// <param name="shape">Экземпляр фигуры для добавления.</param>
        /// <remarks>
        /// Метод выполняет следующие действия:
        /// 1. Привязывает делегат OnUpdated фигуры к Invalidate() холста для авто-перерисовки
        /// 2. Добавляет фигуру в коллекцию _shapes
        /// 3. Устанавливает фигуру как выделенную и уведомляет подписчиков события
        /// 4. Инициирует перерисовку холста
        /// 
        /// Фигура добавляется в конец списка, что помещает её на передний план (Z-order).
        /// </remarks>
        public void AddShape(Shape shape)
        {
            shape.OnUpdated = () => Invalidate();
            _shapes.Add(shape);
            SelectedShape = shape;
            SelectionChanged?.Invoke(SelectedShape);
            Invalidate();
        }

        /// <summary>
        /// Удаляет текущую выделенную фигуру с холста.
        /// </summary>
        /// <remarks>
        /// Если выделенная фигура существует:
        /// - Удаляет её из коллекции _shapes
        /// - Сбрасывает SelectedShape в null
        /// - Уведомляет подписчиков события SelectionChanged
        /// - Инициирует перерисовку холста
        /// 
        /// Если фигура не выделена, метод не выполняет никаких действий.
        /// </remarks>
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

        // =====================================================================
        // Методы управления слоями (Z-order)
        // =====================================================================

        /// <summary>
        /// Перемещает указанную фигуру на передний план отрисовки.
        /// </summary>
        /// <param name="shape">Фигура для перемещения.</param>
        /// <remarks>
        /// Реализация:
        /// - Удаляет фигуру из текущего положения в списке _shapes
        /// - Добавляет её в конец списка, что соответствует максимальному Z-order
        /// - Инициирует перерисовку для применения изменений
        /// 
        /// Если фигура не найдена в списке или равна null, метод завершается без действий.
        /// </remarks>
        public void BringToFront(Shape? shape)
        {
            if (shape != null && _shapes.Remove(shape))
            {
                _shapes.Add(shape);
                Invalidate();
            }
        }

        /// <summary>
        /// Перемещает указанную фигуру на задний план отрисовки.
        /// </summary>
        /// <param name="shape">Фигура для перемещения.</param>
        /// <remarks>
        /// Реализация:
        /// - Удаляет фигуру из текущего положения в списке _shapes
        /// - Вставляет её в начало списка (индекс 0), что соответствует минимальному Z-order
        /// - Инициирует перерисовку для применения изменений
        /// 
        /// Если фигура не найдена в списке или равна null, метод завершается без действий.
        /// </remarks>
        public void SendToBack(Shape? shape)
        {
            if (shape != null && _shapes.Remove(shape))
            {
                _shapes.Insert(0, shape);
                Invalidate();
            }
        }

        // =====================================================================
        // Методы сериализации и десериализации
        // =====================================================================

        /// <summary>
        /// Сохраняет текущее состояние холста (все фигуры) в файл JSON.
        /// </summary>
        /// <param name="path">Полный путь к файлу для сохранения.</param>
        /// <remarks>
        /// Настройки сериализации:
        /// - WriteIndented = true: форматированный вывод для читаемости человеком
        /// - ColorJsonConverter: кастомный конвертер для корректной сериализации Color
        /// 
        /// Полиморфная сериализация обеспечивается атрибутами [JsonDerivedType]
        /// в базовом классе Shape, которые добавляют поле "$type" для определения
        /// конкретного типа фигуры при десериализации.
        /// 
        /// Исключаются из сериализации:
        /// - Свойства с [JsonIgnore]: OnUpdated, X, Y, Width, Height, Color (обёртки)
        /// - Служебные поля: Location, Size, FillColor (дублируются через обёртки)
        /// </remarks>
        public void SaveToFile(string path)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new ColorJsonConverter() }
            };
            var json = JsonSerializer.Serialize(_shapes, options);
            File.WriteAllText(path, json);
        }

        /// <summary>
        /// Загружает состояние холста из файла JSON.
        /// </summary>
        /// <param name="path">Полный путь к файлу для загрузки.</param>
        /// <remarks>
        /// Процесс загрузки:
        /// 1. Чтение содержимого файла в строку
        /// 2. Десериализация списка фигур с использованием полиморфного конвертера
        /// 3. Очистка текущей коллекции _shapes
        /// 4. Добавление загруженных фигур и восстановление делегатов OnUpdated
        /// 5. Сброс выделенной фигуры и инициирование перерисовки
        /// 
        /// Важно: после загрузки необходимо восстановить делегаты OnUpdated для каждой
        /// фигуры, так как они не сериализуются и по умолчанию равны null.
        /// </remarks>
        public void LoadFromFile(string path)
        {
            var options = new JsonSerializerOptions
            {
                Converters = { new ColorJsonConverter() }
            };
            var json = File.ReadAllText(path);
            var shapes = JsonSerializer.Deserialize<List<Shape>>(json, options);

            _shapes.Clear();
            if (shapes != null)
            {
                _shapes.AddRange(shapes);
                foreach (var s in _shapes) s.OnUpdated = () => Invalidate();
            }

            SelectedShape = null;
            SelectionChanged?.Invoke(null);
            Invalidate();
        }

        // =====================================================================
        // Преобразование координат
        // =====================================================================

        /// <summary>
        /// Преобразует координаты из пространства экрана в мировые координаты.
        /// </summary>
        /// <param name="screenPoint">Точка в координатах экрана (пиксели контрола).</param>
        /// <returns>Точка в мировых координатах с учётом текущего зума и панорамирования.</returns>
        /// <remarks>
        /// Формула преобразования:
        /// worldX = (screenX - panX) / zoom
        /// worldY = (screenY - panY) / zoom
        /// 
        /// Это обратное преобразование к тому, что применяется в OnPaint() при отрисовке.
        /// Все обработчики событий мыши должны использовать этот метод для получения
        /// корректных координат взаимодействия с фигурами.
        /// </remarks>
        private Point ScreenToWorld(Point screenPoint)
        {
            return new Point(
                (int)((screenPoint.X - _panX) / _zoom),
                (int)((screenPoint.Y - _panY) / _zoom)
            );
        }

        // =====================================================================
        // Методы работы с полилиниями
        // =====================================================================

        /// <summary>
        /// Инициирует процесс добавления новой вершины в выделенную полилинию.
        /// </summary>
        /// <param name="clickPosWorld">Позиция клика в мировых координатах.</param>
        /// <remarks>
        /// Алгоритм работы:
        /// 1. Проверка, что выделена полилиния (иначе выход)
        /// 2. Поиск ближайшей вершины в радиусе 10 пикселей
        /// 3. Если клик на конечной вершине (первой или последней):
        ///    - Добавляется новая вершина в той же позиции
        ///    - Активируется режим перетаскивания только что добавленной вершины
        /// 4. Если клик на промежуточной вершине:
        ///    - Активируется режим перетаскивания этой вершины (без добавления)
        /// 5. Если клик не на вершине:
        ///    - Поиск ближайшего сегмента
        ///    - Если сегмент найден в радиусе 10 пикселей:
        ///      * Вставка новой вершины в середину сегмента
        ///      * Активация режима перетаскивания новой вершины
        ///    - Иначе: активация режима перетаскивания всей полилинии
        /// 
        /// Метод устанавливает соответствующие флаги состояния и инициирует перерисовку.
        /// </remarks>
        public void StartAddingPoint(Point clickPosWorld)
        {
            if (SelectedShape is not PolylineShape polyline) return;
            int nearestVertex = polyline.FindNearestVertex(clickPosWorld, threshold: 10f);

            if (nearestVertex == 0 || nearestVertex == polyline.Points.Count - 1)
            {
                _isAddingPoint = true; _dragStartPos = clickPosWorld;
                var endPos = polyline.Points[nearestVertex];
                if (nearestVertex == 0) polyline.InsertPoint(0, new Point(endPos.X, endPos.Y));
                else polyline.AddPoint(new Point(endPos.X, endPos.Y));
                _dragVertexIndex = nearestVertex == 0 ? 0 : polyline.Points.Count - 1;
                _originalPoints = polyline.Points.ToArray(); _isDragging = true; Invalidate();
            }
            else if (nearestVertex > 0)
            {
                _isDragging = true; _dragVertexIndex = nearestVertex;
                _originalPoints = polyline.Points.ToArray(); _dragStartPos = clickPosWorld; Invalidate();
            }
            else
            {
                float distance; int segment = polyline.FindNearestSegment(clickPosWorld, out distance);
                if (segment >= 0 && distance < 10)
                {
                    _isAddingPoint = true; _insertAfterSegment = segment; _dragStartPos = clickPosWorld;
                    var p1 = polyline.Points[segment]; var p2 = polyline.Points[segment + 1];
                    polyline.InsertPoint(segment, new Point((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2));
                    _dragVertexIndex = segment + 1; _originalPoints = polyline.Points.ToArray();
                    _isDragging = true; Invalidate();
                }
                else
                {
                    _isDragging = true; _dragVertexIndex = -1;
                    _originalPoints = polyline.Points.ToArray(); _dragStartPos = clickPosWorld; Invalidate();
                }
            }
        }

        /// <summary>
        /// Завершает процесс добавления вершины и сбрасывает связанные флаги.
        /// </summary>
        /// <remarks>
        /// Вызывается при отпускании кнопки мыши после добавления вершины.
        /// Сбрасывает _isAddingPoint, _insertAfterSegment, _dragVertexIndex
        /// и инициирует финальную перерисовку.
        /// </remarks>
        public void FinishAddingPoint()
        {
            if (_isAddingPoint) { _isAddingPoint = false; _insertAfterSegment = -1; _dragVertexIndex = -1; Invalidate(); }
        }

        // =====================================================================
        // Hit-testing для управляющих элементов
        // =====================================================================

        /// <summary>
        /// Вычисляет координаты четырёх углов ограничивающей рамки выделенной фигуры
        /// с учётом текущего поворота.
        /// </summary>
        /// <returns>
        /// Массив из 4 точек в мировых координатах, представляющих углы рамки
        /// в порядке: верх-лево, верх-право, низ-право, низ-лево.
        /// Возвращает пустой массив, если фигура не выделена или является полилинией.
        /// </returns>
        /// <remarks>
        /// Алгоритм:
        /// 1. Получение исходных координат углов прямоугольника (Location, Size)
        /// 2. Если угол поворота отличен от нуля (порог 0.1 градуса):
        ///    - Вычисление центра прямоугольника
        ///    - Применение матрицы поворота 2D к каждой вершине:
        ///      x' = cx + (x-cx)·cos(θ) - (y-cy)·sin(θ)
        ///      y' = cy + (x-cx)·sin(θ) + (y-cy)·cos(θ)
        /// 3. Возвращение массива повёрнутых координат
        /// 
        /// Результат используется для отрисовки рамки выделения и hit-testing ручек.
        /// </remarks>
        private PointF[] GetSelectionCorners()
        {
            if (SelectedShape == null || SelectedShape is PolylineShape) return Array.Empty<PointF>();
            Rectangle rect = new Rectangle(SelectedShape.Location, SelectedShape.Size);
            PointF center = new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
            PointF[] corners = new PointF[]
            {
                new PointF(rect.Left, rect.Top),
                new PointF(rect.Right, rect.Top),
                new PointF(rect.Right, rect.Bottom),
                new PointF(rect.Left, rect.Bottom)
            };

            bool isRotated = Math.Abs(SelectedShape.Angle) > 0.1f;
            if (isRotated)
            {
                float rad = SelectedShape.Angle * (float)Math.PI / 180f;
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
            return corners;
        }

        /// <summary>
        /// Определяет, находится ли указанная точка в зоне захвата одной из ручек
        /// изменения размера выделенной фигуры.
        /// </summary>
        /// <param name="worldPoint">Точка в мировых координатах для проверки.</param>
        /// <returns>
        /// Индекс ручки (0-3) при попадании, или -1 если ни одна ручка не захвачена.
        /// </returns>
        /// <remarks>
        /// Проверка выполняется путём вычисления расстояния от точки до каждой
        /// из четырёх угловых вершин ограничивающей рамки. Если расстояние меньше
        /// порога HandleThreshold, считается что ручка захвачена.
        /// 
        /// Нумерация ручек соответствует порядку в GetSelectionCorners():
        /// 0 = верх-лево, 1 = верх-право, 2 = низ-право, 3 = низ-лево.
        /// </remarks>
        private int GetHandleAt(Point worldPoint)
        {
            var corners = GetSelectionCorners();
            if (corners.Length == 0) return -1;
            for (int i = 0; i < 4; i++)
            {
                Point cornerPoint = Point.Round(corners[i]);
                if (Distance(worldPoint, cornerPoint) < HandleThreshold) return i;
            }
            return -1;
        }

        /// <summary>
        /// Проверяет, находится ли указанная точка в зоне захвата ручки поворота.
        /// </summary>
        /// <param name="worldPoint">Точка в мировых координатах для проверки.</param>
        /// <returns>True, если точка находится в радиусе захвата ручки поворота.</returns>
        /// <remarks>
        /// Позиция ручки поворота (_rotationHandlePos) вычисляется и обновляется
        /// в методе DrawSelection() при каждой перерисовке. Проверка выполняется
        /// путём сравнения расстояния до этой точки с порогом HandleThreshold.
        /// </remarks>
        private bool IsRotationHandleAt(Point worldPoint)
        {
            if (SelectedShape == null || SelectedShape is PolylineShape) return false;
            return Distance(worldPoint, _rotationHandlePos) < HandleThreshold;
        }

        // =====================================================================
        // Обработчики событий мыши
        // =====================================================================

        /// <summary>
        /// Обработчик события нажатия кнопки мыши.
        /// </summary>
        /// <remarks>
        /// Последовательность обработки:
        /// 1. Конвертация координат мыши в мировые через ScreenToWorld()
        /// 2. Проверка режима панорамирования (средняя кнопка) → активация _isPanning
        /// 3. Проверка захвата ручки поворота → активация _isRotating, вычисление начального угла
        /// 4. Проверка захвата ручки изменения размера → активация _isResizing, сохранение исходных границ
        /// 5. Поиск фигуры под курсором (в обратном порядке Z-order для корректного выделения верхних)
        /// 6. Для полилинии: определение, захвачена ли отдельная вершина или вся фигура
        /// 7. Для других фигур: вычисление смещения привязки через GetSnapOffset()
        /// 8. Сохранение начальных координат для расчёта перемещения
        /// 9. Уведомление об изменении выделения и перерисовка
        /// 
        /// Важно: все координаты хранятся и обрабатываются в мировых координатах,
        /// что обеспечивает корректную работу при любом масштабе и панорамировании.
        /// </remarks>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            Point mouseWorldPos = ScreenToWorld(e.Location);

            if (e.Button == MouseButtons.Middle)
            {
                _isPanning = true;
                _panStartPos = new Point(e.X - (int)_panX, e.Y - (int)_panY);
                return;
            }

            if (IsRotationHandleAt(mouseWorldPos))
            {
                _isRotating = true;
                _shapeCenter = new Point(SelectedShape.Location.X + SelectedShape.Size.Width / 2, SelectedShape.Location.Y + SelectedShape.Size.Height / 2);
                _initialAngle = SelectedShape.Angle;
                _startMouseAngle = (float)Math.Atan2(mouseWorldPos.Y - _shapeCenter.Y, mouseWorldPos.X - _shapeCenter.X);
                return;
            }

            int handleIdx = GetHandleAt(mouseWorldPos);
            if (handleIdx >= 0)
            {
                _isResizing = true;
                _resizeHandleIndex = handleIdx;
                _initialBounds = new Rectangle(SelectedShape.Location, SelectedShape.Size);

                //Запоминаем смещение ручки от курсора
                var corners = GetSelectionCorners();
                if (handleIdx < corners.Length)
                {
                    var handle = corners[handleIdx];
                    _resizeHandleOffsetX = handle.X - mouseWorldPos.X;
                    _resizeHandleOffsetY = handle.Y - mouseWorldPos.Y;
                }
                return;
            }

            var oldSelection = SelectedShape;
            _draggingShape = null; _isDragging = false; _dragVertexIndex = -1;
            _shapeStartPos = Point.Empty; _snapOffset = Point.Empty;

            for (int i = _shapes.Count - 1; i >= 0; i--)
            {
                if (_shapes[i].Contains(mouseWorldPos))
                {
                    SelectedShape = _shapes[i]; _draggingShape = SelectedShape;
                    if (SelectedShape is PolylineShape polyline)
                    {
                        _dragVertexIndex = polyline.FindNearestVertex(mouseWorldPos, threshold: 10f);
                        _isDragging = true; _originalPoints = polyline.Points.ToArray();
                    }
                    else
                    {
                        _isDragging = true; _shapeStartPos = _draggingShape.Location;
                        _snapOffset = _draggingShape.GetSnapOffset(mouseWorldPos);
                    }
                    break;
                }
            }

            _mouseDownPos = mouseWorldPos;
            _dragStartPos = mouseWorldPos;

            if (SelectedShape != oldSelection) SelectionChanged?.Invoke(SelectedShape);
            Invalidate();
        }

        /// <summary>
        /// Обработчик события перемещения мыши.
        /// </summary>
        /// <remarks>
        /// Метод реализует конечный автомат с несколькими взаимоисключающими режимами:
        /// 
        /// 1. Режим панорамирования (_isPanning):
        ///    - Вычисление нового смещения _panX/_panY относительно начальной позиции
        ///    - Перерисовка холста
        ///    - Ранний выход (остальные режимы не обрабатываются)
        /// 
        /// 2. Режим поворота (_isRotating):
        ///    - Вычисление текущего угла вектора от центра фигуры до курсора
        ///    - Расчёт дельты угла относительно начального значения
        ///    - Применение дельты к исходному углу фигуры
        ///    - При зажатом Shift: округление угла до кратного 45 градусам
        ///    - Уведомление об изменении и перерисовка
        /// 
        /// 3. Режим изменения размера (_isResizing):
        ///    - Вычисление смещения курсора относительно позиции нажатия
        ///    - Применение смещения к исходным границам согласно индексу ручки
        ///    - Ограничение минимального размера (10 пикселей)
        ///    - При зажатом Shift: пропорциональное масштабирование относительно
        ///      противоположного угла с сохранением соотношения сторон
        ///    - При зажатом Ctrl: привязка перемещаемого угла к вершинам других фигур
        ///    - Применение новых Location и Size к фигуре
        /// 
        /// 4. Режим перетаскивания фигуры (_isDragging):
        ///    A. Для полилинии:
        ///       - Если перетаскивается отдельная вершина:
        ///         * Вычисление новой позиции с учётом смещения
        ///         * При Ctrl: привязка с гистерезисом через _snappedToVertex
        ///         * При Shift: привязка к сетке через Snap()
        ///       - Если перетаскивается вся полилиния:
        ///         * Применение смещения ко всем вершинам
        ///         * Привязка каждой вершины при Ctrl/Shift
        ///    B. Для других фигур:
        ///       - Вычисление новой позиции с учётом смещения
        ///       - При Ctrl: привязка с учётом _snapOffset для корректного позиционирования
        ///       - При Shift: привязка к сетке
        /// 
        /// После выполнения действий в любом режиме вызывается Invalidate() для перерисовки.
        /// </remarks>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            // === 1. Панорамирование (сдвиг холста) ===
            if (_isPanning)
            {
                _panX = e.X - _panStartPos.X;
                _panY = e.Y - _panStartPos.Y;
                Invalidate();
                return;
            }

            // Получаем координаты мыши в мире один раз
            Point mouseWorldPos = ScreenToWorld(e.Location);

            // === 2. Вращение ===
            if (_isRotating && SelectedShape != null)
            {
                float currentAngle = (float)Math.Atan2(mouseWorldPos.Y - _shapeCenter.Y, mouseWorldPos.X - _shapeCenter.X);
                float delta = currentAngle - _startMouseAngle;
                float newAngle = _initialAngle + (delta * 180f / (float)Math.PI);
                if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift)
                    newAngle = (float)(Math.Round(newAngle / 45) * 45);
                SelectedShape.Angle = newAngle;
                ShapeChanged?.Invoke(); Invalidate();
                return;
            }

            // === 3. Изменение размера (ИСПРАВЛЕНО) ===
            if (_isResizing && SelectedShape != null)
            {
                int nx = _initialBounds.X;
                int ny = _initialBounds.Y;
                int nw = _initialBounds.Width;
                int nh = _initialBounds.Height;
                bool shift = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;

                // Вычисляем новые координаты и размеры на основе текущей позиции мыши
                // Мы привязываем угол фигуры прямо к курсору, чтобы не было "скачка"
                switch (_resizeHandleIndex)
                {
                    case 0: // Верх-Лево
                        nx = mouseWorldPos.X;
                        ny = mouseWorldPos.Y;
                        nw = (_initialBounds.Right) - nx;
                        nh = (_initialBounds.Bottom) - ny;
                        break;
                    case 1: // Верх-Право
                        ny = mouseWorldPos.Y;
                        nw = mouseWorldPos.X - _initialBounds.X;
                        nh = (_initialBounds.Bottom) - ny;
                        break;
                    case 2: // Низ-Право
                        nw = mouseWorldPos.X - _initialBounds.X;
                        nh = mouseWorldPos.Y - _initialBounds.Y;
                        break;
                    case 3: // Низ-Лево
                        nx = mouseWorldPos.X;
                        nw = (_initialBounds.Right) - nx;
                        nh = mouseWorldPos.Y - _initialBounds.Y;
                        break;
                }

                // Ограничение минимального размера
                if (nw < 10) { nw = 10; if (_resizeHandleIndex == 0 || _resizeHandleIndex == 3) nx = _initialBounds.Right - 10; }
                if (nh < 10) { nh = 10; if (_resizeHandleIndex == 0 || _resizeHandleIndex == 1) ny = _initialBounds.Bottom - 10; }

                // Shift: Пропорциональное масштабирование
                if (shift)
                {
                    Point fixedCorner = _resizeHandleIndex switch
                    {
                        0 => new Point(_initialBounds.Right, _initialBounds.Bottom), // Фиксируем правый-нижний
                        1 => new Point(_initialBounds.Left, _initialBounds.Bottom),  // Фиксируем левый-нижний
                        2 => new Point(_initialBounds.Left, _initialBounds.Top),     // Фиксируем левый-верхний
                        3 => new Point(_initialBounds.Right, _initialBounds.Top),    // Фиксируем правый-верхний
                        _ => Point.Empty
                    };

                    float initDiag = (float)Math.Sqrt(Math.Pow(_initialBounds.Width, 2) + Math.Pow(_initialBounds.Height, 2));
                    if (initDiag < 1) initDiag = 1;

                    // Считаем расстояние от фиксированного угла до мыши
                    float newDiag = (float)Math.Sqrt(Math.Pow(mouseWorldPos.X - fixedCorner.X, 2) + Math.Pow(mouseWorldPos.Y - fixedCorner.Y, 2));
                    float scale = Math.Max(newDiag / initDiag, 0.1f);

                    nw = (int)(_initialBounds.Width * scale);
                    nh = (int)(_initialBounds.Height * scale);

                    switch (_resizeHandleIndex)
                    {
                        case 0: nx = fixedCorner.X - nw; ny = fixedCorner.Y - nh; break;
                        case 1: nx = fixedCorner.X; ny = fixedCorner.Y - nh; break;
                        case 2: nx = fixedCorner.X - nw; ny = fixedCorner.Y; break;
                        case 3: nx = fixedCorner.X; ny = fixedCorner.Y; break;
                    }
                }

                // Ctrl: Привязка к вершинам
                if ((Control.ModifierKeys & Keys.Control) == Keys.Control)
                {
                    Point draggedCorner = _resizeHandleIndex switch
                    {
                        0 => new Point(nx, ny),
                        1 => new Point(nx + nw, ny),
                        2 => new Point(nx + nw, ny + nh),
                        3 => new Point(nx, ny + nh),
                        _ => Point.Empty
                    };

                    Point snappedCorner = SnapToVertices(draggedCorner, 20f, SelectedShape);
                    int snapDx = snappedCorner.X - draggedCorner.X;
                    int snapDy = snappedCorner.Y - draggedCorner.Y;

                    switch (_resizeHandleIndex)
                    {
                        case 0: nx += snapDx; ny += snapDy; nw -= snapDx; nh -= snapDy; break;
                        case 1: ny += snapDy; nw += snapDx; nh -= snapDy; break;
                        case 2: nw += snapDx; nh += snapDy; break;
                        case 3: nx += snapDx; nw -= snapDx; nh += snapDy; break;
                    }
                }

                SelectedShape.Location = new Point(nx, ny);
                SelectedShape.Size = new Size(nw, nh);
                ShapeChanged?.Invoke();
                Invalidate();
                return;
            }

            // === 4. Перетаскивание фигур ===
            if (!_isDragging || _draggingShape == null) return;

            int dxM = mouseWorldPos.X - _dragStartPos.X;
            int dyM = mouseWorldPos.Y - _dragStartPos.Y;
            bool isCtrl = (Control.ModifierKeys & Keys.Control) == Keys.Control;
            bool isShift = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;

            if (_draggingShape is PolylineShape polyline)
            {
                if (_dragVertexIndex >= 0 && _dragVertexIndex < polyline.Points.Count)
                {
                    int rx = _originalPoints[_dragVertexIndex].X + dxM;
                    int ry = _originalPoints[_dragVertexIndex].Y + dyM;
                    Point t = new Point(rx, ry);

                    if (isCtrl)
                    {
                        if (_snappedToVertex.HasValue)
                        {
                            float distFromSnapped = Distance(t, _snappedToVertex.Value);
                            if (distFromSnapped < 15f) t = _snappedToVertex.Value;
                            else _snappedToVertex = null;
                        }
                        if (!_snappedToVertex.HasValue)
                        {
                            Point beforeSnap = t;
                            t = SnapToVertices(t, 20f, _draggingShape, _dragVertexIndex);
                            if (Distance(t, beforeSnap) > 0.1f) _snappedToVertex = t;
                        }
                    }
                    else if (isShift) { _snappedToVertex = null; t = new Point(Snap(rx), Snap(ry)); }
                    else { _snappedToVertex = null; }
                    polyline.SetPoint(_dragVertexIndex, t);
                }
                else
                {
                    _snappedToVertex = null;
                    var newPoints = new Point[polyline.Points.Count];
                    for (int i = 0; i < polyline.Points.Count; i++)
                    {
                        int rx = _originalPoints[i].X + dxM; int ry = _originalPoints[i].Y + dyM;
                        Point t = new Point(rx, ry);
                        if (isCtrl) t = SnapToVertices(t, 20f, _draggingShape);
                        else if (isShift) t = new Point(Snap(rx), Snap(ry));
                        newPoints[i] = t;
                    }
                    for (int i = 0; i < polyline.Points.Count; i++) polyline.SetPoint(i, newPoints[i]);
                }
            }
            else // Перетаскивание обычных фигур (Квадрат, Круг, Треугольник)
            {
                int rx = _shapeStartPos.X + dxM;
                int ry = _shapeStartPos.Y + dyM;
                Point target = new Point(rx, ry);

                if (isCtrl)
                {
                    //берем смещение, вычисленное в OnMouseDown
                    Point grabbedVertexPos = new Point(rx + _snapOffset.X, ry + _snapOffset.Y);
                    Point snappedVertex = SnapToVertices(grabbedVertexPos, 20f, _draggingShape);

                    // Сдвигаем фигуру так, чтобы захваченная вершина встала в snappedVertex
                    target = new Point(snappedVertex.X - _snapOffset.X, snappedVertex.Y - _snapOffset.Y);
                }
                else if (isShift)
                {
                    target = new Point(Snap(rx), Snap(ry));
                }

                _draggingShape.Location = target;
            }
            ShapeChanged?.Invoke(); Invalidate();
        }

        /// <summary>
        /// Обработчик события отпускания кнопки мыши.
        /// </summary>
        /// <remarks>
        /// Сбрасывает все флаги активного взаимодействия:
        /// - Завершает режим добавления точки в полилинию через FinishAddingPoint()
        /// - Сбрасывает _isDragging, _isResizing, _isRotating, _isPanning
        /// - Очищает индексы и массивы временных данных
        /// 
        /// Важно: не сбрасывает SelectedShape, чтобы выделение сохранялось после операции.
        /// </remarks>
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_isAddingPoint) FinishAddingPoint();
            _isDragging = false; _isResizing = false; _isRotating = false; _isPanning = false;
            _dragVertexIndex = -1; _resizeHandleIndex = -1;
            _originalPoints = Array.Empty<Point>();
            _resizeHandleOffsetX = 0f;
            _resizeHandleOffsetY = 0f;
        }

        /// <summary>
        /// Обработчик события прокрутки колёсика мыши.
        /// </summary>
        /// <remarks>
        /// Реализует масштабирование с привязкой к позиции курсора:
        /// 1. Вычисление коэффициента изменения зума (1.1 для увеличения, 0.9 для уменьшения)
        /// 2. Ограничение нового значения зума в диапазоне [MinZoom, MaxZoom]
        /// 3. Если изменение незначительно (< 0.001), выход без действий
        /// 4. Вычисление мировых координат точки под курсором ДО изменения зума:
        ///    worldX = (screenX - panX) / zoom
        /// 5. Применение нового значения зума
        /// 6. Корректировка смещения панорамирования так, чтобы мировая точка
        ///    под курсором осталась в той же позиции экрана:
        ///    panX = screenX - worldX * newZoom
        /// 7. Инициирование перерисовки
        /// 
        /// Этот алгоритм обеспечивает интуитивное поведение: область под курсором
        /// не "уплывает" при изменении масштаба.
        /// </remarks>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            float zoomFactor = e.Delta > 0 ? 1.1f : 0.9f;
            float newZoom = _zoom * zoomFactor;
            newZoom = Math.Max(MinZoom, Math.Min(MaxZoom, newZoom));
            if (Math.Abs(newZoom - _zoom) < 0.001f) return;

            Point mouseScreenPos = e.Location;
            float worldX = (mouseScreenPos.X - _panX) / _zoom;
            float worldY = (mouseScreenPos.Y - _panY) / _zoom;
            _zoom = newZoom;
            _panX = mouseScreenPos.X - worldX * _zoom;
            _panY = mouseScreenPos.Y - worldY * _zoom;

            Invalidate();
        }

        /// <summary>
        /// Обработчик события нажатия клавиши.
        /// </summary>
        /// <remarks>
        /// Обрабатывает горячие клавиши:
        /// - Клавиша 'E' при выделенной полилинии: инициирует добавление новой вершины
        ///   в позиции текущего курсора (конвертированной в мировые координаты)
        /// 
        /// Другие клавиши обрабатываются базовым классом или вышестоящими обработчиками.
        /// </remarks>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.E && SelectedShape is PolylineShape)
            {
                Point mouseWorldPos = ScreenToWorld(PointToClient(MousePosition));
                StartAddingPoint(mouseWorldPos);
            }
        }

        // =====================================================================
        // Методы отрисовки
        // =====================================================================

        /// <summary>
        /// Переопределённый метод отрисовки контрола.
        /// </summary>
        /// <remarks>
        /// Алгоритм отрисовки:
        /// 1. Вызов базовой реализации OnPaint()
        /// 2. Сохранение состояния Graphics через Save() для последующего восстановления
        /// 3. Применение трансформаций в правильном порядке:
        ///    - TranslateTransform: сдвиг начала координат на (_panX, _panY)
        ///    - ScaleTransform: масштабирование на коэффициент _zoom
        ///    Порядок важен: сначала сдвиг, потом масштаб, чтобы панорамирование
        ///    также масштабировалось корректно
        /// 4. Отрисовка сетки через DrawGrid()
        /// 5. Отрисовка всех фигур через их метод Draw()
        /// 6. Отрисовка элементов выделения выделенной фигуры через DrawSelection()
        /// 7. Восстановление исходного состояния Graphics через Restore()
        /// 
        /// Использование try/finally гарантирует восстановление состояния Graphics
        /// даже при возникновении исключений в процессе отрисовки.
        /// </remarks>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            GraphicsState state = e.Graphics.Save();
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            try
            {
                // Apply scale first, then translate: корректное соответствие "мировых" координат с экраном.
                e.Graphics.ScaleTransform(_zoom, _zoom);
                e.Graphics.TranslateTransform(_panX, _panY);

                // Отрисовка сетки и фигур (каждая фигура рисуется один раз)
                DrawGrid(e.Graphics);
                foreach (var s in _shapes) s.Draw(e.Graphics);

                // Отрисовка выделения отдельной фигуры (если есть)
                if (SelectedShape != null) DrawSelection(e.Graphics);

                // Отрисовка FOV камер (антиалиасинг для аккуратности)
                if (ShowCameraFovs)
                {
                    var old = g.SmoothingMode;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    foreach (var cam in _shapes.OfType<CameraShape>())
                        DrawCameraFovWithOcclusion(g, cam);
                    g.SmoothingMode = old;
                }

                // Рисуем прямоугольную зону выделения, если установлена.
                // _selectionRect предполагается в мировых координатах (координаты до пан/зум).
                if (_selectionRect.HasValue)
                {
                    var sel = _selectionRect.Value;

                    // применяем смещения к начальной и конечной точкам
                    var leftTop = new PointF(sel.Left + _selectionStartOffset.X, sel.Top + _selectionStartOffset.Y);
                    var rightBottom = new PointF(sel.Right + _selectionEndOffset.X, sel.Bottom + _selectionEndOffset.Y);

                    // нормализуем (на случай, если смещения поменяли порядок)
                    float x0 = Math.Min(leftTop.X, rightBottom.X);
                    float y0 = Math.Min(leftTop.Y, rightBottom.Y);
                    float x1 = Math.Max(leftTop.X, rightBottom.X);
                    float y1 = Math.Max(leftTop.Y, rightBottom.Y);

                    var adjRect = new RectangleF(x0, y0, x1 - x0, y1 - y0);

                    using (var brush = new SolidBrush(Color.FromArgb(40, Color.Red)))
                    {
                        g.FillRectangle(brush, adjRect);
                    }

                    // Толщина рамки компенсирует масштаб, чтобы выглядеть одинаково на любом зуме.
                    float penWidth = 2f / Math.Max(0.0001f, _zoom);
                    using (var pen = new Pen(Color.Red, penWidth))
                    {
                        pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
                        g.DrawRectangle(pen, Rectangle.Round(adjRect));
                    }
                }
            }
            finally
            {
                e.Graphics.Restore(state);
            }
        }




        /*    private void DrawCameraFovWithOcclusion(Graphics g, CameraShape cam)
            {
                var cx = cam.Location.X + cam.Size.Width / 2f;
                var cy = cam.Location.Y + cam.Size.Height / 2f;
                float startAngle = cam.Angle - cam.Fov / 2f;
                int rays = Math.Max(4, (int)(RayCount * (cam.Fov / 360f)));
                float step = cam.Fov / Math.Max(1, rays);

                var pts = new List<PointF> { new PointF(cx, cy) };

                for (int i = 0; i <= rays; i++)
                {
                    float ang = startAngle + i * step;
                    float rad = ang * (float)Math.PI / 180f;
                    var dir = new PointF((float)Math.Cos(rad), (float)Math.Sin(rad));
                    // normalize dir
                    var len = (float)Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
                    if (len > 1e-6f) { dir.X /= len; dir.Y /= len; }

                    float hitDist = cam.Radius;
                    foreach (var shape in _shapes)
                    {
                        if (ReferenceEquals(shape, cam)) continue;
                        var d = RayIntersectShapeDistance(new PointF(cx, cy), dir, shape, cam.Radius);
                        if (d >= 0 && d < hitDist) hitDist = d;
                    }

                    pts.Add(new PointF(cx + dir.X * hitDist, cy + dir.Y * hitDist));
                }

                if (pts.Count >= 3)
                {
                    using var path = new GraphicsPath();
                    path.AddPolygon(pts.ToArray());
                    using var brush = new SolidBrush(Color.FromArgb(80, cam.FillColor));
                    g.FillPath(brush, path);
                    using var pen = new Pen(Color.FromArgb(160, cam.FillColor), 2);
                    g.DrawPath(pen, path);
                }
            } */

        /*    private void DrawCameraFovWithOcclusion(Graphics g, CameraShape cam)
            {
                var cx = cam.Location.X + cam.Size.Width / 2f; var cy = cam.Location.Y + cam.Size.Height / 2f; float startAngle = cam.Angle - cam.Fov / 2f; int rays = Math.Max(4, (int)(RayCount * (cam.Fov / 360f))); float step = cam.Fov / Math.Max(1, rays);
                var pts = new List<PointF> { new PointF(cx, cy) };
                for (int i = 0; i <= rays; i++)
                {
                    float ang = startAngle + i * step; float rad = ang * (float)Math.PI / 180f; var dir = new PointF((float)Math.Cos(rad), (float)Math.Sin(rad));
                    var len = (float)Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y); if (len > 1e-6f) { dir.X /= len; dir.Y /= len; }
                    float hitDist = cam.Radius; foreach (var shape in _shapes) { if (ReferenceEquals(shape, cam)) continue; var d = RayIntersectShapeDistance(new PointF(cx, cy), dir, shape, cam.Radius, cam.Height3d, cam.Fov); if (d >= 0 && d < hitDist) hitDist = d; }
                    pts.Add(new PointF(cx + dir.X * hitDist, cy + dir.Y * hitDist));
                }
                if (pts.Count >= 3) { using var path = new GraphicsPath(); path.AddPolygon(pts.ToArray()); using var brush = new SolidBrush(Color.FromArgb(80, cam.FillColor)); g.FillPath(brush, path); using var pen = new Pen(Color.FromArgb(160, cam.FillColor), 2); g.DrawPath(pen, path); }
            } */
        private void DrawCameraFovWithOcclusion(Graphics g, CameraShape cam)
        {
            var cx = cam.Location.X + cam.Size.Width / 2f; var cy = cam.Location.Y + cam.Size.Height / 2f; float startAngle = cam.Angle - cam.Fov / 2f; int rays = Math.Max(4, (int)(RayCount * (cam.Fov / 360f))); float step = cam.Fov / Math.Max(1, rays);
            var pts = new List<PointF> { new PointF(cx, cy) }; for (int i = 0; i <= rays; i++)
            {
                float ang = startAngle + i * step; float rad = ang * (float)Math.PI / 180f; var dir = new PointF((float)Math.Cos(rad), (float)Math.Sin(rad)); float len = (float)Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y); if (len > 1e-6f) { dir.X /= len; dir.Y /= len; }
                float hitDist = cam.Radius; foreach (var shape in _shapes) { if (ReferenceEquals(shape, cam)) continue; var d = RayIntersectShapeDistance(new PointF(cx, cy), dir, shape, cam.Radius, cam.Height3d, cam.Fov); if (d >= 0 && d < hitDist) hitDist = d; }
                pts.Add(new PointF(cx + dir.X * hitDist, cy + dir.Y * hitDist));
            }
            if (pts.Count >= 3) { using var path = new GraphicsPath(); path.AddPolygon(pts.ToArray()); using var brush = new SolidBrush(Color.FromArgb(80, cam.FillColor)); g.FillPath(brush, path); using var pen = new Pen(Color.FromArgb(160, cam.FillColor), 2); g.DrawPath(pen, path); }
        }
        private List<PointF> ComputeCameraFovPolygon(CameraShape cam, int rayCount)
        {
            var cx = cam.Location.X + cam.Size.Width / 2f; var cy = cam.Location.Y + cam.Size.Height / 2f; float startAngle = cam.Angle - cam.Fov / 2f; int rays = Math.Max(4, (int)(rayCount * (cam.Fov / 360f))); float step = cam.Fov / Math.Max(1, rays);
            var pts = new List<PointF> { new PointF(cx, cy) }; for (int i = 0; i <= rays; i++)
            {
                float ang = startAngle + i * step; float rad = ang * (float)Math.PI / 180f; var dir = new PointF((float)Math.Cos(rad), (float)Math.Sin(rad)); float len = (float)Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y); if (len > 1e-6f) { dir.X /= len; dir.Y /= len; }
                float hitDist = cam.Radius; foreach (var shape in _shapes) { if (ReferenceEquals(shape, cam)) continue; var d = RayIntersectShapeDistance(new PointF(cx, cy), dir, shape, cam.Radius, cam.Height3d, cam.Fov); if (d >= 0 && d < hitDist) hitDist = d; }
                pts.Add(new PointF(cx + dir.X * hitDist, cy + dir.Y * hitDist));
            }
            return pts;
        }
        private float PolygonAreaFromCenter(List<PointF> pts) { if (pts == null || pts.Count < 3) return 0f; var center = pts[0]; double area = 0.0; for (int i = 1; i < pts.Count - 1; i++) { var a = new PointF(pts[i].X - center.X, pts[i].Y - center.Y); var b = new PointF(pts[i + 1].X - center.X, pts[i + 1].Y - center.Y); area += Math.Abs(a.X * b.Y - a.Y * b.X) * 0.5; } return (float)area; }
        public float ComputeTotalCoveredArea(int rayCount = 180) { float total = 0f; foreach (var s in _shapes) { if (s is CameraShape cam) { var poly = ComputeCameraFovPolygon(cam, rayCount); total += PolygonAreaFromCenter(poly); } } return total; }
        /* private void DrawCameraFovWithOcclusion(Graphics g, CameraShape cam)
         {
             var cx = cam.Location.X + cam.Size.Width / 2f; var cy = cam.Location.Y + cam.Size.Height / 2f; float startAngle = cam.Angle - cam.Fov / 2f; int rays = Math.Max(4, (int)(RayCount * (cam.Fov / 360f))); float step = cam.Fov / Math.Max(1, rays);
             var pts = new List<PointF> { new PointF(cx, cy) };
             for (int i = 0; i <= rays; i++)
             {
                 float ang = startAngle + i * step; float rad = ang * (float)Math.PI / 180f; var dir = new PointF((float)Math.Cos(rad), (float)Math.Sin(rad));                       var len = (float)Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);            if (len > 1e-6f) { dir.X /= len; dir.Y /= len; }
                 float hitDist = cam.Radius; foreach (var shape in _shapes) { if (ReferenceEquals(shape, cam)) continue; var d = RayIntersectShapeDistance(new PointF(cx, cy), dir, shape, cam.Radius, cam.Height3d, cam.Fov); if (d >= 0 && d < hitDist) hitDist = d; }
                 pts.Add(new PointF(cx + dir.X * hitDist, cy + dir.Y * hitDist));
             }
             if (pts.Count >= 3) { using var path = new GraphicsPath(); path.AddPolygon(pts.ToArray()); using var brush = new SolidBrush(Color.FromArgb(80, cam.FillColor)); g.FillPath(brush, path); using var pen = new Pen(Color.FromArgb(160, cam.FillColor), 2); g.DrawPath(pen, path); }
         }
          */
        // returns distance from origin to intersection point along dir (dir normalized), or -1 if no hit within maxDist
        /*  private float RayIntersectShapeDistance(PointF origin, PointF dir, Shape shape, float maxDist, float camHeight3D, float camFovAngleDeg)
          {         PointF shapeCenter = new PointF(shape.Location.X + shape.Size.Width / 2f, shape.Location.Y + shape.Size.Height / 2f);        float dx = shapeCenter.X - origin.X;        float dy = shapeCenter.Y - origin.Y;        float horizDist = (float)Math.Sqrt(dx * dx + dy * dy);
                 if (!IsWithinVerticalFov(camHeight3D, shape.Height3d, horizDist, camFovAngleDeg))            return -1f;
              if (shape is RectShape) { var rect = new RectangleF(shape.Location, shape.Size); return RayIntersectRect(origin, dir, rect, maxDist); }
              else if (shape is CircleShape) { float cx = shape.Location.X + shape.Size.Width / 2f; float cy = shape.Location.Y + shape.Size.Height / 2f; float rx = shape.Size.Width / 2f; float ry = shape.Size.Height / 2f; return RayIntersectEllipse(origin, dir, new PointF(cx, cy), rx, ry, maxDist); }
              else if (shape is TriangleShape triangle) { return RayIntersectTriangle(origin, dir, triangle, maxDist); }
              else if (shape is PolylineShape polyline)
              {
                  float minT = -1f; for (int i = 0; i < polyline.Points.Count - 1; i++)
                  {
                      PointF a = polyline.Points[i]; PointF b = polyline.Points[i + 1];
                      // Более точная вертикальная проверка для сегмента: используем ближайшую точку сегмента к origin                PointF nearest = NearestPointOnSegment(origin, a, b);                float ddx = nearest.X - origin.X;                float ddy = nearest.Y - origin.Y;                float segHorizDist = (float)Math.Sqrt(ddx * ddx + ddy * ddy);                if (!IsWithinVerticalFov(camHeight3D, polyline.Height3D, segHorizDist, camFovAngleDeg))                    continue;
                      float t = RayIntersectSegment(origin, dir, a, b, polyline.Thickness / 2f, maxDist); if (t >= 0f && (minT == -1f || t < minT)) minT = t;
                  }
                  return minT;
              }
              return -1f;
          } */

        private float RayIntersectShapeDistance(PointF origin, PointF dir, Shape shape, float maxDist, float camHeight3D, float camFovAngleDeg)
        {
            PointF shapeCenter = new PointF(shape.Location.X + shape.Size.Width / 2f, shape.Location.Y + shape.Size.Height / 2f); float dx = shapeCenter.X - origin.X; float dy = shapeCenter.Y - origin.Y; float horizDist = (float)Math.Sqrt(dx * dx + dy * dy);
            if (!IsWithinVerticalFov(camHeight3D, shape.Height3d, horizDist, camFovAngleDeg)) return -1f;
            if (shape is RectShape) { var rect = new RectangleF(shape.Location, shape.Size); return RayIntersectRect(origin, dir, rect, maxDist); }
            else if (shape is CircleShape) { float cx = shape.Location.X + shape.Size.Width / 2f; float cy = shape.Location.Y + shape.Size.Height / 2f; float rx = shape.Size.Width / 2f; float ry = shape.Size.Height / 2f; return RayIntersectEllipse(origin, dir, new PointF(cx, cy), rx, ry, maxDist); }
            else if (shape is TriangleShape triangle) { return RayIntersectTriangle(origin, dir, triangle, maxDist); }
            else if (shape is PolylineShape polyline)
            {
                float minT = -1f; for (int i = 0; i < polyline.Points.Count - 1; i++)
                {
                    PointF a = polyline.Points[i]; PointF b = polyline.Points[i + 1];
                    PointF nearest = NearestPointOnSegment(origin, a, b); float ddx = nearest.X - origin.X; float ddy = nearest.Y - origin.Y; float segHorizDist = (float)Math.Sqrt(ddx * ddx + ddy * ddy); if (!IsWithinVerticalFov(camHeight3D, polyline.Height3d, segHorizDist, camFovAngleDeg)) continue;
                    float t = RayIntersectSegment(origin, dir, a, b, polyline.Thickness / 2f, maxDist); if (t >= 0f && (minT == -1f || t < minT)) minT = t;
                }
                return minT;
            }
            return -1f;
        }
        private PointF NearestPointOnSegment(PointF p, PointF a, PointF b) { var vx = b.X - a.X; var vy = b.Y - a.Y; var wx = p.X - a.X; var wy = p.Y - a.Y; float len2 = vx * vx + vy * vy; if (len2 <= 1e-9f) return a; float t = (wx * vx + wy * vy) / len2; t = Math.Max(0f, Math.Min(1f, t)); return new PointF(a.X + vx * t, a.Y + vy * t); }


        private float RayIntersectTriangle(PointF origin, PointF dir, TriangleShape triangle, float maxDist) { float minT = -1f; var vertices = triangle.GetVertices().ToList(); if (vertices.Count < 3) return -1f; for (int i = 0; i < 3; i++) { PointF a = vertices[i]; PointF b = vertices[(i + 1) % 3]; float t = RayIntersectSegment(origin, dir, a, b, 0f, maxDist); if (t >= 0f && (minT == -1f || t < minT)) minT = t; } return minT; }

        private float RayIntersectRect(PointF origin, PointF dir, RectangleF rect, float maxDist)
        {
            float tmin = 0f, tmax = maxDist;
            if (Math.Abs(dir.X) < 1e-6f) { if (origin.X < rect.Left || origin.X > rect.Right) return -1; } else { float tx1 = (rect.Left - origin.X) / dir.X; float tx2 = (rect.Right - origin.X) / dir.X; if (tx1 > tx2) (tx1, tx2) = (tx2, tx1); tmin = Math.Max(tmin, tx1); tmax = Math.Min(tmax, tx2); if (tmin > tmax) return -1; }
            if (Math.Abs(dir.Y) < 1e-6f) { if (origin.Y < rect.Top || origin.Y > rect.Bottom) return -1; } else { float ty1 = (rect.Top - origin.Y) / dir.Y; float ty2 = (rect.Bottom - origin.Y) / dir.Y; if (ty1 > ty2) (ty1, ty2) = (ty2, ty1); tmin = Math.Max(tmin, ty1); tmax = Math.Min(tmax, ty2); if (tmin > tmax) return -1; }
            if (tmin < 0f) { if (rect.Contains(origin)) return 0f; if (tmax >= 0f) return tmax <= maxDist ? tmax : -1; return -1; }
            return tmin <= maxDist ? tmin : -1;
        }
        /*  private float RayIntersectTriangle(PointF origin, PointF dir, TriangleShape triangle, float maxDist)
          {
              float minT = -1f;

              // Получаем все 3 вершины треугольника
              var vertices = triangle.GetVertices().ToList();
              if (vertices.Count < 3) return -1f;

              // Проверяем пересечение с каждым из 3 рёбер
              for (int i = 0; i < 3; i++)
              {
                  PointF a = vertices[i];
                  PointF b = vertices[(i + 1) % 3]; // Следующая вершина (с циклическим переходом)

                  float t = RayIntersectSegment(origin, dir, a, b, 0f, maxDist); // 0f - без толщины для рёбер

                  if (t >= 0f && (minT == -1f || t < minT))
                  {
                      minT = t;
                  }
              }

              return minT;
          }
          */
        /*   private float RayIntersectRect(PointF origin, PointF dir, RectangleF rect, float maxDist)
           {
               // slab method
               float tmin = 0f, tmax = maxDist;

               if (Math.Abs(dir.X) < 1e-6f)
               {
                   if (origin.X < rect.Left || origin.X > rect.Right) return -1;
               }
               else
               {
                   float tx1 = (rect.Left - origin.X) / dir.X;
                   float tx2 = (rect.Right - origin.X) / dir.X;
                   if (tx1 > tx2) (tx1, tx2) = (tx2, tx1);
                   tmin = Math.Max(tmin, tx1);
                   tmax = Math.Min(tmax, tx2);
                   if (tmin > tmax) return -1;
               }

               if (Math.Abs(dir.Y) < 1e-6f)
               {
                   if (origin.Y < rect.Top || origin.Y > rect.Bottom) return -1;
               }
               else
               {
                   float ty1 = (rect.Top - origin.Y) / dir.Y;
                   float ty2 = (rect.Bottom - origin.Y) / dir.Y;
                   if (ty1 > ty2) (ty1, ty2) = (ty2, ty1);
                   tmin = Math.Max(tmin, ty1);
                   tmax = Math.Min(tmax, ty2);
                   if (tmin > tmax) return -1;
               }

               if (tmin < 0f)
               {
                   if (rect.Contains(origin)) return 0f;
                   if (tmax >= 0f) return tmax <= maxDist ? tmax : -1;
                   return -1;
               }

               return tmin <= maxDist ? tmin : -1;
           }
        */
        /*   private float RayIntersectEllipse(PointF origin, PointF dir, PointF center, float rx, float ry, float maxDist)
           {
               // map to unit circle
               float ox = (origin.X - center.X) / rx;
               float oy = (origin.Y - center.Y) / ry;
               float dx = dir.X / rx;
               float dy = dir.Y / ry;

               float a = dx * dx + dy * dy;
               float b = 2f * (ox * dx + oy * dy);
               float c = ox * ox + oy * oy - 1f;
               if (Math.Abs(a) < 1e-9f) return -1;
               float disc = b * b - 4f * a * c;
               if (disc < 0f) return -1;
               float sqrtD = (float)Math.Sqrt(disc);
               float t1 = (-b - sqrtD) / (2f * a);
               float t2 = (-b + sqrtD) / (2f * a);
               float t = float.MaxValue;
               if (t1 >= 0f) t = Math.Min(t, t1);
               if (t2 >= 0f) t = Math.Min(t, t2);
               if (t == float.MaxValue) return -1;
               // t is in scaled space; compute world hit point and distance along original dir
               var hitX = origin.X + dir.X * t;
               var hitY = origin.Y + dir.Y * t;
               float dist = (float)Math.Sqrt((hitX - origin.X) * (hitX - origin.X) + (hitY - origin.Y) * (hitY - origin.Y));
               return dist <= maxDist ? dist : -1;
           }
           */
        private float RayIntersectEllipse(PointF origin, PointF dir, PointF center, float rx, float ry, float maxDist)
        {
            float ox = (origin.X - center.X) / rx; float oy = (origin.Y - center.Y) / ry; float dx = dir.X / rx; float dy = dir.Y / ry;
            float a = dx * dx + dy * dy; float b = 2f * (ox * dx + oy * dy); float c = ox * ox + oy * oy - 1f; if (Math.Abs(a) < 1e-9f) return -1; float disc = b * b - 4f * a * c; if (disc < 0f) return -1; float sqrtD = (float)Math.Sqrt(disc); float t1 = (-b - sqrtD) / (2f * a); float t2 = (-b + sqrtD) / (2f * a); float t = float.MaxValue; if (t1 >= 0f) t = Math.Min(t, t1); if (t2 >= 0f) t = Math.Min(t, t2); if (t == float.MaxValue) return -1; var hitX = origin.X + dir.X * t; var hitY = origin.Y + dir.Y * t; float dist = (float)Math.Sqrt((hitX - origin.X) * (hitX - origin.X) + (hitY - origin.Y) * (hitY - origin.Y)); return dist <= maxDist ? dist : -1;
        }
        private float RayIntersectSegment(PointF origin, PointF dir, PointF a, PointF b, float thicknessRadius, float maxDist)
        {
            var v = new PointF(b.X - a.X, b.Y - a.Y); float det = dir.X * (-v.Y) - dir.Y * (-v.X); if (Math.Abs(det) > 1e-9f) { float dx = a.X - origin.X; float dy = a.Y - origin.Y; float t = (dx * (-v.Y) - dy * (-v.X)) / det; float s = (dir.X * dy - dir.Y * dx) / det; if (t >= 0f && t <= maxDist && s >= 0f && s <= 1f) { return t; } }
            float da = RayIntersectCircle(origin, dir, a, thicknessRadius, maxDist); float db = RayIntersectCircle(origin, dir, b, thicknessRadius, maxDist); float res = -1f; if (da >= 0f) res = da; if (db >= 0f && (res < 0f || db < res)) res = db; return res;
        }

        private float RayIntersectCircle(PointF origin, PointF dir, PointF center, float radius, float maxDist)
        {
            float ox = origin.X - center.X;
            float oy = origin.Y - center.Y;
            float a = dir.X * dir.X + dir.Y * dir.Y;
            float b = 2f * (ox * dir.X + oy * dir.Y);
            float c = ox * ox + oy * oy - radius * radius;
            float disc = b * b - 4f * a * c;
            if (disc < 0f)
                return -1f;
            float sqrtD = (float)Math.Sqrt(disc);
            float t1 = (-b - sqrtD) / (2f * a);
            float t2 = (-b + sqrtD) / (2f * a);
            float t = float.MaxValue;
            if (t1 >= 0f) t = Math.Min(t, t1);
            if (t2 >= 0f) t = Math.Min(t, t2);
            if (t == float.MaxValue) return -1f;
            return t <= maxDist ? t : -1f;
        }
        /*    private float RayIntersectSegment(PointF origin, PointF dir, PointF a, PointF b, float thicknessRadius, float maxDist)
            {
                // check line intersection with segment
                var v = new PointF(b.X - a.X, b.Y - a.Y);
                // solve origin + t*dir = a + s*v
                float det = dir.X * (-v.Y) - dir.Y * (-v.X);
                if (Math.Abs(det) > 1e-9f)
                {
                    float dx = a.X - origin.X;
                    float dy = a.Y - origin.Y;
                    float t = (dx * (-v.Y) - dy * (-v.X)) / det;
                    float s = (dir.X * dy - dir.Y * dx) / det;
                    if (t >= 0f && t <= maxDist && s >= 0f && s <= 1f)
                    {
                        // intersection point lies on segment
                        return t;
                    }
                }

                // check caps as circles
                float da = RayIntersectCircle(origin, dir, a, thicknessRadius, maxDist);
                float db = RayIntersectCircle(origin, dir, b, thicknessRadius, maxDist);
                float res = -1f;
                if (da >= 0f) res = da;
                if (db >= 0f && (res < 0f || db < res)) res = db;
                return res;
            }

            private float RayIntersectCircle(PointF origin, PointF dir, PointF center, float radius, float maxDist)
            {
                float ox = origin.X - center.X;
                float oy = origin.Y - center.Y;
                float a = dir.X * dir.X + dir.Y * dir.Y;
                float b = 2f * (ox * dir.X + oy * dir.Y);
                float c = ox * ox + oy * oy - radius * radius;
                float disc = b * b - 4f * a * c;
                if (disc < 0f) return -1f;
                float sqrtD = (float)Math.Sqrt(disc);
                float t1 = (-b - sqrtD) / (2f * a);
                float t2 = (-b + sqrtD) / (2f * a);
                float t = float.MaxValue;
                if (t1 >= 0f) t = Math.Min(t, t1);
                if (t2 >= 0f) t = Math.Min(t, t2);
                if (t == float.MaxValue) return -1f;
                return t <= maxDist ? t : -1f;
            } */

        /// <summary>
        /// Отрисовывает элементы выделения для текущей выделенной фигуры.
        /// </summary>
        /// <param name="g">Графический контекст для отрисовки.</param>
        /// <remarks>
        /// Для полилинии:
        /// - Вызов DrawVertices() для отрисовки маркеров вершин
        /// - Подсветка активной вершины жёлтым цветом при добавлении новой точки
        /// 
        /// Для других фигур:
        /// 1. Получение повёрнутых координат углов рамки через GetSelectionCorners()
        /// 2. Отрисовка пунктирной рамки синим цветом
        /// 3. Отрисовка четырёх управляющих ручек (белый круг с синей обводкой)
        /// 4. Вычисление позиции ручки поворота:
        ///    - Определение центра фигуры
        ///    - Вычисление нормали к верхней грани с учётом поворота
        ///    - Смещение на 20 пикселей вверх от верхней грани
        /// 5. Отрисовка линии от верхней грани к ручке поворота
        /// 6. Отрисовка самой ручки поворота (круг с обводкой)
        /// 7. Сохранение позиции ручки в _rotationHandlePos для hit-testing
        /// 
        /// Все координаты передаются в мировых координатах, так как Graphics контекст
        /// уже содержит применённые трансформации (панорамирование и масштаб).
        /// </remarks>
        private bool IsWithinVerticalFov(float camHeight, float shapeHeight, float horizDist, float camFovAngleDeg)
        {
            // если расстояние нулевое — считаем попадает
            if (horizDist <= 1e-6f) return true;
            float heightDiff = shapeHeight - camHeight;
            // максимально допустимая противоположная сторона: tan(fov) * adjacent
            float maxOpposite = (float)Math.Tan(camFovAngleDeg * Math.PI / 180.0) * horizDist;
            return Math.Abs(heightDiff) <= maxOpposite + 1e-6f;
        }

        private void DrawSelection(Graphics g)
        {
            if (SelectedShape == null) return;

            if (SelectedShape is PolylineShape polyline)
            {
                int? highlight = _isAddingPoint ? _dragVertexIndex : (int?)null;
                polyline.DrawVertices(g, highlight);
            }
            else
            {
                var corners = GetSelectionCorners();
                if (corners.Length == 0) return;

                using var pen = new Pen(Color.Blue, 2) { DashStyle = DashStyle.Dot };
                g.DrawPolygon(pen, corners);

                using var brush = new SolidBrush(Color.White);
                using var handlePen = new Pen(Color.Blue, 2);
                foreach (var pt in corners)
                {
                    g.FillEllipse(brush, pt.X - HandleSize / 2, pt.Y - HandleSize / 2, HandleSize, HandleSize);
                    g.DrawEllipse(handlePen, pt.X - HandleSize / 2, pt.Y - HandleSize / 2, HandleSize, HandleSize);
                }

                Rectangle rect = new Rectangle(SelectedShape.Location, SelectedShape.Size);
                PointF center = new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
                float rad = SelectedShape.Angle * (float)Math.PI / 180f;
                float cos = (float)Math.Cos(rad);
                float sin = (float)Math.Sin(rad);
                float localBaseY = -rect.Height / 2f;
                float localHandleY = localBaseY - 20;

                PointF baseScreen = new PointF(center.X - localBaseY * sin, center.Y + localBaseY * cos);
                PointF handleScreen = new PointF(center.X - localHandleY * sin, center.Y + localHandleY * cos);

                _rotationHandlePos = new Point((int)handleScreen.X, (int)handleScreen.Y);

                using var linePen = new Pen(Color.Blue, 2);
                g.DrawLine(linePen, baseScreen, handleScreen);
                g.FillEllipse(Brushes.White, handleScreen.X - 5, handleScreen.Y - 5, 10, 10);
                g.DrawEllipse(Pens.Blue, handleScreen.X - 5, handleScreen.Y - 5, 10, 10);
            }
        }

        /// <summary>
        /// Отрисовывает координатную сетку на холсте.
        /// </summary>
        /// <param name="g">Графический контекст для отрисовки.</param>
        /// <remarks>
        /// Алгоритм оптимизирован для отрисовки только видимой области:
        /// 1. Вычисление границ видимой области в мировых координатах:
        ///    - startX = -panX / zoom (левая граница)
        ///    - endX = startX + ClientSize.Width / zoom (правая граница)
        ///    - Аналогично для Y
        /// 2. Настройка пера с толщиной 1/zoom пикселей для сохранения визуальной
        ///    толщины линии независимо от масштаба
        /// 3. Отрисовка вертикальных линий:
        ///    - Начальная позиция: кратная GridSize, но не меньше startX
        ///    - Шаг: GridSize
        ///    - Конечная позиция: endX
        /// 4. Отрисовка горизонтальных линий аналогично
        /// 
        /// Использование Math.Floor для вычисления стартовой позиции гарантирует,
        /// что сетка остаётся "привязанной" к целым координатам при панорамировании,
        /// что обеспечивает визуальную стабильность.
        /// </remarks>
        private void DrawGrid(Graphics g)
        {
            float startX = -_panX / _zoom;
            float startY = -_panY / _zoom;
            float endX = startX + ClientSize.Width / _zoom;
            float endY = startY + ClientSize.Height / _zoom;

            using var pen = new Pen(Color.LightGray, 1 / _zoom);
            for (float x = (float)Math.Floor(startX / GridSize) * GridSize; x < endX; x += GridSize)
                g.DrawLine(pen, x, startY, x, endY);
            for (float y = (float)Math.Floor(startY / GridSize) * GridSize; y < endY; y += GridSize)
                g.DrawLine(pen, startX, y, endX, y);
        }

        // =====================================================================
        // Система привязки (snapping)
        // =====================================================================

        /// <summary>
        /// Находит ближайшую вершину среди всех фигур для привязки.
        /// </summary>
        /// <param name="p">Точка в мировых координатах, для которой ищется привязка.</param>
        /// <param name="threshold">Максимальное расстояние для срабатывания привязки.</param>
        /// <param name="ignoreShape">Фигура, вершины которой следует игнорировать (обычно перемещаемая).</param>
        /// <param name="ignoreVertexIndex">
        /// Индекс вершины для игнорирования в случае полилинии. Используется для предотвращения
        /// привязки вершины к самой себе при перетаскивании.
        /// </param>
        /// <returns>
        /// Координаты ближайшей вершины, если расстояние меньше порога; иначе исходная точка.
        /// Если расстояние меньше 2 пикселей, возвращается вершина немедленно (защита от дрожания).
        /// </returns>
        /// <remarks>
        /// Алгоритм:
        /// 1. Инициализация nearest = p, minDist = threshold
        /// 2. Перебор всех фигур в _shapes:
        ///    - Пропуск ignoreShape, если это не полилиния (полилинии могут привязываться к своим вершинам)
        /// 3. Перебор вершин текущей фигуры через GetVertices():
        ///    - Для полилинии: дополнительный пропуск вершины с индексом ignoreVertexIndex
        ///    - Если расстояние < 2: немедленный возврат вершины (защита от флуктуаций)
        ///    - Если расстояние < minDist: обновление nearest и minDist
        /// 4. Возврат nearest, если minDist < threshold; иначе возврат исходной точки
        /// 
        /// Оптимизация с порогом 2 пикселя предотвращает "дрожание" при пограничных значениях
        /// расстояния, когда вершина могла бы постоянно переключаться между привязанным
        /// и непривязанным состоянием при малых движениях мыши.
        /// </remarks>
        private Point SnapToVertices(Point p, float threshold = 20f, Shape? ignoreShape = null, int ignoreVertexIndex = -1)
        {
            Point nearest = p; float minDist = threshold;
            foreach (var shape in _shapes)
            {
                if (shape == ignoreShape && shape is not PolylineShape) continue;
                foreach (var vertex in shape.GetVertices())
                {
                    if (shape is PolylineShape polyline && shape == ignoreShape && ignoreVertexIndex >= 0)
                    {
                        var vertices = polyline.Points.ToList();
                        if (ignoreVertexIndex < vertices.Count && vertices[ignoreVertexIndex] == vertex) continue;
                    }
                    float dist = Distance(p, vertex);
                    if (dist < 2f) return vertex;
                    if (dist < minDist) { minDist = dist; nearest = vertex; }
                }
            }
            return minDist < threshold ? nearest : p;
        }

        /// <summary>
        /// Сбрасывает масштаб к 100% и центрирует вид на начале координат.
        /// </summary>
        /// <remarks>
        /// Устанавливает _zoom = 1.0f и инициирует перерисовку.
        /// Смещения _panX и _panY сохраняются, так как метод предназначен только
        /// для сброса масштаба без изменения позиции просмотра.
        /// </remarks>
        public void ResetZoom()
        {
            _zoom = 1.0f;
            Invalidate();
        }

        /// <summary>
        /// Центрирует вид на геометрическом центре всех фигур на холсте.
        /// </summary>
        /// <remarks>
        /// Алгоритм:
        /// 1. Если фигур нет: сброс панорамирования в (0, 0)
        /// 2. Иначе: вычисление ограничивающего прямоугольника всех фигур:
        ///    - minX, minY: минимальные координаты левых/верхних границ
        ///    - maxX, maxY: максимальные координаты правых/нижних границ
        /// 3. Вычисление центра ограничивающего прямоугольника
        /// 4. Корректировка _panX и _panY так, чтобы центр фигур оказался
        ///    в центре клиентской области контрола:
        ///    panX = ClientSize.Width/2 - centerX * zoom
        ///    panY = ClientSize.Height/2 - centerY * zoom
        /// 5. Инициирование перерисовки
        /// 
        /// Текущий масштаб (_zoom) сохраняется, метод изменяет только позицию просмотра.
        /// </remarks>
        public void CenterMap()
        {
            if (_shapes.Count == 0)
            {
                _panX = 0f;
                _panY = 0f;
                Invalidate();
                return;
            }

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var shape in _shapes)
            {
                float x1 = shape.Location.X;
                float y1 = shape.Location.Y;
                float x2 = x1 + shape.Size.Width;
                float y2 = y1 + shape.Size.Height;

                if (x1 < minX) minX = x1;
                if (y1 < minY) minY = y1;
                if (x2 > maxX) maxX = x2;
                if (y2 > maxY) maxY = y2;
            }

            float centerX = (minX + maxX) / 2f;
            float centerY = (minY + maxY) / 2f;

            _panX = ClientSize.Width / 2f - centerX * _zoom;
            _panY = ClientSize.Height / 2f - centerY * _zoom;

            Invalidate();
        }

        // =====================================================================
        // Вспомогательные математические функции
        // =====================================================================

        /// <summary>
        /// Округляет значение до ближайшего кратного GridSize.
        /// </summary>
        /// <param name="val">Значение для округления.</param>
        /// <returns>Значение, округлённое до ближайшей линии сетки.</returns>
        /// <remarks>
        /// Используется для привязки к сетке при перемещении фигур с зажатым Shift.
        /// Формула: round(val / GridSize) * GridSize
        /// </remarks>
        private int Snap(int val) => (int)(Math.Round(val / (double)GridSize) * GridSize);

        /// <summary>
        /// Вычисляет евклидово расстояние между двумя точками.
        /// </summary>
        /// <param name="p1">Первая точка.</param>
        /// <param name="p2">Вторая точка.</param>
        /// <returns>Расстояние в пикселях как значение float.</returns>
        /// <remarks>
        /// Использует формулу: sqrt((x2-x1)² + (y2-y1)²)
        /// Возвращает float для совместимости с пороговыми сравнениями в коде привязки.
        /// </remarks>
        private float Distance(Point p1, Point p2) => (float)Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));



        private class GAIndividual
        {
            public float[] Genes; 
            public float Fitness;       
            public GAIndividual(int geneCount) 
            { Genes = new float[geneCount]; 
                Fitness = float.MinValue;
            }
            public GAIndividual Clone()
            {
                var c = new GAIndividual(Genes.Length);
                Array.Copy(Genes, c.Genes, Genes.Length);
                c.Fitness = Fitness;
                return c;
            }   
        }
        public void RunGeneticOptimize(int generations = 200, int populationSize = 50, float posRange = 50f, float angleRange = 30f, int rayCount = 120, float crossoverRate = 0.7f, float mutationRate = 0.15f, float mutationStdPos = 8f, float mutationStdAngle = 4f, int? randomSeed = null, float overlapPenalty = 500, float unBoundPenalty = 5)
        {
            // Helper: set selection rect on UI thread (expects SetSelectionRect(Rectangle?) to exist)
            void SetSelectionRectSafe(Rectangle? rect)
            {
                try
                {
                    if (this.IsHandleCreated)
                    {
                        if (this.InvokeRequired) this.Invoke(new Action(() => SetSelectionRect(rect)));
                        else SetSelectionRect(rect);
                    }
                }
                catch { /* ignore UI errors */ }
            }

            var rng = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
            var cameras = _shapes.OfType<CameraShape>().ToList();
            if (cameras.Count == 0) return;

            // --- compute bounding square from all shapes and show selection ---
            try
            {
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (var s in _shapes)
                {
                    var loc = s.Location;
                    minX = Math.Min(minX, loc.X);
                    minY = Math.Min(minY, loc.Y);
                    maxX = Math.Max(maxX, loc.X + s.Size.Width);
                    maxY = Math.Max(maxY, loc.Y + s.Size.Height);
                }

                if (minX <= maxX && minY <= maxY)
                {
                    float w = maxX - minX;
                    float h = maxY - minY;
                    float size = Math.Max(w, h);
                    float cx = (minX + maxX) / 2f;
                    float cy = (minY + maxY) / 2f;
                    var left = (int)Math.Round(cx - size / 2f);
                    var top = (int)Math.Round(cy - size / 2f);
                    var rect = new Rectangle(left, top, (int)Math.Round(size), (int)Math.Round(size));
                    SetSelectionRectSafe(rect);
                }
            }
            catch { /* ignore selection errors */ }

            // Save originals for applying genes
            var originals = cameras.Select(c => (Location: c.Location, Angle: c.Angle)).ToList();
            int genePerCam = 3;
            int geneCount = cameras.Count * genePerCam;

            void ApplyGenesToCameras(GAIndividual ind)
            {
                for (int i = 0; i < cameras.Count; i++)
                {
                    int gi = i * genePerCam;
                    var loc = originals[i].Location;
                    int nx = (int)Math.Round(loc.X + ind.Genes[gi + 0]);
                    int ny = (int)Math.Round(loc.Y + ind.Genes[gi + 1]);
                    cameras[i].Location = new Point(nx, ny);
                    cameras[i].Angle = originals[i].Angle + ind.Genes[gi + 2];
                }
            }

            // Initialize population
            var population = new List<GAIndividual>(populationSize);
            for (int p = 0; p < populationSize; p++)
            {
                var ind = new GAIndividual(geneCount);
                for (int i = 0; i < cameras.Count; i++)
                {
                    int gi = i * genePerCam;
                    ind.Genes[gi + 0] = (float)((rng.NextDouble() * 2.0 - 1.0) * posRange);
                    ind.Genes[gi + 1] = (float)((rng.NextDouble() * 2.0 - 1.0) * posRange);
                    ind.Genes[gi + 2] = (float)((rng.NextDouble() * 2.0 - 1.0) * angleRange);
                }
                population.Add(ind);
            }

            // Evaluation function (uses existing helpers: ComputeFovPolygonFromParams, RayIntersectShapeDistance, PolygonAreaFromCenter, PointInPolygon)
            float Evaluate(GAIndividual ind, float ovelapPenaltyMult, float unBoundPenaltyMult)
            {
                var simPolygons = new List<List<PointF>>(cameras.Count);
                var moveBlocked = new bool[cameras.Count];

                for (int camIdx = 0; camIdx < cameras.Count; camIdx++)
                {
                    int gi = camIdx * genePerCam;
                    var orig = originals[camIdx];
                    float nx = orig.Location.X + ind.Genes[gi + 0];
                    float ny = orig.Location.Y + ind.Genes[gi + 1];
                    float nang = orig.Angle + ind.Genes[gi + 2];

                    var center = new PointF(nx + cameras[camIdx].Size.Width / 2f, ny + cameras[camIdx].Size.Height / 2f);

                    var poly = ComputeFovPolygonFromParams(center, nang, cameras[camIdx].Fov, cameras[camIdx].Radius, rayCount, cameras[camIdx].Height3d, cameras[camIdx]);
                    simPolygons.Add(poly);

                    var origCenter = new PointF(orig.Location.X + cameras[camIdx].Size.Width / 2f, orig.Location.Y + cameras[camIdx].Size.Height / 2f);
                    var dx = center.X - origCenter.X;
                    var dy = center.Y - origCenter.Y;
                    var dist = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (dist > 1e-6f)
                    {
                        var dir = new PointF(dx / dist, dy / dist);
                        bool blocked = false;
                        foreach (var shape in _shapes)
                        {
                            if (ReferenceEquals(shape, cameras[camIdx])) continue;
                            var d = RayIntersectShapeDistance(origCenter, dir, shape, dist, cameras[camIdx].Height3d, cameras[camIdx].Fov);
                            if (d >= 0f && d < dist - 1e-3f) { blocked = true; break; }
                        }
                        moveBlocked[camIdx] = blocked;
                    }
                    else moveBlocked[camIdx] = false;
                }

                float totalArea = 0f;
                for (int p = 0; p < simPolygons.Count; p++)
                    totalArea += PolygonAreaFromCenter(simPolygons[p]);

                float overlapPen = 0f;
                for (int a = 0; a < simPolygons.Count; a++)
                {
                    var polyA = simPolygons[a];
                    if (polyA == null || polyA.Count < 3) continue;
                    for (int b = a + 1; b < simPolygons.Count; b++)
                    {
                        var polyB = simPolygons[b];
                        if (polyB == null || polyB.Count < 3) continue;
                        int hits = 0;
                        for (int ka = 1; ka < polyA.Count; ka++) if (PointInPolygon(polyA[ka], polyB)) hits++;
                        for (int kb = 1; kb < polyB.Count; kb++) if (PointInPolygon(polyB[kb], polyA)) hits++;
                        int maxSamples = Math.Max(polyA.Count - 1 + polyB.Count - 1, 1);
                        float frac = (float)hits / maxSamples;
                        overlapPen += frac * frac;
                    }
                }

                float movePenalty = 0f;
                for (int m = 0; m < moveBlocked.Length; m++) if (moveBlocked[m]) movePenalty += 1.0f;

                float weightOverlap = ovelapPenaltyMult * Math.Max(1f, totalArea);
                float weightMove = Math.Max(1f, totalArea) * unBoundPenaltyMult;
                float fitness = totalArea - weightOverlap * overlapPen - weightMove * movePenalty;

                ind.Fitness = fitness;
                return fitness;
            }

            GAIndividual BestOfPopulation() => population.OrderByDescending(i => i.Fitness).First();

            GAIndividual TournamentSelect(int k = 3)
            {
                GAIndividual best = null;
                for (int i = 0; i < k; i++)
                {
                    var cand = population[rng.Next(population.Count)];
                    if (best == null || cand.Fitness > best.Fitness) best = cand;
                }
                return best.Clone();
            }

            (GAIndividual, GAIndividual) Crossover(GAIndividual a, GAIndividual b)
            {
                var ca = a.Clone();
                var cb = b.Clone();
                if (rng.NextDouble() < crossoverRate)
                {
                    int pt = rng.Next(1, geneCount);
                    for (int i = pt; i < geneCount; i++)
                    {
                        float t = ca.Genes[i];
                        ca.Genes[i] = cb.Genes[i];
                        cb.Genes[i] = t;
                    }
                    ca.Fitness = cb.Fitness = float.MinValue;
                }
                return (ca, cb);
            }

            void Mutate(GAIndividual ind)
            {
                for (int i = 0; i < cameras.Count; i++)
                {
                    int gi = i * genePerCam;
                    if (rng.NextDouble() < mutationRate) ind.Genes[gi + 0] += (float)(NextGaussian(rng) * mutationStdPos);
                    if (rng.NextDouble() < mutationRate) ind.Genes[gi + 1] += (float)(NextGaussian(rng) * mutationStdPos);
                    if (rng.NextDouble() < mutationRate) ind.Genes[gi + 2] += (float)(NextGaussian(rng) * mutationStdAngle);
                    ind.Genes[gi + 0] = Math.Max(-posRange, Math.Min(posRange, ind.Genes[gi + 0]));
                    ind.Genes[gi + 1] = Math.Max(-posRange, Math.Min(posRange, ind.Genes[gi + 1]));
                    ind.Genes[gi + 2] = Math.Max(-angleRange, Math.Min(angleRange, ind.Genes[gi + 2]));
                }
                ind.Fitness = float.MinValue;
            }

            // Evaluate initial population
            foreach (var ind in population) Evaluate(ind, overlapPenalty, unBoundPenalty);

            var bestOverall = BestOfPopulation().Clone();

            try
            {
                for (int gen = 0; gen < generations; gen++)
                {
                    var newPop = new List<GAIndividual>(populationSize);
                    var sorted = population.OrderByDescending(i => i.Fitness).ToList();

                    // elitism: keep top-1 and top-2
                    newPop.Add(sorted[0].Clone());
                    if (sorted.Count > 1) newPop.Add(sorted[1].Clone());

                    while (newPop.Count < populationSize)
                    {
                        var parent1 = TournamentSelect();
                        var parent2 = TournamentSelect();
                        var (child1, child2) = Crossover(parent1, parent2);
                        Mutate(child1);
                        Mutate(child2);
                        Evaluate(child1, overlapPenalty, unBoundPenalty);
                        if (newPop.Count < populationSize) newPop.Add(child1);
                        if (newPop.Count < populationSize)
                        {
                            Evaluate(child2, overlapPenalty, unBoundPenalty);
                            newPop.Add(child2);
                        }
                    }

                    population = newPop;
                    var localBest = BestOfPopulation();
                    if (localBest.Fitness > bestOverall.Fitness) bestOverall = localBest.Clone();

                    mutationStdPos *= 0.9995f;
                    mutationStdAngle *= 0.9995f;
                }

                // apply best solution
                ApplyGenesToCameras(bestOverall);
                foreach (var cam in cameras) cam.OnUpdated?.Invoke();
            }
            finally
            {
                // always clear selection rectangle when finished (or on exception)
                SetSelectionRectSafe(null);
            }
        }

        /*   public void RunGeneticOptimize(int generations = 200, int populationSize = 50, float posRange = 50f, float angleRange = 30f, int rayCount = 120, float crossoverRate = 0.7f, float mutationRate = 0.15f, float mutationStdPos = 8f, float mutationStdAngle = 4f, int? randomSeed = null, float overlapPenalty = 500, float unBoundPenalty = 5)
           {
               var rng = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random(); var cameras = _shapes.OfType<CameraShape>().ToList(); if (cameras.Count == 0) return;




               var originals = cameras.Select(c => (Location: c.Location, Angle: c.Angle)).ToList(); int genePerCam = 3; int geneCount = cameras.Count * genePerCam;
               void ApplyGenesToCameras(GAIndividual ind) { for (int i = 0; i < cameras.Count; i++) { int gi = i * genePerCam; var loc = originals[i].Location; int nx = (int)Math.Round(loc.X + ind.Genes[gi + 0]); int ny = (int)Math.Round(loc.Y + ind.Genes[gi + 1]); cameras[i].Location = new Point(nx, ny); cameras[i].Angle = originals[i].Angle + ind.Genes[gi + 2]; } }
               var population = new List<GAIndividual>(populationSize); for (int p = 0; p < populationSize; p++) { var ind = new GAIndividual(geneCount); for (int i = 0; i < cameras.Count; i++) { int gi = i * genePerCam; ind.Genes[gi + 0] = (float)((rng.NextDouble() * 2.0 - 1.0) * posRange); ind.Genes[gi + 1] = (float)((rng.NextDouble() * 2.0 - 1.0) * posRange); ind.Genes[gi + 2] = (float)((rng.NextDouble() * 2.0 - 1.0) * angleRange); } population.Add(ind); }
              // float Evaluate(GAIndividual ind) { ApplyGenesToCameras(ind); float score = ComputeTotalCoveredArea(rayCount); ind.Fitness = score; return score; }
               foreach (var ind in population) Evaluate(ind, overlapPenalty, unBoundPenalty);
               GAIndividual BestOfPopulation() => population.OrderByDescending(i => i.Fitness).First();
               GAIndividual TournamentSelect(int k = 3) { GAIndividual best = null; for (int i = 0; i < k; i++) { var cand = population[rng.Next(population.Count)]; if (best == null || cand.Fitness > best.Fitness) best = cand; } return best.Clone(); }
               (GAIndividual, GAIndividual) Crossover(GAIndividual a, GAIndividual b) { var ca = a.Clone(); var cb = b.Clone(); if (rng.NextDouble() < crossoverRate) { int pt = rng.Next(1, geneCount); for (int i = pt; i < geneCount; i++) { float t = ca.Genes[i]; ca.Genes[i] = cb.Genes[i]; cb.Genes[i] = t; } ca.Fitness = cb.Fitness = float.MinValue; } return (ca, cb); }
               void Mutate(GAIndividual ind) { for (int i = 0; i < cameras.Count; i++) { int gi = i * genePerCam; if (rng.NextDouble() < mutationRate) ind.Genes[gi + 0] += (float)(NextGaussian(rng) * mutationStdPos); if (rng.NextDouble() < mutationRate) ind.Genes[gi + 1] += (float)(NextGaussian(rng) * mutationStdPos); if (rng.NextDouble() < mutationRate) ind.Genes[gi + 2] += (float)(NextGaussian(rng) * mutationStdAngle); ind.Genes[gi + 0] = Math.Max(-posRange, Math.Min(posRange, ind.Genes[gi + 0])); ind.Genes[gi + 1] = Math.Max(-posRange, Math.Min(posRange, ind.Genes[gi + 1])); ind.Genes[gi + 2] = Math.Max(-angleRange, Math.Min(angleRange, ind.Genes[gi + 2])); } ind.Fitness = float.MinValue; }
               var bestOverall = BestOfPopulation().Clone(); for (int gen = 0; gen < generations; gen++)
               {
                   var newPop = new List<GAIndividual>(populationSize); var sorted = population.OrderByDescending(i => i.Fitness).ToList();            // elitism: keep top-1 and top-2 (if exist)            newPop.Add(sorted[0].Clone());            if (sorted.Count > 1) newPop.Add(sorted[1].Clone());
                   while (newPop.Count < populationSize) { var parent1 = TournamentSelect(); var parent2 = TournamentSelect(); var (child1, child2) = Crossover(parent1, parent2); Mutate(child1); Mutate(child2); Evaluate(child1, overlapPenalty, unBoundPenalty); if (newPop.Count < populationSize) newPop.Add(child1); if (newPop.Count < populationSize) { Evaluate(child2, overlapPenalty, unBoundPenalty); newPop.Add(child2); } }
                   population = newPop; var localBest = BestOfPopulation(); if (localBest.Fitness > bestOverall.Fitness) bestOverall = localBest.Clone(); mutationStdPos *= 0.9995f; mutationStdAngle *= 0.9995f;
               }
               ApplyGenesToCameras(bestOverall); foreach (var cam in cameras) cam.OnUpdated?.Invoke();

               float Evaluate(GAIndividual ind, float ovelapPenaltyMult, float unBoundPenaltyMult)
               {
                   var simCenters = new List<PointF>(cameras.Count);
                   var simPolygons = new List<List<PointF>>(cameras.Count);
                   var moveBlocked = new bool[cameras.Count];

                   for (int camIdx = 0; camIdx < cameras.Count; camIdx++)
                   {
                       int gi = camIdx * genePerCam;
                       var orig = originals[camIdx];
                       float nx = orig.Location.X + ind.Genes[gi + 0];
                       float ny = orig.Location.Y + ind.Genes[gi + 1];
                       float nang = orig.Angle + ind.Genes[gi + 2];

                       var center = new PointF(nx + cameras[camIdx].Size.Width / 2f, ny + cameras[camIdx].Size.Height / 2f);
                       simCenters.Add(center);

                       var poly = ComputeFovPolygonFromParams(center, nang, cameras[camIdx].Fov, cameras[camIdx].Radius, rayCount, cameras[camIdx].Height3d, cameras[camIdx]);
                       simPolygons.Add(poly);

                       var origCenter = new PointF(orig.Location.X + cameras[camIdx].Size.Width / 2f, orig.Location.Y + cameras[camIdx].Size.Height / 2f);
                       var dx = center.X - origCenter.X;
                       var dy = center.Y - origCenter.Y;
                       var dist = (float)Math.Sqrt(dx * dx + dy * dy);
                       if (dist > 1e-6f)
                       {
                           var dir = new PointF(dx / dist, dy / dist);
                           bool blocked = false;
                           foreach (var shape in _shapes)
                           {
                               if (ReferenceEquals(shape, cameras[camIdx])) continue;
                               var d = RayIntersectShapeDistance(origCenter, dir, shape, dist, cameras[camIdx].Height3d, cameras[camIdx].Fov);
                               if (d >= 0f && d < dist - 1e-3f) { blocked = true; break; }
                           }
                           moveBlocked[camIdx] = blocked;
                       }
                       else moveBlocked[camIdx] = false;
                   }

                   float totalArea = 0f;
                   for (int p = 0; p < simPolygons.Count; p++) totalArea += PolygonAreaFromCenter(simPolygons[p]);

                   float overlapPenalty = 0f;
                   for (int a = 0; a < simPolygons.Count; a++)
                   {
                       var polyA = simPolygons[a];
                       if (polyA == null || polyA.Count < 3) continue;
                       for (int b = a + 1; b < simPolygons.Count; b++)
                       {
                           var polyB = simPolygons[b];
                           if (polyB == null || polyB.Count < 3) continue;
                           int hits = 0;
                           for (int ka = 1; ka < polyA.Count; ka++) if (PointInPolygon(polyA[ka], polyB)) hits++;
                           for (int kb = 1; kb < polyB.Count; kb++) if (PointInPolygon(polyB[kb], polyA)) hits++;
                           int maxSamples = Math.Max(polyA.Count - 1 + polyB.Count - 1, 1);
                           float frac = (float)hits / maxSamples;
                           overlapPenalty += frac * frac;
                       }
                   }

                   float movePenalty = 0f;
                   for (int m = 0; m < moveBlocked.Length; m++) if (moveBlocked[m]) movePenalty += 1.0f;

                   float weightOverlap = ovelapPenaltyMult * Math.Max(1f, totalArea);
                   float weightMove = Math.Max(1f, totalArea) * unBoundPenaltyMult;
                   float fitness = totalArea - weightOverlap * overlapPenalty - weightMove * movePenalty;

                   ind.Fitness = fitness;
                   return fitness;
               }


           } */

        // Box-Muller    private static double NextGaussian(Random rng)    {        double u1 = 1.0 - rng.NextDouble();        double u2 = 1.0 - rng.NextDouble();        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);    }
        private static double NextGaussian(Random rng) { double u1 = 1.0 - rng.NextDouble(); double u2 = 1.0 - rng.NextDouble(); return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2); }
     //   public void AddShape(Shape s) { _shapes.Add(s); s.OnUpdated = () => { /* invalidate canvas */ }; }
        public void ClearShapes() { _shapes.Clear(); }
        private List<PointF> ComputeFovPolygonFromParams(PointF center, float angleDeg, float fovDeg, int radius, int rayCount, float camHeight3D, CameraShape camToIgnore) { float startAngle = angleDeg - fovDeg / 2f; int rays = Math.Max(4, (int)(rayCount * (fovDeg / 360f))); float step = fovDeg / Math.Max(1, rays); var pts = new List<PointF> { center }; for (int i = 0; i <= rays; i++) { float ang = startAngle + i * step; float rad = ang * (float)Math.PI / 180f; var dir = new PointF((float)Math.Cos(rad), (float)Math.Sin(rad)); float len = (float)Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y); if (len > 1e-6f) { dir.X /= len; dir.Y /= len; } float hitDist = radius; foreach (var shape in _shapes) { if (ReferenceEquals(shape, camToIgnore)) continue; var d = RayIntersectShapeDistance(center, dir, shape, radius, camHeight3D, fovDeg); if (d >= 0 && d < hitDist) hitDist = d; } pts.Add(new PointF(center.X + dir.X * hitDist, center.Y + dir.Y * hitDist)); } return pts; }
        private bool PointInPolygon(PointF p, List<PointF> poly) { bool inside = false; for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++) { var pi = poly[i]; var pj = poly[j]; bool intersect = ((pi.Y > p.Y) != (pj.Y > p.Y)) && (p.X < (pj.X - pi.X) * (p.Y - pi.Y) / ((pj.Y - pi.Y) + float.Epsilon) + pi.X); if (intersect) inside = !inside; } return inside; }

    }

    // =====================================================================
    // Вспомогательные классы
    // =====================================================================

    /// <summary>
    /// Конвертер для сериализации и десериализации структур Color в JSON.
    /// </summary>
    /// <remarks>
    /// System.Drawing.Color не поддерживается JsonSerializer по умолчанию.
    /// Этот конвертер использует HTML-представление цвета (#RRGGBB или имя)
    /// через методы ColorTranslator.ToHtml() и FromHtml().
    /// 
    /// Преимущества подхода:
    /// - Читаемый формат в JSON (например, "LightBlue" вместо числового ARGB)
    /// - Совместимость с другими системами, понимающими HTML-цвета
    /// - Автоматическая обработка именованных цветов и шестнадцатеричных кодов
    /// 
    /// При десериализации невалидной строки возвращается Color.Black как запасной вариант.
    /// </remarks>
    public class ColorJsonConverter : JsonConverter<Color>
    {
        /// <summary>
        /// Читает значение Color из JSON-токена.
        /// </summary>
        /// <param name="reader">Читатель JSON-данных.</param>
        /// <param name="typeToConvert">Тип для преобразования (Color).</param>
        /// <param name="options">Опции сериализации.</param>
        /// <returns>Десериализованное значение цвета.</returns>
        public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return ColorTranslator.FromHtml(reader.GetString() ?? "Black");
        }

        /// <summary>
        /// Записывает значение Color в JSON.
        /// </summary>
        /// <param name="writer">Писатель JSON-данных.</param>
        /// <param name="value">Значение цвета для сериализации.</param>
        /// <param name="options">Опции сериализации.</param>
        public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(ColorTranslator.ToHtml(value));
        }
    }






}