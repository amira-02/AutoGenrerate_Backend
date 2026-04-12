namespace AutoGenerate.Shared.Models;

public class Topic
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // relation
    public List<Post> Posts { get; set; } = new();
}