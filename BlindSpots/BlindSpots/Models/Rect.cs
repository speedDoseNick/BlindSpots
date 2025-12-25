namespace BlindSpots.Models
{
    public class Rect : Shape
    {
        public double width { get; set; }
        public double height { get; set; }

        public Rect(double Width, double Height, double Rotation)
        {
            width = Width;
            height = Height;
            rotation = Rotation;
        }
    }
}
