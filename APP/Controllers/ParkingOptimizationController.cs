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
        private const string DefaultPopulationFile = "tur_pop_2023_CN_100m_R2024B_v1.tif";

        public ParkingOptimizationController(
            IUnitOfWork unitOfWork,
            IConfiguration config)
           
        {
            _unitOfWork = unitOfWork;
            
            _populationDataPath = config["PopulationDataPath"] ?? DefaultPopulationFile;

            try
            {
                ValidatePopulationDataFile();
            }
            catch (Exception ex)
            {

                throw new Exception($"{ex.Message}"); ;
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
     [FromBody] List<WktModel> candidateSpots,
     [FromQuery][Range(1, 1000)] int topN = 10,
     [FromQuery][Range(0.000001, 10000)] double minDistance = 500,
     [FromQuery][Range(0.000001, 10000)] double cellSize = 100)
        {
            if (candidateSpots == null || candidateSpots.Count == 0)
            {

                return BadRequest("At least one candidate spot must be provided");
            }


            try
            {
               

                using (var optimizer = new ParkingOptimizer(_populationDataPath))
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
                
                return StatusCode(500, new { Error = ex.Message });
            }
        }
    }
}