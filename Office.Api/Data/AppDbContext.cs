using Microsoft.EntityFrameworkCore;

namespace Office.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
}
