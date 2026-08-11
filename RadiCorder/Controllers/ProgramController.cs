using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RadiCorder.Models;

namespace RadiCorder.Controllers
{
    public class ProgramController : Controller
    {

        public IActionResult Index()
        {
            return View();
        }


        public IActionResult Radiko()
        {
            return View();
        }

        public IActionResult Nhk()
        {
            return View();
        }


        public IActionResult Search()
        {
            return View();
        }


        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
