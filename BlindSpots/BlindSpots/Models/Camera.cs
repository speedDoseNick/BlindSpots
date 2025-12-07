namespace BlindSpots.Models
{
    public class Camera : Shape
    {
        public double angle { get; set; }     // направление камеры (в градусах)
        public double fieldOfView { get; set; } // угол обзора (FOV), например 60
        public double range { get; set; }       // максимальная дистанция
    }
}
