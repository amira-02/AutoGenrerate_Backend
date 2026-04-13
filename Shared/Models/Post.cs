using AutoGenerate.CaptionService.Models;
using AutoGenerate.Shared.Models;

namespace AutoGenerate.Shared.Models;

public enum PostStatus { Draft, InReview, Approved, Scheduled, Published, Failed }

public class Post
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int TopicId { get; set; }

    public PostStatus Status { get; set; } = PostStatus.Draft;

    public DateTime? ScheduledAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User? User { get; set; }
    public Topic? Topic { get; set; }
    public List<CaptionService.Models.Caption> Captions { get; set; } = new();  // ← nom complet pour éviter l'ambiguïté
    public List<Image> Images { get; set; } = new();
}