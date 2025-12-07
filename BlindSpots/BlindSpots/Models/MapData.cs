namespace BlindSpots.Models
{
    public class MapData
    {
        public List<Shape> shapes { get; set; } = new(); //Примитивы на карте
        public List<Camera> cameras { get; set; } = new(); //Камеры на карте
    }
}
