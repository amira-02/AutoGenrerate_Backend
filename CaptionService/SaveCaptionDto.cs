namespace AutoGenerate.Caption.Dto;

public class SaveCaptionDto
{
    public int UserId { get; set; }
    public int TopicId { get; set; }
    public string? TopicName { get; set; }
    public string? Caption { get; set; }
    public string? Tone { get; set; }
    public string? CaptionLength { get; set; }
    public string? Hashtags { get; set; }
    public List<string>? Platforms { get; set; }
    public string? Status { get; set; }
    public string? ImageUrl { get; set; }       // first/main image
    public List<string>? ImageUrls { get; set; }     // ✅ multiple images
    public string? VideoUrl { get; set; }        // ✅ video
    public DateTime? ScheduledAt { get; set; }
    public DateTime? ScheduledFor { get; set; }
}