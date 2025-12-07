using NetTopologySuite.Geometries;
using System.Drawing;

namespace BlindSpots.Models
{
    public class ShapeGeometryConverter
    {
        private static readonly GeometryFactory Factory = new();

        public static Geometry ToGeometry(Shape shape)
        {
            return shape switch
            {
                Rect rect => CreateRectangle(rect),
                Circle circle => CreateCircle(circle),
                Polygon poly => CreatePolygon(poly),
                _ => throw new ArgumentException($"Неизвестный тип фигуры: {shape?.GetType()}")
            };
        }

        private static Geometry CreateRectangle(Rect rect)
        {
            var coords = new[]
            {
            new Coordinate(rect.position.x, rect.position.y),
            new Coordinate(rect.position.x + rect.width, rect.position.y),
            new Coordinate(rect.position.x + rect.width, rect.position.y + rect.height),
            new Coordinate(rect.position.x, rect.position.y + rect.height),
            new Coordinate(rect.position.x, rect.position.y) // замыкаем
        };
            return Factory.CreatePolygon(coords);
        }

        private static Geometry CreateCircle(Circle circle, int segments = 32)
        {
            // Окружность → многоугольник
            var shell = new Coordinate[segments + 1];
            var angleStep = 2 * Math.PI / segments;
            for (int i = 0; i <= segments; i++)
            {
                double angle = i * angleStep;
                double x = circle.position.x + circle.radius * Math.Cos(angle);
                double y = circle.position.y + circle.radius * Math.Sin(angle);
                shell[i] = new Coordinate(x, y);
            }
            return Factory.CreatePolygon(shell);
        }

        private static Geometry CreatePolygon(Polygon poly)
        {
            if (poly.points.Count < 3)
                throw new ArgumentException("Полигон должен содержать минимум 3 точки");

            var coords = poly.points.Select(p => new Coordinate(p.x, p.y)).ToArray();
            // Замыкаем, если не замкнут
            if (coords[0] != coords[^1])
            {
                coords = coords.Append(coords[0]).ToArray();
            }
            return Factory.CreatePolygon(coords);
        }
    }
}
