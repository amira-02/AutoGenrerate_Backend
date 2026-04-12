using AutoGenerate.Shared.Models;

namespace AutoGenerate.CaptionService.Models;

public class Caption
{
    public int Id { get; set; }

    public string? Content { get; set; }

    // 👇 your AI parameters
    public string ToneOfVoice { get; set; } = "Casual";
    public string CaptionLength { get; set; } = "Medium";

    public bool IsSelected { get; set; } = true;

    public int PostId { get; set; }
    public Post? Post { get; set; }
}