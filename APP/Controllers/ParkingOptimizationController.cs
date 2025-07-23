using APP.Interface;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SimplePointApplication.Entity;
using SimplePointApplication.Optimizers;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;

namespace SimplePointApplication.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ParkingOptimizationController : ControllerBase
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly string _populationDataPath;
        private readonly ILogger<ParkingOptimizationController> _logger;
        private const string DefaultPopulationFile = "tur_pop_2023_CN_100m_R2024B_v1.tif";

        public ParkingOptimizationController(
            IUnitOfWork unitOfWork,
            IConfiguration config,
            ILogger<ParkingOptimizationController> logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
            _populationDataPath = config["PopulationDataPath"] ?? DefaultPopulationFile;

            try
            {
                ValidatePopulationDataFile();
                _logger.LogInformation("Successfully initialized parking optimization controller");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Controller initialization failed");
                throw;
            }
        }

        private void ValidatePopulationDataFile()
        {
            if (!System.IO.File.Exists(_populationDataPath))
            {
                throw new FileNotFoundException($"Population data file not found at: {Path.GetFullPath(_populationDataPath)}");
            }
        }

        [HttpPost("optimize")]
        public ActionResult<List<WktModel>> OptimizeParkingSpots(
     [FromQuery][Range(1, 1000)] int topN = 10,
     [FromQuery][Range(0.000001, 10000)] double minDistance = 0.027,
     [FromQuery][Range(0.000001, 1)] double cellSize = 0.009)
        {
            var candidateSpots = new List<WktModel>
    {
        new WktModel { Id = 1, Name = "Taksim Square", Wkt = "POINT (28.9871 41.0370)" },
        new WktModel { Id = 2, Name = "Sultanahmet", Wkt = "POINT (28.9764 41.0055)" },
        new WktModel { Id = 3, Name = "Kadikoy", Wkt = "POINT (29.0206 40.9906)" },
        new WktModel { Id = 4, Name = "Besiktas", Wkt = "POINT (29.0089 41.0429)" },
        new WktModel { Id = 5, Name = "Levent", Wkt = "POINT (28.9949 41.0794)" },
        new WktModel { Id = 6, Name = "Bebek", Wkt = "POINT (29.0433 41.0772)" },
        new WktModel { Id = 7, Name = "Uskudar", Wkt = "POINT (29.0267 41.0228)" }
    };
        
            try
            {
                _logger.LogInformation("Starting optimization with {Count} candidate spots", candidateSpots.Count);

                // Verify WKT was properly set
                foreach (var spot in candidateSpots)
                {
                    if (spot.Geometry == null)
                    {
                        _logger.LogWarning("Failed to parse WKT for spot {Id}: {Wkt}", spot.Id, spot.Wkt);
                    }
                }

                using (var optimizer = new ParkingOptimizer(_populationDataPath, _logger))
                {
                    var optimizedSpots = optimizer.OptimizeParkingSpots(
                        candidateSpots,
                        topN,
                        minDistance,
                        cellSize);

                    return Ok(optimizedSpots);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Optimization failed");
                return StatusCode(500, new { Error = ex.Message });
            }
        }
    }
}