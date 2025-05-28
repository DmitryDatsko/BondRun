using BondRun.Data;
using Microsoft.EntityFrameworkCore;

namespace BondRun.DbInitializer;

public class DbInitializer(ApiDbContext context) : IDbInitializer
{
    public void Initialize()
    {
        context.Database.Migrate();
    }
}