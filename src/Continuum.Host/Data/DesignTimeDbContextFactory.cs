using Continuum.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pgvector.EntityFrameworkCore;

namespace Continuum.Host.Data;

/// <summary>Lets <c>dotnet ef</c> build the context without booting the whole app.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ContinuumDbContext>
{
    public ContinuumDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ContinuumDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=continuum;Username=continuum;Password=continuum",
                npg => npg.UseVector())
            .Options;
        return new ContinuumDbContext(options);
    }
}
