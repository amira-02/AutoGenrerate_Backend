namespace AutoGenerate.Shared.Models;

public class SocialAccount
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public int PlatformId { get; set; }
    public string AccessToken { get; set; } = "";
    public string? RefreshToken { get; set; }
    public string? AccountId { get; set; }
    public string? Username { get; set; }
    public string? ProfilePicture { get; set; }
    public int? FollowersCount { get; set; }
    public bool IsConnected { get; set; } = true;
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

    public Client Client { get; set; } = null!;
    public Platform Platform { get; set; } = null!;
}