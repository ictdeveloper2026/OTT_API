using Microsoft.EntityFrameworkCore;
using OTT.Infrastructure.Data;
using StackExchange.Redis;
using Testcontainers.MsSql;
using Testcontainers.Redis;
using Xunit;

namespace OTT.IntegrationTests;

/// <summary>
/// Spins up real SQL Server + Redis containers once for the whole test collection, applies the EF
/// migrations against SQL Server, and exposes factories for fresh DbContexts / the Redis multiplexer.
/// This catches what the InMemory unit tests cannot: migrations actually applying, queries
/// translating to T-SQL, and the Lua slot script running on a real Redis.
/// </summary>
public sealed class ContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    public string SqlConnectionString => _sql.GetConnectionString();
    public IConnectionMultiplexer Redis { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_sql.StartAsync(), _redis.StartAsync());

        // Apply every migration against the real SQL Server instance.
        await using var db = NewDbContext();
        await db.Database.MigrateAsync();

        Redis = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
    }

    public OttDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<OttDbContext>()
            .UseSqlServer(SqlConnectionString)
            .Options);

    public async Task DisposeAsync()
    {
        if (Redis is not null) await Redis.DisposeAsync();
        await _redis.DisposeAsync();
        await _sql.DisposeAsync();
    }
}

[CollectionDefinition("containers")]
public class ContainerCollection : ICollectionFixture<ContainerFixture> { }
