using Microsoft.AspNetCore.Mvc;
using NetTopologySuite.Geometries;
using SimplePointApplication.Entity;
using SimplePointApplication.Optimizers;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace SimplePointApplication.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class KMeansController : ControllerBase

    {
        private readonly string _populationDataPath = "tur_pop_2023_CN_100m_R2024B_v1.tif";

        [HttpPost("optimize")]
        public ActionResult<List<WktModel>> OptimizePoints(
            [FromBody] string polygonWkt = null,
            [FromQuery][Range(0.000001, 10000)] double cellSize = 0.009,
            [FromQuery][Range(1, 1000)] int pointCount = 10,
            [FromQuery][Range(0.000001, 10000)] double minDistance = 0.027)
        {
            try
            {
                List<WktModel> existingPoints = null;
                var points = existingPoints?.Select(m => m.Geometry as Point).ToList() ?? new List<Point>();

                using (var optimizer = new KMeansOptimizer(points))
                {
                    var optimizedPoints = optimizer.Optimize(
                        _populationDataPath,
                        cellSize,
                        pointCount,
                        minDistance,
                        polygonWkt);

                  
                    var results = optimizedPoints.Select(p => new WktModel
                    {
                        Geometry = p,
                        Wkt = p.ToText(),
                        Name = "Optimized Point"
                    }).ToList();

                    return Ok(results);
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Optimization failed: {ex.Message}");
            }
        }
    }
}