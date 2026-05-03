using AutoGenerate.AiService;
using AutoGenerate.Auth;
    using AutoGenerate.Shared.Models;
    using Microsoft.EntityFrameworkCore;

    namespace AutoGenerate.Shared.Data;

    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<OtpCode> OtpCodes { get; set; }
        public DbSet<Post> Posts { get; set; }
        public DbSet<Topic> Topics { get; set; }
        public DbSet<SocialAccount> SocialAccounts { get; set; }
        public DbSet<AutoGenerate.CaptionService.Models.Caption> Captions { get; set; }

    public DbSet<AiRecommendation> AiRecommendations { get; set; }

    public DbSet<PostImage> PostImages { get; set; }
   
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

            modelBuilder.Entity<SocialAccount>()
                .HasOne(s => s.User)
                .WithMany(u => u.SocialAccounts)
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }



    }