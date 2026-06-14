using Microsoft.VisualBasic;
using System;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms;

namespace CameraTracker
{
    /// <summary>
    /// Главное окно приложения редактора.
    /// </summary>
    /// <remarks>
    /// Класс отвечает за инициализацию пользовательского интерфейса, создание
    /// и компоновку основных компонентов (холст, панель свойств, меню),
    /// а также обработку глобальных событий клавиатуры.
    /// 
    /// Архитектурные особенности:
    /// - Все компоненты создаются программно в конструкторе
    /// - Используется паттерн "Композиция": Form1 содержит экземпляры GridCanvas и PropertyGrid
    /// - События компонентов связаны через лямбда-выражения для минимизации кода
    /// - Поддержка локализации реализована через атрибуты DisplayName/Category в моделях
    /// </remarks>
    public partial class Form1 : Form
    {
        /// <summary>
        /// Основной холст для отрисовки и редактирования графических примитивов.
        /// </summary>
        /// <remarks>
        /// Экземпляр GridCanvas занимает всё доступное пространство формы
        /// (DockStyle.Fill) и является центральным элементом пользовательского интерфейса.
        /// </remarks>
        private readonly GridCanvas _canvas;

        /// <summary>
        /// Панель свойств для редактирования параметров выделенной фигуры.
        /// </summary>
        /// <remarks>
        /// PropertyGrid автоматически генерирует интерфейс на основе публичных
        /// свойств выделенного объекта с учётом атрибутов Category, DisplayName,
        /// Description и Browsable. Привязка к выделению холста осуществляется
        /// через событие SelectionChanged.
        /// </remarks>
        private readonly PropertyGrid _propertyGrid;

