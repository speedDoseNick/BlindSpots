using System.Diagnostics;
using BlindSpots.Models;
using Microsoft.AspNetCore.Mvc;

namespace BlindSpots.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
