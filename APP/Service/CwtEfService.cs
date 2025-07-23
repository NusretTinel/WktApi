
using static SimplePointApplication.Response.Result.Response;
using SimplePointApplication.Entity;
using APP.Interface;
using NetTopologySuite.Geometries;


namespace APP.Service
{
    public class CwtEfService 
    {
        private readonly IUnitOfWork _unitOfWork;

        public CwtEfService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;    
        }
        
            public bool Add(string name, Geometry WKT,string type)
            {
                var result = new Result();
            var _point = new WktModel
            {
                Name = name,
                Geometry = WKT,
                TypeN = type
            };
            _unitOfWork.genericRepository.Add(_point);
             _unitOfWork.Commit();
                return result.Success;
            }

            public bool AddRange(List<SimplePointApplication.Entity.WktModel> rangeObj)
            {
            var result = new Result();
            _unitOfWork.genericRepository.AddRange(rangeObj);
            result.Message = "İşlem Başarılı";
            result.Data = rangeObj;
            result.Success = true;
            _unitOfWork.Commit();
            return result.Success;
            }

            public List<WktModel> GetAll()
            {
                

                return _unitOfWork.genericRepository.GetAll();
            }

            public WktModel GetById(int id)
            {



            return _unitOfWork.genericRepository.GetById(id);
            }

            public bool deleteById(int id)
            {
                var result = new Result();
            var Obj = new WktModel() ;
          ;
            _unitOfWork.genericRepository.deleteById(id);

            _unitOfWork.Commit();
            return result.Success;
            }

            public bool updateById(int id, string Name, string WKT)
            {
                var result = new Result();
            _unitOfWork.Commit();
            return result.Success;
            }
        }
    }

