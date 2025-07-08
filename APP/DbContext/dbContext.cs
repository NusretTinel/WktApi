using Microsoft.EntityFrameworkCore;
using SimplePointApplication.Entity;
using NetTopologySuite.Geometries;
public class AppDbContext : DbContext
{
    public DbSet<WktModel> Points => Set<WktModel>();

    public AppDbContext(DbContextOptions<AppDbContext> opts) : base(opts) { }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<WktModel>(e =>
        {
            e.Property(p => p.Geometry).HasColumnType("geometry(Point,4326)");

            
        });
    }
}
