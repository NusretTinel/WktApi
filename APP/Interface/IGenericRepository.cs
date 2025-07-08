namespace APP.Interface
{
    public interface IGenericRepository<T> where T : IEntity
    {
        public bool Add(T entity);
        public bool AddRange(List<T> rangeObj);
        public List<T> GetAll();  
        public T GetById(int id);
        public bool deleteById(int id); 
        public bool updateById(T entity );
    }
}
