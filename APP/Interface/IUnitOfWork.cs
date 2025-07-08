


using SimplePointApplication.Entity;

namespace APP.Interface
{
    public interface IUnitOfWork : IDisposable
    {
        IGenericRepository<WktModel> genericRepository { get; }
        int Commit();


    }
}
