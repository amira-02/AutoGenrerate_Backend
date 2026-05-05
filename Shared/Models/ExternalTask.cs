namespace AutoGenerate.Shared.Models;

public class ExternalTask
{
    public int Id { get; set; }
    public string TaskId { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime ScheduledAt { get; set; }
    public string CallbackUrl { get; set; } = "";
    public string Status { get; set; } = "pending"; // pending / assigned / published
    public int? PostId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Post? Post { get; set; }
}