using Microsoft.EntityFrameworkCore;
using VRCVideoCacher.Database.Models;

namespace VRCVideoCacher.Database;

public class Database : DbContext
{
    internal static readonly string CacheDir = Path.Join(Program.DataPath, "MetadataCache");
    internal static readonly string DbPath = Path.Join(CacheDir, "database.db");

    public DbSet<History> PlayHistory { get; set; }
    public DbSet<VideoInfoCache> VideoInfoCache { get; set; }
    
    // Required for PooledDbContextFactory
    public Database(DbContextOptions<Database> options) : base(options) { }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            Directory.CreateDirectory(CacheDir);
            optionsBuilder.UseSqlite($"Data Source={DbPath}");
            optionsBuilder.EnableSensitiveDataLogging();
        }
    }
}