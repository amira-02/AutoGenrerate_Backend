using Microsoft.EntityFrameworkCore;
using AutoPost.Api.Models;

namespace AutoPost.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<OtpCode> OtpCodes { get; set; }
    public DbSet<Post> Posts { get; set; }
    public DbSet<SocialAccount> SocialAccounts { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Email unique
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        // User → Posts
        modelBuilder.Entity<Post>()
            .HasOne(p => p.User)
            .WithMany(u => u.Posts)
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // User → SocialAccounts
        modelBuilder.Entity<SocialAccount>()
            .HasOne(s => s.User)
            .WithMany(u => u.SocialAccounts)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}