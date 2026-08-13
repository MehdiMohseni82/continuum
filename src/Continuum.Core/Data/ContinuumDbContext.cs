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
    public DbSet<User> Users => Set<User>();
    public DbSet<AccessToken> AccessTokens => Set<AccessToken>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RoomMember> RoomMembers => Set<RoomMember>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrgMembership> OrgMemberships => Set<OrgMembership>();
    public DbSet<Grant> Grants => Set<Grant>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasPostgresExtension("vector");

        b.Entity<Machine>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => m.Name).IsUnique();
            e.Property(m => m.Name).HasMaxLength(200);
        });

        b.Entity<Organization>(e =>
        {
            e.HasKey(o => o.Id);
            e.HasIndex(o => o.Slug).IsUnique();
            e.Property(o => o.Name).HasMaxLength(200);
            e.Property(o => o.Slug).HasMaxLength(100);
        });

        b.Entity<OrgMembership>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => new { m.OrgId, m.UserId }).IsUnique();
            e.HasIndex(m => m.UserId);
            e.HasOne(m => m.Organization).WithMany(o => o.Members).HasForeignKey(m => m.OrgId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Grant>(e =>
        {
            e.HasKey(g => g.Id);
            // The shape every visibility query probes: which rows of this kind are granted to me.
            e.HasIndex(g => new { g.OrgId, g.ResourceType, g.ResourceId });
            e.HasIndex(g => new { g.OrgId, g.PrincipalType, g.PrincipalId });
            e.HasIndex(g => new { g.ResourceType, g.ResourceId, g.PrincipalType, g.PrincipalId }).IsUnique();
        });

        b.Entity<Team>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasIndex(t => new { t.OrgId, t.Name }).IsUnique();
            e.Property(t => t.Name).HasMaxLength(128);
        });

        b.Entity<TeamMember>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => new { m.TeamId, m.UserId }).IsUnique();
            e.HasIndex(m => m.UserId);
            e.HasOne(m => m.Team).WithMany(t => t.Members).HasForeignKey(m => m.TeamId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Workspace>(e =>
        {
            e.HasKey(w => w.Id);
            // Per organization, not global: two tenants may each work on a repo with the same path,
            // and they must not collapse into one shared row.
            e.HasIndex(w => new { w.OrgId, w.ProjectKey }).IsUnique();
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
            e.HasIndex(s => s.OwnerId);
            e.HasIndex(s => s.OrgId);
            e.HasIndex(s => s.Shared);
            e.Property(s => s.SummaryEmbedding).HasColumnType($"vector({EmbeddingConfig.Dimensions})");
            e.HasIndex(s => s.SummaryEmbedding).HasMethod("hnsw").HasOperators("vector_cosine_ops");
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
            e.HasIndex(m => m.OrgId);
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

        // Agents, channels and rooms are addressed by name, and that name has to mean one thing to
        // everyone who shares a room — so uniqueness is per organization, not per user. Safe to change:
        // every existing row has a single owner and already-distinct names, so nothing can collide.
        b.Entity<Agent>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasIndex(a => new { a.OrgId, a.Name }).IsUnique();
            e.HasIndex(a => a.OwnerId);
            e.Property(a => a.Name).HasMaxLength(128);
        });

        b.Entity<Channel>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => new { c.OrgId, c.Name }).IsUnique();
            e.HasIndex(c => c.OwnerId);
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

        b.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Email).HasMaxLength(320);
            e.Property(u => u.DisplayName).HasMaxLength(200);
            e.Property(u => u.PasswordHash).HasMaxLength(512);
        });

        b.Entity<AccessToken>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.UserId);
            e.Property(t => t.Name).HasMaxLength(200);
            e.Property(t => t.TokenHash).HasMaxLength(128);
            e.Property(t => t.Prefix).HasMaxLength(32);
            e.HasOne(t => t.User).WithMany(u => u.Tokens).HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Room>(e =>
        {
            e.HasKey(r => r.Id);
            e.HasIndex(r => new { r.OrgId, r.Name }).IsUnique();
            e.HasIndex(r => r.OwnerId);
            e.HasIndex(r => r.Status);
            e.HasIndex(r => r.ChannelName);
            e.Property(r => r.Name).HasMaxLength(200);
            e.Property(r => r.Topic).HasMaxLength(2000);
            e.Property(r => r.Language).HasMaxLength(64);
            e.Property(r => r.Status).HasMaxLength(16);
            e.Property(r => r.ChannelName).HasMaxLength(200);
        });

        b.Entity<RoomMember>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => new { m.RoomId, m.AgentId }).IsUnique();
            e.HasOne(m => m.Room).WithMany(r => r.Members).HasForeignKey(m => m.RoomId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.Agent).WithMany().HasForeignKey(m => m.AgentId);
        });
    }
}
