using AutoGenerate.Shared.Models;

namespace AutoGenerate.SocialMedia;

public class SocialAccount
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
    public bool IsConnected { get; set; } = false;
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User User { get; set; } = null!;
}