using AutoGenerate.CaptionService.Models;

namespace AutoGenerate.Shared.Models;

public enum PostStatus { Draft, InReview, Approved, Scheduled, Published, Failed }

public class Post
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int ClientId { get; set; }
    public int TopicId { get; set; }

    public PostStatus Status { get; set; } = PostStatus.Draft;

    public DateTime? ScheduledAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User? User { get; set; }
    public Client? Client { get; set; }
    public Topic? Topic { get; set; }
    public List<CaptionService.Models.Caption> Captions { get; set; } = new();
    public string? ExternalTaskId  { get; set; }
    public string? ExternalCallback { get; set; }
    public string? SheetRowKey     { get; set; }  // N° post from the sheet (for sync)
    public string? BriefData       { get; set; }  // JSON blob: format, budget, audience, etc.

    // ✅ One PostImage row per post containing ["url1","url2","url3"]
    public PostImage? Media { get; set; }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public List<string> GetMediaUrls() => Media?.GetUrls() ?? new();
    public string? FirstMediaUrl() => Media?.FirstUrl();
}