using System.ComponentModel.DataAnnotations;

public class User
{
    public int Id { get; set; }

    [Required]
    public string Email { get; set; }

    [Required]
    public string PasswordHash { get; set; }

    public string Role { get; set; } = "Editor";

    public bool IsVerified { get; set; } = false;
}