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


        //добавляем свойство полей камер
        [Category("Behavior")]
        [Description("If true, camera fields of view are drawn on top.")]
        [DefaultValue(false)]
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public bool ShowCameraFovs { get; set; } = false;
        


        // number of rays per camera (performance vs quality)
        private const int RayCount = 1800;

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
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;

            DrawGrid(e.Graphics);
            foreach (var s in _shapes) s.Draw(e.Graphics);
            if (SelectedShape != null) DrawSelection(e.Graphics);

            foreach (var shape in _shapes)
                shape.Draw(g);
            if (ShowCameraFovs)
            {
                var old = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                foreach (var cam in _shapes.OfType<CameraShape>())
                    DrawCameraFovWithOcclusion(g, cam);
                g.SmoothingMode = old;
            }
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
        private void DrawCameraFovWithOcclusion(Graphics g, CameraShape cam)
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
        }

        // returns distance from origin to intersection point along dir (dir normalized), or -1 if no hit within maxDist
        private float RayIntersectShapeDistance(PointF origin, PointF dir, Shape shape, float maxDist)
        {
            if (shape is RectShape)
            {
                var rect = new RectangleF(shape.Location, shape.Size);
                return RayIntersectRect(origin, dir, rect, maxDist);
            }
            else if (shape is CircleShape)
            {
                float cx = shape.Location.X + shape.Size.Width / 2f;
                float cy = shape.Location.Y + shape.Size.Height / 2f;
                float rx = shape.Size.Width / 2f;
                float ry = shape.Size.Height / 2f;
                return RayIntersectEllipse(origin, dir, new PointF(cx, cy), rx, ry, maxDist);
            }
            else if (shape is LineShape line)
            {
                var a = new PointF(line.Location.X, line.Location.Y);
                var b = new PointF(line.Location.X + line.Size.Width, line.Location.Y + line.Size.Height);
                return RayIntersectSegment(origin, dir, a, b, line.Thickness / 2f, maxDist);
            }
            return -1;
        }

        private float RayIntersectRect(PointF origin, PointF dir, RectangleF rect, float maxDist)
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

        private float RayIntersectEllipse(PointF origin, PointF dir, PointF center, float rx, float ry, float maxDist)
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

        private float RayIntersectSegment(PointF origin, PointF dir, PointF a, PointF b, float thicknessRadius, float maxDist)
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
        }
    }
}