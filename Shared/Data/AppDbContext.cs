using AutoGenerate.AiService;
using AutoGenerate.Auth;
using AutoGenerate.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace AutoGenerate.Shared.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<Client> Clients { get; set; }
    public DbSet<OtpCode> OtpCodes { get; set; }
    public DbSet<Post> Posts { get; set; }
    public DbSet<Topic> Topics { get; set; }
    public DbSet<SocialAccount> SocialAccounts { get; set; }
    public DbSet<Platform> Platforms { get; set; }
    public DbSet<AutoGenerate.CaptionService.Models.Caption> Captions { get; set; }

    public DbSet<AiRecommendation> AiRecommendations { get; set; }
    public DbSet<ExternalTask> ExternalTasks { get; set; }
    public DbSet<Notification> Notifications { get; set; }


    public DbSet<PostImage> PostImages { get; set; }
    public DbSet<SheetRow>       SheetRows       { get; set; }
    public DbSet<TrelloBrief>    TrelloBriefs    { get; set; }
    public DbSet<PostAssignment> PostAssignments { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<Post>()
            .HasOne(p => p.User)
            .WithMany(u => u.Posts)
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Post>()
            .HasOne(p => p.Topic)
            .WithMany(t => t.Posts)
            .HasForeignKey(p => p.TopicId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Post>()
            .Property(p => p.Status)
            .HasConversion<string>();

        modelBuilder.Entity<Post>()
            .Property(p => p.ScheduledAt)
            .HasColumnType("smalldatetime");

        modelBuilder.Entity<Post>()
            .Property(p => p.PublishedAt)
            .HasColumnType("smalldatetime");


        modelBuilder.Entity<AutoGenerate.CaptionService.Models.Caption>()
            .HasOne(c => c.Post)
            .WithMany(p => p.Captions)
            .HasForeignKey(c => c.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        // ✅ One PostImage row per post — Urls is JSON array
        modelBuilder.Entity<PostImage>()
            .HasOne(pi => pi.Post)
            .WithOne(p => p.Media)
            .HasForeignKey<PostImage>(pi => pi.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PostImage>()
            .Property(pi => pi.Urls)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<Client>()
            .HasOne(c => c.User)
            .WithMany(u => u.Clients)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Post>()
            .HasOne(p => p.Client)
            .WithMany(c => c.Posts)
            .HasForeignKey(p => p.ClientId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Topic>()
            .HasOne(t => t.Client)
            .WithMany(c => c.Topics)
            .HasForeignKey(t => t.ClientId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SocialAccount>()
            .HasOne(s => s.Client)
            .WithMany(c => c.SocialAccounts)
            .HasForeignKey(s => s.ClientId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SocialAccount>()
            .HasOne(s => s.Platform)
            .WithMany(p => p.SocialAccounts)
            .HasForeignKey(s => s.PlatformId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SheetRow>()
            .HasOne(sr => sr.Client)
            .WithMany()
            .HasForeignKey(sr => sr.ClientId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SheetRow>()
            .HasOne(sr => sr.Post)
            .WithMany()
            .HasForeignKey(sr => sr.PostId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SheetRow>()
            .HasIndex(sr => new { sr.ClientId, sr.RowKey })
            .IsUnique();

        modelBuilder.Entity<TrelloBrief>()
            .HasOne(b => b.Client)
            .WithMany()
            .HasForeignKey(b => b.ClientId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<TrelloBrief>()
            .HasIndex(b => b.CardId)
            .IsUnique();

        modelBuilder.Entity<PostAssignment>()
            .HasOne(a => a.AssignedTo)
            .WithMany()
            .HasForeignKey(a => a.AssignedToUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PostAssignment>()
            .HasOne(a => a.AssignedBy)
            .WithMany()
            .HasForeignKey(a => a.AssignedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PostAssignment>()
            .HasOne(a => a.Client)
            .WithMany()
            .HasForeignKey(a => a.ClientId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PostAssignment>()
            .HasOne(a => a.Parent)
            .WithMany()
            .HasForeignKey(a => a.ParentAssignmentId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // Seed platforms
        modelBuilder.Entity<Platform>().HasData(
            new Platform { Id = 1, Name = "instagram", DisplayName = "Instagram" },
            new Platform { Id = 2, Name = "facebook", DisplayName = "Facebook" },
            new Platform { Id = 3, Name = "linkedin", DisplayName = "LinkedIn" },
            new Platform { Id = 4, Name = "tiktok", DisplayName = "TikTok" },
            new Platform { Id = 5, Name = "twitter", DisplayName = "Twitter/X" },
            new Platform { Id = 6, Name = "threads", DisplayName = "Threads" }
        );
    }



}