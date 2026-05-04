using Microsoft.EntityFrameworkCore;

public class WireDbContext : DbContext
{
    public WireDbContext(DbContextOptions<WireDbContext> options):base(options) {}

    public DbSet<WireTransaction> WireTransactions => Set<WireTransaction>();
    public DbSet<IsoMessage> IsoMessages => Set<IsoMessage>();
    public DbSet<Account> Accounts => Set<Account>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WireTransaction>()
        .HasIndex(w => w.ClientReferenceId)
        .IsUnique();
    }
}