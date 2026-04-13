using AutoGenerate.Auth;
using AutoGenerate.SocialMedia;
using AutoGenerate.Shared.Models;
using Microsoft.EntityFrameworkCore;

// ← PAS de "using AutoGenerate.CaptionService.Models;" ici

namespace AutoGenerate.Shared.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<OtpCode> OtpCodes { get; set; }
    public DbSet<Post> Posts { get; set; }
    public DbSet<Topic> Topics { get; set; }
    public DbSet<Image> PostImages { get; set; }
    public DbSet<SocialAccount> SocialAccounts { get; set; }
    public DbSet<AutoGenerate.CaptionService.Models.Caption> Captions { get; set; }  // ← nom complet

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

        modelBuilder.Entity<AutoGenerate.CaptionService.Models.Caption>()  // ← nom complet
            .HasOne(c => c.Post)
            .WithMany(p => p.Captions)
            .HasForeignKey(c => c.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Image>()
            .HasOne(i => i.Post)
            .WithMany(p => p.Images)
            .HasForeignKey(i => i.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SocialAccount>()
            .HasOne(s => s.User)
            .WithMany(u => u.SocialAccounts)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}