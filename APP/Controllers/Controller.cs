using Microsoft.AspNetCore.Mvc;
using SimplePointApplication.Entity;
using SimplePointApplication.Optimizers;
using System.ComponentModel.DataAnnotations;

namespace SimplePointApplication.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TrashBinInputDto
    {
        public string Wkt { get; set; }
    }
    public class TrashBinController : ControllerBase
    {
        [HttpPost("optimize")]
        public ActionResult<List<WktModel>> OptimizeTrashBins([FromBody] OptimizationRequest request)
        {
            try
            {
                // Check for null inputs
                if (request == null)
                    return BadRequest("Request body cannot be null.");

                if (request.ExistingBins == null || request.ExistingBins.Count == 0)
                    return BadRequest("ExistingBins cannot be null or empty.");

                if (request.PopulationHeatmap == null || request.PopulationHeatmap.Length == 0)
                    return BadRequest("PopulationHeatmap cannot be null or empty.");

                // Ensure all WKT strings are valid
                var invalidBins = request.ExistingBins.Where(b => string.IsNullOrWhiteSpace(b.Wkt)).ToList();
                if (invalidBins.Any())
                    return BadRequest($"Invalid WKT in bins at positions: {string.Join(", ", invalidBins)}");

                // Proceed if everything is valid
                var existingBins = request.ExistingBins.Select(b => new WktModel { Wkt = b.Wkt }).ToList();
                var optimizer = new TrashBinOptimizer(existingBins);
                var result = optimizer.OptimizeTrashBins(
                    request.PopulationHeatmap,
                    request.CellSize,
                    request.NewBinCount,
                    request.MinDistance);

                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest($"Error during optimization: {ex.Message}");
            }
        }

        public class OptimizationRequest
        {
            [Required]
            public List<TrashBinInputDto> ExistingBins { get; set; }

            [Required]
            public double[][] PopulationHeatmap { get; set; }

            [Required]
            [Range(0.1, double.MaxValue)]
            public double CellSize { get; set; } = 1.0;

            [Required]
            [Range(1, int.MaxValue)]
            public int NewBinCount { get; set; } = 5;

            [Required]
            [Range(0.1, double.MaxValue)]
            public double MinDistance { get; set; } = 10.0;
        }
    }
}