namespace AutoPost.Api.DTOs;

public class PostResponseDto
{
    public int Id { get; set; }

    public string Topic { get; set; } = "";
    public string Hashtags { get; set; } = "";

    public string? Caption { get; set; }
    public string? ImageUrl { get; set; }

    public string Status { get; set; } = "";

    public DateTime CreatedAt { get; set; }
    public DateTime? ScheduledDate { get; set; }

    public int UserId { get; set; }

    // 🔥 caption settings
    public string ToneOfVoice { get; set; } = "";
    public string CaptionLength { get; set; } = "";
}
