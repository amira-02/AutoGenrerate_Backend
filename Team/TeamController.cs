using AutoGenerate.Auth;
using AutoGenerate.Shared.Data;
using AutoGenerate.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

[ApiController]
[Route("api/team")]
[Authorize]
[Tags("Team")]
public class TeamController : ControllerBase
{
    private readonly AppDbContext _db;

    public TeamController(AppDbContext db) => _db = db;

    // GET /api/team — list all team members (graphiste / redacteur)
    [HttpGet]
    public async Task<IActionResult> GetTeam(CancellationToken ct)
    {
        var members = await _db.Users
            .Where(u => u.Role == "Graphiste" || u.Role == "Redacteur" || u.Role == "ChefVisuel" || u.Role == "ChefRedac")
            .OrderBy(u => u.Role).ThenBy(u => u.Name)
            .Select(u => new { u.Id, u.Name, u.Email, u.Role, u.CreatedAt })
            .ToListAsync(ct);

        return Ok(members);
    }

    // POST /api/team — create a team member
    [HttpPost]
    public async Task<IActionResult> CreateMember([FromBody] CreateMemberDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest(new { message = "Email et mot de passe requis" });

        if (dto.Role != "Graphiste" && dto.Role != "Redacteur" && dto.Role != "ChefVisuel" && dto.Role != "ChefRedac")
            return BadRequest(new { message = "Rôle invalide (ChefVisuel, ChefRedac, Graphiste ou Redacteur)" });

        if (await _db.Users.AnyAsync(u => u.Email == dto.Email.Trim(), ct))
            return Conflict(new { message = "Cet email est déjà utilisé" });

        var user = new User
        {
            Name         = dto.Name?.Trim(),
            Email        = dto.Email.Trim().ToLower(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            Role         = dto.Role,
            IsVerified   = true,
            CreatedAt    = DateTime.UtcNow,
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        return Ok(new { user.Id, user.Name, user.Email, user.Role });
    }

    // DELETE /api/team/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteMember(int id, CancellationToken ct)
    {
        var user = await _db.Users.FindAsync(new object[] { id }, ct);
        if (user == null) return NotFound();
        if (user.Role != "Graphiste" && user.Role != "Redacteur" && user.Role != "ChefVisuel" && user.Role != "ChefRedac")
            return BadRequest(new { message = "Impossible de supprimer un compte admin/CM" });

        _db.Users.Remove(user);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // GET /api/team/me — current user info (used by team member views)
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var email = User.FindFirstValue(ClaimTypes.Name);
        var user  = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user == null) return Unauthorized();
        return Ok(new { user.Id, user.Name, user.Email, user.Role });
    }
}

public class CreateMemberDto
{
    public string? Name     { get; set; }
    public string  Email    { get; set; } = "";
    public string  Password { get; set; } = "";
    public string  Role     { get; set; } = ""; // "Graphiste" | "Redacteur"
}
