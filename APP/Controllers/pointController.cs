using APP.Interface;
using Microsoft.AspNetCore.Mvc;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
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
        public object Add([FromBody] WktModel model)
        {
            try
            {
                var rawWkt = model.GetRawWkt();

                if (string.IsNullOrWhiteSpace(rawWkt))
                    return new { error = "WKT değeri boş olamaz." };

                var reader = new WKTReader(new GeometryFactory(new PrecisionModel(), 4326));
                Geometry geometry;
                try
                {
                    geometry = reader.Read(rawWkt);
                    geometry.SRID = 4326;
                }
                catch (ParseException ex)
                {
                    return new { error = $"Geometri hatalı: {ex.Message}" };
                }

                model.Geometry = geometry;

                _unitOfWork.genericRepository.Add(model);
                _unitOfWork.Commit();

                return model;
            }
            catch (Exception ex)
            {
                return new { error = $"Genel hata: {ex.Message}" };
            }
        }

        [HttpPost("range")]
        public object AddRange([FromBody] List<WktModel> points)
        {
            try
            {
                _unitOfWork.genericRepository.AddRange(points);
                _unitOfWork.Commit();
                return points;
            }
            catch (Exception ex)
            {
                return new { error = $"Error adding points: {ex.Message}" };
            }
        }

        [HttpGet]
        public object GetAll()
        {
            try
            {
                return _unitOfWork.genericRepository.GetAll();
            }
            catch (Exception ex)
            {
                return new { error = $"Error retrieving points: {ex.Message}" };
            }
        }

        [HttpGet("{id}")]
        public object GetById(int id)
        {
            try
            {
                var point = _unitOfWork.genericRepository.GetById(id);
                return point ;
            }
            catch (Exception ex)
            {
                return new { error = $"Error retrieving point: {ex.Message}" };
            }
        }

        [HttpPut("{id}")]
        public object Update(int id, [FromBody] WktModel point)
        {
            try
            {
                if (id != point.Id)
                    return new { error = "ID mismatch" };

                var rawWkt = point.GetRawWkt();

                if (string.IsNullOrWhiteSpace(rawWkt))
                    return new { error = "WKT değeri boş olamaz." };

                var reader = new WKTReader(new GeometryFactory(new PrecisionModel(), 4326));
                Geometry geometry;

                try
                {
                    geometry = reader.Read(rawWkt);
                    geometry.SRID = 4326;
                }
                catch (ParseException ex)
                {
                    return new { error = $"Geometri hatalı: {ex.Message}" };
                }

                point.Geometry = geometry;

                _unitOfWork.genericRepository.updateById(point);
                _unitOfWork.Commit();

                return point;
            }
            catch (Exception ex)
            {
                return new { error = $"Error updating point: {ex.Message}" };
            }
        }


        [HttpDelete("{id}")]
        public object Delete(int id)
        {
            try
            {
                var point = _unitOfWork.genericRepository.GetById(id);
                if (point == null)
                {
                    return new { error = "Point not found." };
                }

                _unitOfWork.genericRepository.deleteById(id);
                _unitOfWork.Commit();
                return new { message = "Silme işlemi başarılı." };
            }
            catch (Exception ex)
            {
                return new { error = $"Error deleting point: {ex.Message}" };
            }
        }
    }
}
