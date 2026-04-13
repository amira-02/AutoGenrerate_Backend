using AutoGenerate.Shared.Models;

namespace AutoGenerate.CaptionService.Models;

public class Caption
{
    public int Id { get; set; }
    public int PostId { get; set; }

    public string? Content { get; set; }
    public string ToneOfVoice { get; set; } = "Casual";
    public string CaptionLength { get; set; } = "Medium";
    public string? Hashtags { get; set; }
    public string? Platforms { get; set; }   // stored as JSON string e.g. "[\"instagram\",\"tiktok\"]"

    public string GeneratedBy { get; set; } = "ai";  // "ai" | "manual" | "ai_edited"
    public int Version { get; set; } = 1;
    public bool IsSelected { get; set; } = true;

    // Navigation
    public Post? Post { get; set; }
}