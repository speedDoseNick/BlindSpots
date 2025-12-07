namespace BlindSpots.Models
{
    public class Point
    {
        public double x { get; set; }
        public double y { get; set; }

        public Point() {
            x = 0;
            y = 0;
        }

        public Point(double x, double y)
        {
            this.x = x;
            this.y = y;
        }
    }
}
