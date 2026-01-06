using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SantaFeWaterSystem.Data
{
    public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

            optionsBuilder.UseNpgsql(
                "Host=centerbeam.proxy.rlwy.net;Port=48798;Database=railway;Username=postgres;Password=vCdxaaPXNpdhKeJkHLmgRmwKAYNcYdfY"
            );

            return new ApplicationDbContext(optionsBuilder.Options);
        }
    }
}
