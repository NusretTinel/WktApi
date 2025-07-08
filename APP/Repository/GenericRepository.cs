using APP.Interface;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;

namespace APP.Repository
{
    public class GenericRepository<TEntity> : IGenericRepository<TEntity> where TEntity : class , IEntity
    {
        private readonly AppDbContext _dbContext;
        private readonly DbSet<TEntity> _dbSet;

        public GenericRepository(AppDbContext dbContext)
        {
            _dbContext = dbContext;
            _dbSet = dbContext.Set<TEntity>();
        }

        public bool Add(TEntity entity)
        {
            _dbContext.Add(entity);
            _dbContext.SaveChanges();

            return true;
        }

        public bool AddRange(List<TEntity> rangeObj)
        {
            _dbContext.AddRange(rangeObj);
            _dbContext.SaveChanges();

            return true;
        }

        public List<TEntity> GetAll()
        {
            return _dbSet.ToList();
        }

        public TEntity GetById(int id)
        {
            return _dbSet.Find(id);
        }

        public  bool deleteById(int id)
        {
            var entity = GetById(id);
            if (entity != null)
            {
                _dbSet.Remove(entity);
               
                _dbContext.SaveChanges();
                return true;
            }
            return false;
        }

        public bool updateById(TEntity entity)
        {
            _dbSet.Update(entity);
            _dbContext.SaveChanges();

            return true;
        }
    }
}