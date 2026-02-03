using Microsoft.EntityFrameworkCore;
using System;

namespace AtemDirector.Server.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<AdminUser> AdminUsers { get; set; }
        public DbSet<MediaItem> MediaItems { get; set; }
        public DbSet<Category> Categories { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            // Configure Category-MediaItem relationship
            modelBuilder.Entity<MediaItem>()
                .HasOne(m => m.Category)
                .WithMany()
                .HasForeignKey(m => m.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }

    public class AdminUser
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty; // In production, use hashing! keeping simple as requested.
    }

    public class MediaItem
    {
        public int Id { get; set; }
        public string Type { get; set; } = "Image"; // Image, Video, Youtube
        public string Url { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        
        // Category relationship (nullable - items can be "General" with no category)
        public int? CategoryId { get; set; }
        public Category? Category { get; set; }
    }
}
