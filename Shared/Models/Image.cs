namespace AutoGenerate.Shared.Models;

public enum ImageSource { Upload, Generated, Stock }

public class Image
{
    public int Id { get; set; }
    public int PostId { get; set; }

    public string Url { get; set; } = "";
    public ImageSource Source { get; set; } = ImageSource.Upload;
    public string? PromptUsed { get; set; }
    public string? AltText { get; set; }
    public int Order { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Post? Post { get; set; }
}