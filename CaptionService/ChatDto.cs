namespace AutoGenerate.Caption.Dto;

public class ChatDto
{
    public int TopicId { get; set; }
    public string? ToneOfVoice { get; set; }
    public string? CaptionLength { get; set; }
    public string? Hashtags { get; set; }
    public string? FileContent { get; set; }
    public string? Message { get; set; }
    public List<string>? Platforms { get; set; }
    public List<ChatHistoryItem>? History { get; set; }
}