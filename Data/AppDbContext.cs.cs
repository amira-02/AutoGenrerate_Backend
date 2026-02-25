using System;
using Microsoft.EntityFrameworkCore;
using AutoPost.Api.Models;

namespace AutoPost.Api.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<OtpCode> OtpCodes { get; set; }
    }
}
