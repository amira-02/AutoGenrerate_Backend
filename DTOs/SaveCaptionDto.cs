namespace AutoPost.Api.DTOs;

public class SaveCaptionDto
{
    public string? Caption { get; set; }
    public string? SessionId { get; set; }

    public string? Topic { get; set; }
    public string? Hashtags { get; set; }

    public string? Tone { get; set; }
    public string? CaptionLength { get; set; }

    public List<string>? Platforms { get; set; }

    public int UserId { get; set; }   // IMPORTANT
}