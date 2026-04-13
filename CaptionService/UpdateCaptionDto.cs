namespace AutoGenerate.CaptionService.Models
{
    public class UpdateCaptionDto
    {
        public string Content { get; set; } = "";
        public string? Tone { get; set; }
        public string? CaptionLength { get; set; }
        public string? Hashtags { get; set; }
        public List<string>? Platforms { get; set; }
        public string? GeneratedBy { get; set; }
    }
}
