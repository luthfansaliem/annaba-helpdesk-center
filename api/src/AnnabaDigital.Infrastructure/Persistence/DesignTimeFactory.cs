using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AnnabaDigital.Infrastructure.Persistence;

/// <summary>Lets `dotnet ef` build the model without booting the web host (no DB connection is opened to generate migrations).</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AnnabaDigitalDbContext>
{
    public AnnabaDigitalDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ANNABA_DIGITAL_DESIGN_CONNECTION") ?? "Host=localhost;Database=annaba_digital_design;Username=design;Password=design";
        var options = new DbContextOptionsBuilder<AnnabaDigitalDbContext>().UseNpgsql(cs).UseSnakeCaseNamingConvention().Options;
        return new AnnabaDigitalDbContext(options);
    }
}
