namespace AutoGenerate.Shared.Models;

public class Client
{
    public int Id { get; set; }
    public int UserId { get; set; }       // admin who manages this client
    public string Name { get; set; } = "";
    public string? Logo { get; set; }
    public string? Industry { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? TrelloBoardId    { get; set; }
    public string? SheetUrl         { get; set; }
    public DateTime? SheetLastSyncAt { get; set; }

    public User User { get; set; } = null!;
    public ICollection<SocialAccount> SocialAccounts { get; set; } = [];
    public ICollection<Post> Posts { get; set; } = [];
    public ICollection<Topic> Topics { get; set; } = [];
}
