using Microsoft.AspNetCore.Mvc;
using SimplePointApplication.Entity;
using SimplePointApplication.Optimizers;
using System.ComponentModel.DataAnnotations;
using APP.Interface;

namespace SimplePointApplication.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TrashBinController : ControllerBase
    {
        private readonly IUnitOfWork _unitOfWork;

        public TrashBinController(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        [HttpPost("optimize")]
        public ActionResult<List<WktModel>> OptimizeTrashBins(
            [FromBody] double[][] populationHeatmap,
            [FromQuery][Range(0.1, double.MaxValue)] double cellSize = 1.0,
            [FromQuery][Range(1, int.MaxValue)] int newBinCount = 5,
            [FromQuery][Range(0, double.MaxValue)] double minDistance = 0)
        {
            try
            {
                var existingBins = _unitOfWork.genericRepository.GetAll();
                var invalidBins = existingBins.Where(b => string.IsNullOrWhiteSpace(b.Wkt)).ToList();
                if (invalidBins.Any())
                    return BadRequest($"Invalid WKT in bins at positions: {string.Join(", ", invalidBins)}");

                var dbBins = _unitOfWork.genericRepository.GetAll().ToList();
                var optimizer = new TrashBinOptimizer(dbBins);
                var result = optimizer.OptimizeTrashBins(
                    populationHeatmap,
                    cellSize,
                    newBinCount,
                    minDistance);

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