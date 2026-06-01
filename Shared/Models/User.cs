
using System.ComponentModel.DataAnnotations;

namespace AutoGenerate.Shared.Models;

public class User
{
    public int Id { get; set; }

    public string? Name { get; set; }

    [Required]
    public string Email { get; set; } = "";

    [Required]
    public string PasswordHash { get; set; } = "";

    public string Role { get; set; } = "Editor";

    public bool IsVerified { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Post> Posts { get; set; } = new List<Post>();
    public ICollection<Client> Clients { get; set; } = new List<Client>();
}