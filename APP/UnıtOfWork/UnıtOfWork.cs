

using APP.Interface;
using APP.Repository;
using SimplePointApplication.Entity;

namespace APP.UnıtOfWork
{
    public class UnitOfWork : IUnitOfWork
    {
        public readonly AppDbContext _context;
        public IGenericRepository<WktModel> genericRepository { get; private set; } 
        public UnitOfWork(AppDbContext context)
        {
            _context = context;
            genericRepository = new GenericRepository<WktModel>(_context);
        }

        

        public int Commit()
        {
            return _context.SaveChanges();
        }
        public void Dispose()
        {
             _context?.Dispose(); 
        }
    }
}
