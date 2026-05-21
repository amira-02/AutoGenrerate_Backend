namespace AutoGenerate.Shared.Models;

public class Platform
{
    public int Id { get; set; }
    public string Name { get; set; } = "";         // "instagram", "facebook" ...
    public string DisplayName { get; set; } = "";  // "Instagram", "Facebook" ...

    public ICollection<SocialAccount> SocialAccounts { get; set; } = [];
}
