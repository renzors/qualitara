using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Relay.Data;

/// <summary>Lets `dotnet ef` create migrations without starting the API.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<RelayDbContext>
{
    public RelayDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<RelayDbContext>().UseSqlite("Data Source=design.db").Options);
}
