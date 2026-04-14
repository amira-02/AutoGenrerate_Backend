using System.ComponentModel.DataAnnotations.Schema;

namespace AutoGenerate.Shared.Models;

public enum ImageSource { Upload, Generated, Stock }

public class Image
{
    public int Id { get; set; }

    [Column(TypeName = "nvarchar(MAX)")]  
    public string? Url { get; set; }

    public ImageSource Source { get; set; }
    public string? AltText { get; set; }
    public int Order { get; set; }
    public int PostId { get; set; }
    public Post Post { get; set; } = null!;
}