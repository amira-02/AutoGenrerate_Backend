using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace AutoGenerate.Shared.Models;

public class PostImage
{
    public int Id { get; set; }
    public int PostId { get; set; }

    // ✅ ["url1","url2","url3"] — all media URLs in one column
    [Column(TypeName = "nvarchar(MAX)")]
    public string? Urls { get; set; }

    public string? AltText { get; set; }

    // Navigation
    public Post Post { get; set; } = null!;

    // ── Helpers ───────────────────────────────────────────────────────────────

    public List<string> GetUrls()
    {
        if (string.IsNullOrWhiteSpace(Urls)) return new();
        try { return JsonSerializer.Deserialize<List<string>>(Urls) ?? new(); }
        catch { return new(); }
    }

    public void SetUrls(List<string> urls)
    {
        Urls = urls.Count > 0 ? JsonSerializer.Serialize(urls) : null;
    }

    public string? FirstUrl() => GetUrls().FirstOrDefault();
}