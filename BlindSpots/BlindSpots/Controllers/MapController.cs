using BlindSpots.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NetTopologySuite.Geometries;

namespace BlindSpots.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MapController : Controller
    {
        // Путь к файлу (лежит в папке wwwroot или рядом с .exe)
        private readonly string _filePath = Path.Combine(Directory.GetCurrentDirectory(), "map.json");

        // Сохранить карту
        [HttpPost]
        public IActionResult SaveMap([FromBody] MapData map)
        {
            if (map == null)
                return BadRequest(new MapValidationError
                {
                    error = "missing_data",
                    message = "Отсутствуют данные карты"
                });

            var geometries = new List<Geometry>();
            var shapes = map.shapes;

            try
            {
                // Преобразуем каждую фигуру
                for (int i = 0; i < shapes.Count; i++)
                {
                    var geom = ShapeGeometryConverter.ToGeometry(shapes[i]);
                    geometries.Add(geom);
                }

                // Проверяем все пары
                for (int i = 0; i < geometries.Count; i++)
                {
                    for (int j = i + 1; j < geometries.Count; j++)
                    {
                        if (geometries[i].Intersects(geometries[j]))
                        {
                            // Определяем типы для отладки
                            string GetType(Type t)
                            {
                                return t.Name switch
                                {
                                    "Rectangle" => "прямоугольник",
                                    "Circle" => "круг",
                                    "Polygon" => "полигон",
                                    _ => "unknown"
                                };
                            }

                            return BadRequest(new MapValidationError
                            {
                                error = "intersection",
                                message = $"Фигуры #{i + 1} и #{j + 1} пересекаются.",
                                indexA = i,
                                indexB = j,
                                shapeTypeA = GetType(shapes[i].GetType()),
                                shapeTypeB = GetType(shapes[j].GetType())
                            });
                        }
                    }
                }
            }



            catch (Exception ex)
            {
                return BadRequest(new MapValidationError
                {
                    error = "geometry_error",
                    message = $"Ошибка при обработке геометрии: {ex.Message}"
                });
            }

            // Сохраняем в файл
            try
            {
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                };
                var json = System.Text.Json.JsonSerializer.Serialize(map, options);
                System.IO.File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new MapValidationError
                {
                    error = "file_error",
                    message = $"Не удалось сохранить файл: {ex.Message}"
                });
            }
            return Ok();
        }

        // Загрузить карту
        [HttpGet]
        public IActionResult GetMap()
        {
            try
            {
                if (!System.IO.File.Exists(_filePath))
                {
                    // Если файла нет — возвращаем пустую карту
                    return Ok(new MapData());
                }

                var json = System.IO.File.ReadAllText(_filePath);
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                };
                var map = System.Text.Json.JsonSerializer.Deserialize<MapData>(json) ?? new MapData();
                return Ok(map);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "file_read_error", message = ex.Message });
            }
        }
    }
}
