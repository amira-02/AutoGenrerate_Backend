using AutoGenerate.Shared.Data;
using AutoGenerate.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[Tags("Clients")]
public class ClientsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ClientsController(AppDbContext db) => _db = db;

    private async Task<User?> GetCurrentUserAsync()
    {
        var email = User.FindFirst(ClaimTypes.Name)?.Value ?? User.FindFirst("email")?.Value;
        if (email == null) return null;
        return await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
    }

    // GET /api/clients
    [HttpGet]
    public async Task<IActionResult> GetClients()
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var clients = await _db.Clients
            .Where(c => c.UserId == user.Id)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new {
                c.Id, c.Name, c.Logo, c.Industry, c.Description, c.CreatedAt,
                c.TrelloBoardId,
                c.SheetUrl,
                c.SheetLastSyncAt,
                accountsCount = c.SocialAccounts.Count(a => a.IsConnected),
                postsCount    = c.Posts.Count,
            })
            .ToListAsync();

        return Ok(clients);
    }

    // POST /api/clients
    [HttpPost]
    public async Task<IActionResult> CreateClient([FromBody] ClientDto dto)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var client = new Client
        {
            UserId      = user.Id,
            Name        = dto.Name,
            Logo        = dto.Logo,
            Industry    = dto.Industry,
            Description = dto.Description,
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();

        return Ok(new { client.Id, client.Name, client.Logo, client.Industry, client.Description, client.CreatedAt });
    }

    // PUT /api/clients/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateClient(int id, [FromBody] ClientDto dto)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id && c.UserId == user.Id);
        if (client == null) return NotFound();

        client.Name        = dto.Name;
        client.Logo        = dto.Logo        ?? client.Logo;
        client.Industry    = dto.Industry    ?? client.Industry;
        client.Description = dto.Description ?? client.Description;

        await _db.SaveChangesAsync();
        return Ok(new { client.Id, client.Name, client.Logo, client.Industry, client.Description });
    }

    // PATCH /api/clients/{id}/trello-board
    [HttpPatch("{id}/trello-board")]
    public async Task<IActionResult> SetTrelloBoard(int id, [FromBody] TrelloBoardDto dto)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id && c.UserId == user.Id);
        if (client == null) return NotFound();

        client.TrelloBoardId = string.IsNullOrWhiteSpace(dto.BoardId) ? null : dto.BoardId.Trim();
        await _db.SaveChangesAsync();
        return Ok(new { message = "Trello board linked", trelloBoardId = client.TrelloBoardId });
    }

    // DELETE /api/clients/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteClient(int id)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id && c.UserId == user.Id);
        if (client == null) return NotFound();

        _db.Clients.Remove(client);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Client deleted" });
    }
}

public class ClientDto
{
    public string Name { get; set; } = "";
    public string? Logo { get; set; }
    public string? Industry { get; set; }
    public string? Description { get; set; }
}

public class TrelloBoardDto
{
    public string? BoardId { get; set; }
}
