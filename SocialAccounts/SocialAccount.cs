namespace AutoGenerate.Shared.Models;

public class SocialAccount
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Platform { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string? AccountId { get; set; }
    public string? Username { get; set; }
    public string? ProfilePicture { get; set; }
    public int? FollowersCount { get; set; }
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}