using Microsoft.AspNetCore.Mvc;
using SimplePointApplication.Entity;
using SimplePointApplication.Optimizers;
using System.ComponentModel.DataAnnotations;
using APP.Interface;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using System.Linq;

namespace SimplePointApplication.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TrashBinController : ControllerBase
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly WKTReader _wktReader;
        private readonly double[][] _fixedPopulationHeatmap =
[
    [0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 0.9, 0.8, 0.7, 0.6, 0.5, 0.4, 0.3, 0.2, 0.1, 0.0],
    [0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.1, 1.0, 0.9, 0.8, 0.7, 0.6, 0.5, 0.4, 0.3, 0.2, 0.1],
    [0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.1, 1.2, 1.1, 1.0, 0.9, 0.8, 0.7, 0.6, 0.5, 0.4, 0.3, 0.2],
    [0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.1, 1.2, 1.3, 1.2, 1.1, 1.0, 0.9, 0.8, 0.7, 0.6, 0.5, 0.4, 0.3],
    [0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.1, 1.2, 1.3, 1.4, 1.3, 1.2, 1.1, 1.0, 0.9, 0.8, 0.7, 0.6, 0.5, 0.4],
    [0.6, 0.7, 0.8, 0.9, 1.0, 1.1, 1.2, 1.3, 1.4, 1.5, 1.4, 1.3, 1.2, 1.1, 1.0, 0.9, 0.8, 0.7, 0.6, 0.5],
    [0.7, 0.8, 0.9, 1.0, 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.5, 1.4, 1.3, 1.2, 1.1, 1.0, 0.9, 0.8, 0.7, 0.6],
    [0.8, 0.9, 1.0, 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.6, 1.5, 1.4, 1.3, 1.2, 1.1, 1.0, 0.9, 0.8, 0.7],
    [0.9, 1.0, 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.7, 1.6, 1.5, 1.4, 1.3, 1.2, 1.1, 1.0, 0.9, 0.8],
    [1.0, 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9, 1.8, 1.7, 1.6, 1.5, 1.4, 1.3, 1.2, 1.1, 1.0, 0.9],
    [0.9, 1.0, 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.7, 1.6, 1.5, 1.4, 1.3, 1.2, 1.1, 1.0, 0.9, 0.8],
    [0.8, 0.9, 1.0, 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.6, 1.5, 1.4, 1.3, 1.2, 1.1, 1.0, 0.9, 0.8, 0.7],
    [0.7, 0.8, 0.9, 1.0, 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.5, 1.4, 1.3, 1.2, 1.1, 1.0, 0.9, 0.8, 0.7, 0.6],
    [0.6, 0.7, 0.8, 0.9, 1.0, 1.1, 1.2, 1.3, 1.4, 1.5, 1.4, 1.3, 1.2, 1.1, 1.0, 0.9, 0.8, 0.7, 0.6, 0.5],
    [0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.1, 1.2, 1.3, 1.4, 1.3, 1.2, 1.1, 1.0, 0.9, 0.8, 0.7, 0.6, 0.5, 0.4],
    [0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.1, 1.2, 1.3, 1.2, 1.1, 1.0, 0.9, 0.8, 0.7, 0.6, 0.5, 0.4, 0.3],
    [0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.1, 1.2, 1.1, 1.0, 0.9, 0.8, 0.7, 0.6, 0.5, 0.4, 0.3, 0.2],
    [0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.1, 1.0, 0.9, 0.8, 0.7, 0.6, 0.5, 0.4, 0.3, 0.2, 0.1],
    [0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 0.9, 0.8, 0.7, 0.6, 0.5, 0.4, 0.3, 0.2, 0.1, 0.0],
    [0.0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 0.8, 0.7, 0.6, 0.5, 0.4, 0.3, 0.2, 0.1, 0.0, 0.0]
];

        public TrashBinController(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
            _wktReader = new WKTReader();
        }

        [HttpPost("optimize")]
        public IActionResult OptimizeTrashBins(
            [FromQuery][Range(0.1, double.MaxValue)] double cellSize = 1.0,
            [FromQuery][Range(1, int.MaxValue)] int newBinCount = 5,
            [FromQuery][Range(0, double.MaxValue)] double minDistance = 0,
            [FromBody] string polygonWkt = null)
        {
            try
            {
                var allBins = _unitOfWork.genericRepository.GetAll().ToList();
                Polygon polygon = null;

               
                if (!string.IsNullOrEmpty(polygonWkt))
                {
                    var geometry = _wktReader.Read(polygonWkt);
                    if (geometry is not Polygon poly)
                        return BadRequest("Provided WKT must represent a Polygon");
                    polygon = poly;
                }

                
                List<WktModel> existingBins;
                if (polygon != null)
                {
                    existingBins = allBins.Where(bin =>
                    {
                        if (string.IsNullOrWhiteSpace(bin.Wkt))
                            return false;

                        try
                        {
                            var point = _wktReader.Read(bin.Wkt) as Point;
                            return point != null && polygon.Contains(point);
                        }
                        catch
                        {
                            return false;
                        }
                    }).ToList();
                }
                else
                {
                    existingBins = allBins;
                }

                
                var invalidBins = existingBins.Where(b => string.IsNullOrWhiteSpace(b.Wkt)).ToList();
                if (invalidBins.Any())
                    return BadRequest($"Invalid WKT in bins at positions: {string.Join(", ", invalidBins)}");

                
                var optimizer = new TrashBinOptimizer(existingBins);
                var result = optimizer.OptimizeTrashBins(
                    _fixedPopulationHeatmap,
                    cellSize,
                    newBinCount,
                    minDistance,
                    polygon);  

              
                _unitOfWork.genericRepository.AddRange(result);
                _unitOfWork.Commit();

                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest($"Error during optimization: {ex.Message}");
            }
        }
    }
}