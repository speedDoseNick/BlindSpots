namespace BlindSpots.Models
{
    public class Polygon :  Shape
    {
        public List<Point> points { get; set; } = new();
    }
}
