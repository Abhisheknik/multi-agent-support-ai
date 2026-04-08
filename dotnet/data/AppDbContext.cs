using Microsoft.EntityFrameworkCore;

namespace MultiAgentSupportAI.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ArticleEntity> Articles => Set<ArticleEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<ArticleEntity>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasIndex(a => a.DocId).IsUnique();
            e.HasIndex(a => a.Category);
            e.Property(a => a.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(a => a.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });
    }
}

public class ArticleEntity
{
    public int      Id        { get; set; }
    public string   DocId     { get; set; } = "";   // e.g. "billing_001"
    public string   Category  { get; set; } = "";
    public string   Title     { get; set; } = "";
    public string   Content   { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
