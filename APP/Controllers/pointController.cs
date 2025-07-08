using APP.Interface;
using Microsoft.AspNetCore.Mvc;
using SimplePointApplication.Entity;

namespace SimplePointApplication.Controllers
{
     [ApiController]
    [Route("[controller]/[action]")] 
    public class PointController : ControllerBase
    {
        private readonly IUnitOfWork _unitOfWork;

        public PointController(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        [HttpPost]
        public IActionResult Add([FromBody] WktModel point)
        {
            try
            {
                _unitOfWork.genericRepository.Add(point);
                _unitOfWork.Commit();
                return Ok(point);
            }
            catch (Exception ex)
            {
                return BadRequest($"Error adding point: {ex.Message}");
            }
        }

        [HttpPost("range")]
        public IActionResult AddRange([FromBody] List<WktModel> points)
        {
            try
            {
                _unitOfWork.genericRepository.AddRange(points);
                _unitOfWork.Commit();
                return Ok(points);
            }
            catch (Exception ex)
            {
                return BadRequest($"Error adding points: {ex.Message}");
            }
        }

        [HttpGet]
        public IActionResult GetAll()
        {
            try
            {
                var points = _unitOfWork.genericRepository.GetAll();
                return Ok(points);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving points: {ex.Message}");
            }
        }

        [HttpGet("{id}")]
        public IActionResult GetById(int id)
        {
            try
            {
                var point = _unitOfWork.genericRepository.GetById(id);
                if (point == null)
                {
                    return NotFound();
                }
                return Ok(point);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving point: {ex.Message}");
            }
        }

        [HttpPut("{id}")]
        public IActionResult Update(int id, [FromBody] WktModel point)
        {
            try
            {
                if (id != point.Id)
                {
                    return BadRequest("ID mismatch");
                }

                _unitOfWork.genericRepository.updateById(point);
                _unitOfWork.Commit();
                return Ok(point);
            }
            catch (Exception ex)
            {
                return BadRequest($"Error updating point: {ex.Message}");
            }
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            try
            {
                var point = _unitOfWork.genericRepository.GetById(id);
                if (point == null)
                {
                    return NotFound();
                }

                _unitOfWork.genericRepository.deleteById(id);
                _unitOfWork.Commit();
                return NoContent();
            }
            catch (Exception ex)
            {
                return BadRequest($"Error deleting point: {ex.Message}");
            }
        }
    }
}