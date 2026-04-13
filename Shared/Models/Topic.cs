namespace AutoGenerate.Shared.Models;

public class Topic
{
    public int Id { get; set; }
    public int UserId { get; set; }

    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Platform { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User? User { get; set; }
    public List<Post> Posts { get; set; } = new();
}