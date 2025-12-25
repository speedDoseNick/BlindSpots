using Microsoft.AspNetCore.Rewrite;
using System.Drawing;
using System.Text.Json.Serialization;
namespace BlindSpots.Models
{
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(Rect), "rectangle")]
    [JsonDerivedType(typeof(Circle), "circle")]
    [JsonDerivedType(typeof(Polygon), "polygon")]
    public class Shape
    {
        public Point? position { get; set; } = new();
        public double rotation { get; set; } = new float();
    }
}
