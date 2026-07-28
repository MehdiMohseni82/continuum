using Continuum.Core.Domain;
using Continuum.Core.Embeddings;
using Microsoft.EntityFrameworkCore;

namespace Continuum.Core.Data;

public class ContinuumDbContext(DbContextOptions<ContinuumDbContext> options) : DbContext(options)
{
    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<MemoryItem> Memories => Set<MemoryItem>();
    public DbSet<Checkpoint> Checkpoints => Set<Checkpoint>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<AgentMessage> AgentMessages => Set<AgentMessage>();
    public DbSet<Handoff> Handoffs => Set<Handoff>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasPostgresExtension("vector");

        b.Entity<Machine>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => m.Name).IsUnique();
            e.Property(m => m.Name).HasMaxLength(200);
        });

        b.Entity<Workspace>(e =>
        {
            e.HasKey(w => w.Id);
            e.HasIndex(w => w.ProjectKey).IsUnique();
            e.HasIndex(w => w.OwnerId);
            e.Property(w => w.ProjectKey).HasMaxLength(512);
            e.Property(w => w.DisplayName).HasMaxLength(512);
        });

        b.Entity<Session>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).ValueGeneratedNever(); // source session id, not generated
            e.Property(s => s.Title).HasMaxLength(1024);
            e.HasIndex(s => s.LastEventAt);
            e.HasIndex(s => s.Status);
            e.HasOne(s => s.Machine).WithMany(m => m.Sessions).HasForeignKey(s => s.MachineId);
            e.HasOne(s => s.Workspace).WithMany(w => w.Sessions).HasForeignKey(s => s.WorkspaceId);
        });

        b.Entity<Event>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.SessionId, x.Uuid }).IsUnique(); // idempotency
            e.HasIndex(x => x.Timestamp);
            e.Property(x => x.RawJson).HasColumnType("jsonb");
            e.Property(x => x.Type).HasMaxLength(64);
            e.Property(x => x.Role).HasMaxLength(64);
            e.HasOne(x => x.Session).WithMany(s => s.Events).HasForeignKey(x => x.SessionId);

            // Full-text search: a generated tsvector over the flattened excerpt, GIN-indexed.
            e.HasGeneratedTsVectorColumn(x => x.SearchVector!, "english", x => new { TextExcerpt = x.TextExcerpt! })
             .HasIndex(x => x.SearchVector)
             .HasMethod("GIN");
        });

        b.Entity<MemoryItem>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => m.OwnerId);
            e.HasIndex(m => m.WorkspaceId);
            e.HasIndex(m => m.Type);
            e.Property(m => m.Embedding).HasColumnType($"vector({EmbeddingConfig.Dimensions})");
            // Cosine HNSW index for fast approximate nearest-neighbour recall.
            e.HasIndex(m => m.Embedding)
             .HasMethod("hnsw")
             .HasOperators("vector_cosine_ops");
        });

        b.Entity<Checkpoint>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => new { c.SessionId, c.CreatedAt });
            e.Property(c => c.Reason).HasMaxLength(32);
            e.HasOne(c => c.Session).WithMany().HasForeignKey(c => c.SessionId);
        });

        b.Entity<Agent>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasIndex(a => new { a.OwnerId, a.Name }).IsUnique();
            e.Property(a => a.Name).HasMaxLength(128);
        });

        b.Entity<Channel>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => new { c.OwnerId, c.Name }).IsUnique();
            e.Property(c => c.Name).HasMaxLength(128);
        });

        b.Entity<AgentMessage>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => new { m.ToAgentId, m.Read });
            e.HasIndex(m => new { m.ChannelId, m.Id });
            e.HasOne(m => m.FromAgent).WithMany().HasForeignKey(m => m.FromAgentId);
        });

        b.Entity<Handoff>(e =>
        {
            e.HasKey(h => h.Id);
            e.HasIndex(h => h.Status);
            e.Property(h => h.Status).HasMaxLength(16);
            e.Property(h => h.Title).HasMaxLength(256);
            e.HasOne(h => h.FromAgent).WithMany().HasForeignKey(h => h.FromAgentId);
            e.HasOne(h => h.ClaimedByAgent).WithMany().HasForeignKey(h => h.ClaimedByAgentId);
        });
    }
}
