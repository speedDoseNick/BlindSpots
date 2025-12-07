namespace BlindSpots.Models
{
    public class MapValidationError
    {
        public string error { get; set; } = "validation_error";
        public string message { get; set; } = string.Empty;
        public int? indexA { get; set; }
        public int? indexB { get; set; }
        public string? shapeTypeA { get; set; }
        public string? shapeTypeB { get; set; }
    }
}
