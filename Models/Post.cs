namespace AutoPost.Api.Models;

public enum PostStatus { Draft, Scheduled, Published, Failed }

public class Post
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public PostStatus Status { get; set; } = PostStatus.Draft;
    public string Platform { get; set; } = string.Empty;
    public DateTime? ScheduledAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User User { get; set; } = null!;
}