        /// <summary>
        /// Инициализирует новый экземпляр главного окна приложения.
        /// </summary>
        /// <remarks>
        /// Последовательность инициализации:
        /// 1. Вызов InitializeComponent() для базовой настройки формы
        /// 2. Включение предварительного просмотра клавиш (KeyPreview) для обработки
        ///    глобальных горячих клавиш независимо от фокуса ввода
        /// 3. Подписка на событие KeyDown для обработки клавиши Delete
        /// 4. Создание и настройка основных компонентов интерфейса:
        ///    - GridCanvas: холст с привязкой к форме через DockStyle.Fill
        ///    - PropertyGrid: панель свойств с фиксированной шириной 250px справа
        /// 5. Настройка двусторонней синхронизации между компонентами:
        ///    - SelectionChanged: при выделении фигуры на холсте обновляется
        ///      SelectedObject в PropertyGrid
        ///    - ShapeChanged: при изменении фигуры обновляется PropertyGrid
        ///    - PropertyValueChanged: при изменении свойства в гриде инициируется
        ///      перерисовка холста и обновление грида
        /// 6. Создание иерархии меню с группировкой по функциональности
        /// 7. Добавление контролов на форму в порядке, определяющем Z-order
        /// 8. Настройка заголовка, размера и позиции окна
        /// 
        /// Важно: порядок добавления контролов через Controls.Add() влияет на
        /// их отображение при использовании Dock-стилей. Контролы, добавленные
        /// позже, могут перекрывать ранее добавленные при конфликтах компоновки.
        /// </remarks>
        public Form1()
        {
            InitializeComponent();
            KeyPreview = true;
            KeyDown += Form1_KeyDown;

            _canvas = new GridCanvas { Dock = DockStyle.Fill };
            _propertyGrid = new PropertyGrid { Dock = DockStyle.Right, Width = 250 };

            // =================================================================
            // Настройка событий синхронизации между компонентами
            // =================================================================

            // При изменении выделенной фигуры на холсте обновляем объект в PropertyGrid.
            // Это обеспечивает отображение свойств текущей фигуры для редактирования.
            _canvas.SelectionChanged += shape => _propertyGrid.SelectedObject = shape;

            // При изменении свойств фигуры (перемещение, размер, поворот) обновляем
            // PropertyGrid для отражения актуальных значений в интерфейсе.
            _canvas.ShapeChanged += () => _propertyGrid.Refresh();

            // При изменении свойства через PropertyGrid:
            // 1. Invalidate() холста инициирует перерисовку для визуального обновления
            // 2. Refresh() грида обновляет отображение значений (необходимо для свойств,
            //    которые могут изменяться косвенно, например, при привязке к сетке)
            _propertyGrid.PropertyValueChanged += (s, e) =>
            {
                _canvas.Invalidate();
                _propertyGrid.Refresh();
            };

            // =================================================================
            // Создание и настройка главного меню приложения
            // =================================================================

            var menuStrip = new MenuStrip { Dock = DockStyle.Top };

            // -----------------------------------------------------------------
            // Меню "Файл": операции сохранения и загрузки проекта
            // -----------------------------------------------------------------
            var fileMenu = new ToolStripMenuItem("Файл");

            var btnSave = new ToolStripMenuItem("Сохранить");
            btnSave.Click += (s, e) =>
            {
                // Стандартный диалог сохранения файла с фильтром по расширению .json
                using var dlg = new SaveFileDialog
                {
                    Filter = "JSON файлы|*.json",
                    Title = "Сохранить проект"
                };
                // Сохранение выполняется только при подтверждении пользователем
                if (dlg.ShowDialog() == DialogResult.OK)
                    _canvas.SaveToFile(dlg.FileName);
            };

            var btnLoad = new ToolStripMenuItem("Загрузить");
            btnLoad.Click += (s, e) =>
            {
                // Стандартный диалог открытия файла с фильтром по расширению .json
                using var dlg = new OpenFileDialog
                {
                    Filter = "JSON файлы|*.json",
                    Title = "Загрузить проект"
                };
                // Загрузка выполняется только при выборе файла пользователем
                if (dlg.ShowDialog() == DialogResult.OK)
                    _canvas.LoadFromFile(dlg.FileName);
            };

            fileMenu.DropDownItems.AddRange(new ToolStripItem[] { btnSave, btnLoad });
            menuStrip.Items.Add(fileMenu);

            // -----------------------------------------------------------------
            // Меню "Добавление": создание новых графических примитивов
            // -----------------------------------------------------------------
            var addMenu = new ToolStripMenuItem("Добавление");

            // Каждая фигура создаётся с параметрами по умолчанию:
            // - Расположение: (100, 100) в мировых координатах
            // - Размер: 60x60 пикселей (для полилинии — отрезок 150 пикселей)
            // После добавления фигура автоматически выделяется и отображается в PropertyGrid

            addMenu.DropDownItems.Add("Квадрат", null, (s, e) =>
                _canvas.AddShape(new RectShape { Location = new Point(100, 100), Size = new Size(60, 60) }));

            addMenu.DropDownItems.Add("Треугольник", null, (s, e) =>
                _canvas.AddShape(new TriangleShape { Location = new Point(100, 100), Size = new Size(60, 60) }));

            addMenu.DropDownItems.Add("Круг", null, (s, e) =>
                _canvas.AddShape(new CircleShape { Location = new Point(100, 100), Size = new Size(60, 60) }));

            addMenu.DropDownItems.Add("Линия", null, (s, e) =>
                _canvas.AddShape(new PolylineShape(new Point(50, 50), new Point(200, 50)) { Thickness = 2f }));

            addMenu.DropDownItems.Add("Камера", null, (s, e) =>
                _canvas.AddShape(new CameraShape
                {
                    Location = new Point(200, 150),
                    Size = new Size(28, 28),
                    //размер маркера (визуально)
                    FillColor = Color.OrangeRed,
                    Angle = 0f,
                    Radius = 200,
                    Fov = 60f
                }));

            addMenu.DropDownItems.Add("Отрисовать поля зрения камер", null, (s, e) =>
                {
                    _canvas.ShowCameraFovs = true;
                    _canvas.Invalidate();
                });

            menuStrip.Items.Add(addMenu);

            // -----------------------------------------------------------------
            // Меню "Слои": управление порядком отрисовки фигур (Z-order)
            // -----------------------------------------------------------------
            var layersMenu = new ToolStripMenuItem("Слои");

            // Перемещение выделенной фигуры на передний план (отрисовка поверх остальных)
            layersMenu.DropDownItems.Add("На передний план", null,
                (s, e) => _canvas.BringToFront(_canvas.SelectedShape));

            // Перемещение выделенной фигуры на задний план (отрисовка под остальными)
            layersMenu.DropDownItems.Add("На задний план", null,
                (s, e) => _canvas.SendToBack(_canvas.SelectedShape));

            menuStrip.Items.Add(layersMenu);

            // -----------------------------------------------------------------
            // Меню "Камера": управление видом и навигацией по холсту
            // -----------------------------------------------------------------
            var cameraMenu = new ToolStripMenuItem("Камера");

            // Сброс масштаба к 100% (без изменения позиции панорамирования)
            var btnResetZoom = new ToolStripMenuItem("Вернуть исходный зум");
            btnResetZoom.Click += (s, e) => _canvas.ResetZoom();

            // Центрирование вида на геометрическом центре всех фигур на холсте
            var btnCenterMap = new ToolStripMenuItem("Вернуться к центру карты");
            btnCenterMap.Click += (s, e) => _canvas.CenterMap();

            var btnCameraOptimize = new ToolStripMenuItem("Оптимизация покрытия камерами");
            //   btnCameraOptimize.Click += (s, e) => _canvas.RunGeneticOptimize(posRange:125, angleRange:180, rayCount:360);
            btnCameraOptimize.Click += async (s, e) => {
            try
            {      
                    string sG = Interaction.InputBox("Поколений:", "Оптимизация", "200");      
                    if (string.IsNullOrWhiteSpace(sG)) { MessageBox.Show("Отменено."); return; }  
                    string sP = Interaction.InputBox("Размер популяции:", "Оптимизация", "50");      
                    if (string.IsNullOrWhiteSpace(sP)) { MessageBox.Show("Отменено."); return; }       
                    string sPos = Interaction.InputBox("Диапазон смещения (px):", "Оптимизация", "125");       
                    if (string.IsNullOrWhiteSpace(sPos)) { MessageBox.Show("Отменено."); return; }      
                    string sAng = Interaction.InputBox("Диапазон угла (deg):", "Оптимизация", "180");      
                    if (string.IsNullOrWhiteSpace(sAng)) { MessageBox.Show("Отменено."); return; }     
                    string sRays = Interaction.InputBox("Количество лучей:", "Оптимизация", "360");
                    if (string.IsNullOrWhiteSpace(sRays)) { MessageBox.Show("Отменено."); return; }
                    string sOvelapPenalty = Interaction.InputBox("штраф за пересечение полей зрения камер:", "Оптимизация", "500");
                    if (string.IsNullOrWhiteSpace(sAng)) { MessageBox.Show("Отменено."); return; }
                    string sUnBoundPenalty = Interaction.InputBox("штраф за пересечение камерами границ помещения:", "Оптимизация", "5");
                    if (string.IsNullOrWhiteSpace(sAng)) { MessageBox.Show("Отменено."); return; }

                    if (!int.TryParse(sG, out int generations)) { MessageBox.Show("Неверное число поколений."); return; }
                if (!int.TryParse(sP, out int population)) { MessageBox.Show("Неверный размер популяции."); return; }
                if (!float.TryParse(sPos, out float posRange)) { MessageBox.Show("Неверный диапазон смещения."); return; }
                if (!float.TryParse(sAng, out float angleRange)) { MessageBox.Show("Неверный диапазон угла."); return; }
                if (!int.TryParse(sRays, out int rayCount)) { MessageBox.Show("Неверное количество лучей."); return; }
                    if (!int.TryParse(sOvelapPenalty, out int parsedOvelapPenalty)) { MessageBox.Show("Неверное значение штрафа за пересечение полей зрения."); return; }
                    if (!int.TryParse(sUnBoundPenalty, out int parsedUnBoundPenalty)) { MessageBox.Show("Неверное значение штрафа за пересечение границ помещения."); return; }
                    if (_canvas == null) { MessageBox.Show("Canvas не задан."); return; }
                      bool hasCams = false;      
                    _canvas.Invoke(() => hasCams = _canvas.Controls.OfType<Control>().Any() ? false : true); 
                   
                    MessageBox.Show("Оптимизация запущена в фоне. Подождите.", "Оптимизация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Exception? runEx = null;        await Task.Run(() =>        {            try            {                _canvas.RunGeneticOptimize(                    generations: generations,                    populationSize: population,                    posRange: posRange,                    angleRange: angleRange,                    rayCount: rayCount, overlapPenalty: parsedOvelapPenalty, unBoundPenalty: parsedUnBoundPenalty);            }            catch (Exception ex)            {                runEx = ex;            }        });
                if (runEx != null) { MessageBox.Show($"Ошибка во время оптимизации:\n{runEx.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
               if (!_canvas.IsDisposed)     
                    {            _canvas.Invoke(() =>            
                    {                _canvas.Invalidate();      
                        MessageBox.Show("Оптимизация завершена.", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);   
                    });        }    }    catch (Exception ex)    {      
                    MessageBox.Show($"Внутренняя ошибка: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);  
                }};


                cameraMenu.DropDownItems.AddRange(new ToolStripItem[] { btnResetZoom, btnCenterMap, btnCameraOptimize });
            menuStrip.Items.Add(cameraMenu);

            // -----------------------------------------------------------------
            // Пункт "О программе": отображение справочной информации
            // -----------------------------------------------------------------
            var aboutItem = new ToolStripMenuItem("О программе");
            aboutItem.Click += (s, e) => ShowAboutDialog();
            menuStrip.Items.Add(aboutItem);

            // =================================================================
            // Финальная компоновка элементов интерфейса
            // =================================================================

            // Порядок добавления контролов важен при использовании Dock-стилей:
            // 1. _canvas (Fill) занимает всё доступное пространство
            // 2. _propertyGrid (Right) "откусывает" 250px справа от оставшейся области
            // 3. menuStrip (Top) размещается в верхней части формы поверх остальных
            Controls.Add(_canvas);
            Controls.Add(_propertyGrid);
            Controls.Add(menuStrip);

            // Настройка свойств окна приложения
            Text = "Редактор интерьера";
            Size = new Size(1000, 700);
            StartPosition = FormStartPosition.CenterScreen;
        }

        /// <summary>
        /// Обработчик глобальных нажатий клавиш на форме.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Параметры события клавиатуры.</param>
        /// <remarks>
        /// Обрабатывает клавишу Delete для удаления выделенной фигуры.
        /// Свойство SuppressKeyPress = true предотвращает дальнейшую обработку
        /// нажатия (например, системный звуковой сигнал), что обеспечивает
        /// чистое поведение интерфейса.
        /// 
        /// Благодаря KeyPreview = true, обработчик получает события клавиатуры
        /// даже когда фокус ввода находится на других контролах (PropertyGrid,
        /// меню, диалоговые окна).
        /// </remarks>
        private void Form1_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                _canvas.DeleteSelected();
                e.SuppressKeyPress = true;
            }
        }

        /// <summary>
        /// Отображает модальное диалоговое окно со справочной информацией.
        /// </summary>
        /// <remarks>
        /// Метод создаёт временную форму с текстовым описанием возможностей
        /// редактора. Окно настраивается как модальный диалог с фиксированным
        /// размером, центрированный относительно родительского окна.
        /// 
        /// TextBox конфигурируется для отображения многострочного текста:
        /// - ReadOnly = true: запрет редактирования пользователем
        /// - WordWrap = true: автоматический перенос строк по ширине
        /// - ScrollBars.Vertical: вертикальная прокрутка при нехватке места
        /// - AcceptsReturn = true: корректная обработка символов новой строки
        /// - BorderStyle.None + Margin: визуальное отделение текста от краёв
        /// 
        /// Текст справки структурирован с использованием символов "->" и "-->"
        /// для визуальной иерархии разделов и пунктов.
        /// 
        /// После закрытия диалога форма автоматически уничтожается благодаря
        /// использованию using-директивы.
        /// </remarks>
        private void ShowAboutDialog()
        {
            using var aboutForm = new Form
            {
                Text = "О программе",
                Size = new Size(450, 400),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.White
            };

            var textBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                WordWrap = true,
                Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Segoe UI", 9.5f),
                BackColor = Color.White,
                BorderStyle = BorderStyle.None,
                Margin = new Padding(20),
                AcceptsReturn = true,
                Text = "СПРАВКА ПО РЕДАКТОРУ" + Environment.NewLine + Environment.NewLine +
                       "-> Управление фигурами:" + Environment.NewLine +
                       "--> ЛКМ + перетаскивание - перемещение" + Environment.NewLine +
                       "--> Shift + перетаскивание - привязка к сетке" + Environment.NewLine +
                       "--> Ctrl + перетаскивание - магнитная привязка к вершинам" + Environment.NewLine +
                       "--> ЛКМ по угловым кружкам - изменение размера (Shift для пропорций)" + Environment.NewLine +
                       "--> ЛКМ по ручке сверху - поворот (Shift для шага 45°)" + Environment.NewLine +
                       "--> Delete - удаление выделенной фигуры" + Environment.NewLine + Environment.NewLine +
                       "-> Работа с линиями:" + Environment.NewLine +
                       "--> ЛКМ по линии + клавиша E - добавить новую вершину" + Environment.NewLine +
                       "--> ЛКМ по вершине + перетаскивание - изменение формы" + Environment.NewLine +
                       "-> Слои:" + Environment.NewLine +
                       "--> Используйте меню 'Слои', чтобы менять порядок отрисовки." + Environment.NewLine +
                       "--> Рекомендуется добавлять стены первыми, а мебель поверх." + Environment.NewLine +
                       "-> Камера:" + Environment.NewLine +
                       "--> Крутите колесо мышки для увеличения/уменьшения зума" + Environment.NewLine +
                       "--> Клик и удержание по колесику мышки для перемещения камеры"
            };

            aboutForm.Controls.Add(textBox);
            aboutForm.ShowDialog(this);
        }
    }
}