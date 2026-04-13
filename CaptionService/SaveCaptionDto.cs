namespace AutoGenerate.Caption.Dto;

public class SaveCaptionDto
{
    public string? Caption { get; set; }
    public string? SessionId { get; set; }

    public int TopicId { get; set; }        // depuis le front React
    public string? TopicName { get; set; }  // fallback depuis n8n

    public string? Hashtags { get; set; }
    public string? Tone { get; set; }
    public string? CaptionLength { get; set; }
    public List<string>? Platforms { get; set; }

    public int UserId { get; set; }
}