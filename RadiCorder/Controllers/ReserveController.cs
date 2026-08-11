using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RadiCorder.Logics.Logics.StationLogic;
using RadiCorder.Models;

namespace RadiCorder.Controllers
{
    public class ReserveController(
        StationLobLogic stationLobLogic) : Controller
    {
        public IActionResult ProgramReserveList()
        {
            return View();
        }

        public async ValueTask<IActionResult> KeywordReserveList()
        {
            var radikoStationList = await stationLobLogic.GetRadikoStationAsync();

            var radiruStationList = await stationLobLogic.GetRadiruStationAsync();

            return View(new KeywordReserveViewModel
            {
                RadikoStationList = radikoStationList,
                RadiruStationList = radiruStationList
            });
        }


        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
