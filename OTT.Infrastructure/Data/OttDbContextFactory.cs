using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OTT.Infrastructure.Data;

/// <summary>
/// Design-time factory so `dotnet ef migrations` can build the model without
/// starting the full web host (Redis/Hangfire/etc). The connection string here
/// is only used at design time; the running app uses ConnectionStrings:DefaultConnection.
/// </summary>
public class OttDbContextFactory : IDesignTimeDbContextFactory<OttDbContext>
{
    public OttDbContext CreateDbContext(string[] args)
    {
        var connStr = Environment.GetEnvironmentVariable("OTT_DESIGN_CONNECTION")
            ?? "Server=localhost,1433;Database=ott_platform;User Id=sa;Password=Strong!Passw0rd;TrustServerCertificate=True;";

        var options = new DbContextOptionsBuilder<OttDbContext>()
            .UseSqlServer(connStr)
            .Options;

        return new OttDbContext(options);
    }
}
