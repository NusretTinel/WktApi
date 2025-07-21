using Microsoft.AspNetCore.Mvc;
using SimplePointApplication.Entity;
using SimplePointApplication.Optimizers;
using System.ComponentModel.DataAnnotations;
using APP.Interface;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using System.Linq;
using System.Text.Json;

namespace SimplePointApplication.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TrashBinController : ControllerBase
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly WKTReader _wktReader;
        private readonly string _populationDataPath;
        private const int MaxGridDimension = 1000;

        public TrashBinController(IUnitOfWork unitOfWork, IConfiguration config)
        {
            _unitOfWork = unitOfWork;
            _wktReader = new WKTReader();
            _populationDataPath = "C:\\Users\\USER\\Downloads\\GHS_POP_E2030_GLOBE_R2023A_54009_100_V1_0_R7_C19\\GHS_POP_E2030_GLOBE_R2023A_54009_100_V1_0_R7_C19.tif";

            if (!System.IO.File.Exists(_populationDataPath))
            {
                throw new FileNotFoundException("Global population data file not found");
            }
        }

        [HttpPost("optimize")]
        public IActionResult OptimizeTrashBins(
            [FromQuery][Range(0.1, 1000)] double cellSize = 10.0,
            [FromQuery][Range(1, 1000)] int newBinCount = 10,
            [FromQuery][Range(0, 10000)] double minDistance = 50.0,
            [FromBody] string polygonWkt = null)
        {
            try
            {
                // Validate polygon
                Polygon polygon = null;
                if (!string.IsNullOrEmpty(polygonWkt))
                {
                    var geometry = _wktReader.Read(polygonWkt);
                    if (geometry is not Polygon)
                        return BadRequest("Provided WKT must represent a Polygon");
                    polygon = (Polygon)geometry;
                }

                // Get existing bins (filtered by polygon if provided)
                var allBins = _unitOfWork.genericRepository.GetAll().ToList();
                var existingBins = polygon != null
                    ? allBins.Where(b => IsPointInPolygon(b.Wkt, polygon)).ToList()
                    : allBins;

                // Validate bins
                if (existingBins.Any(b => string.IsNullOrWhiteSpace(b.Wkt)))
                    return BadRequest("Some existing bins have invalid WKT data");

                // Calculate grid dimensions dynamically
                var (gridWidth, gridHeight) = CalculateGridDimensions(polygon, cellSize);

                // Optimize
                var optimizer = new TrashBinOptimizer(existingBins);
                var result = optimizer.OptimizeTrashBins(
                    _populationDataPath,
                    gridWidth,
                    gridHeight,
                    cellSize,
                    newBinCount,
                    minDistance,
                    polygonWkt);

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Optimization failed: {ex.Message}");
            }
        }

        private (int width, int height) CalculateGridDimensions(Polygon polygon, double cellSize)
        {
            if (polygon == null)
            {
                return (500, 500); // Default fallback
            }

            var env = polygon.EnvelopeInternal;
            int width = (int)Math.Ceiling((env.MaxX - env.MinX) / cellSize);
            int height = (int)Math.Ceiling((env.MaxY - env.MinY) / cellSize);

            // Apply maximum dimension limit
            if (width > MaxGridDimension || height > MaxGridDimension)
            {
                double scaleFactor = Math.Max(
                    (double)width / MaxGridDimension,
                    (double)height / MaxGridDimension);

                width = (int)(width / scaleFactor);
                height = (int)(height / scaleFactor);
            }

            return (width, height);
        }

        private bool IsPointInPolygon(string wkt, Polygon polygon)
        {
            try
            {
                var point = _wktReader.Read(wkt) as Point;
                return point != null && polygon.Contains(point);
            }
            catch
            {
                return false;
            }
        }
    }
}