namespace AutoGenerate.CaptionService.Models
{
    public class GenerateCaptionDto
    {
        public string? Message { get; set; }
        public string? Tone { get; set; }
        public string? CaptionLength { get; set; }
        public string? Hashtags { get; set; }
        public List<string>? Platforms { get; set; }
        public string? FileContent { get; set; }
    }
}