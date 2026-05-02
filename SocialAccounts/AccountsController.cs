using AutoGenerate.Shared.Data;
using AutoGenerate.Shared.Models;
using AutoGenerate.SocialAccounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[Tags("Accounts")]
public class AccountsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AccountsController(AppDbContext db) => _db = db;

    private async Task<User?> GetCurrentUserAsync()
    {
        var email = User.FindFirst(ClaimTypes.Name)?.Value
                 ?? User.FindFirst("email")?.Value;
        if (email == null) return null;
        return await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
    }

    // GET /api/accounts
    [HttpGet]
    public async Task<IActionResult> GetAccounts()
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var accounts = await _db.SocialAccounts
            .Where(a => a.UserId == user.Id)
            .Select(a => new {
                a.Id,
                a.Platform,
                a.Username,
                a.ProfilePicture,
                a.FollowersCount,
                a.IsConnected,
                a.ConnectedAt,
                a.AccountId,
                hasToken = !string.IsNullOrEmpty(a.AccessToken),
            })
            .ToListAsync();

        return Ok(accounts);
    }

    // POST /api/accounts
    [HttpPost]
    public async Task<IActionResult> SaveAccount([FromBody] SaveAccountDto dto)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var existing = await _db.SocialAccounts
            .FirstOrDefaultAsync(a => a.UserId == user.Id && a.Platform == dto.Platform);

        if (existing != null)
        {
            existing.AccessToken = dto.AccessToken;
            existing.AccountId = dto.AccountId;
            existing.Username = dto.Username;
            existing.IsConnected = true;
            existing.ConnectedAt = DateTime.UtcNow;
        }
        else
        {
            _db.SocialAccounts.Add(new SocialAccount
            {
                UserId = user.Id,
                Platform = dto.Platform,
                AccessToken = dto.AccessToken,
                AccountId = dto.AccountId,
                Username = dto.Username,
                IsConnected = true,
                ConnectedAt = DateTime.UtcNow,
            });
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = "Account saved" });
    }

    // DELETE /api/accounts/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAccount(int id)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var account = await _db.SocialAccounts
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == user.Id);

        if (account == null) return NotFound();

        _db.SocialAccounts.Remove(account);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Account disconnected" });
    }

    // GET /api/accounts/tokens — pour n8n (sans [Authorize])
    [HttpGet("tokens")]
    [AllowAnonymous]
    public async Task<IActionResult> GetTokens()
    {
        var accounts = await _db.SocialAccounts
            .Where(s => s.IsConnected)
            .Select(s => new {
                s.Id,
                s.Platform,
                s.AccessToken,
                s.RefreshToken,
                s.AccountId,
                s.Username
            })
            .ToListAsync();
        return Ok(accounts);
    }

    // PATCH /api/accounts/tokens/{id} — pour n8n (sans [Authorize])
    [HttpPatch("tokens/{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> UpdateToken(int id, [FromBody] UpdateTokenDto dto)
    {
        var account = await _db.SocialAccounts.FindAsync(id);
        if (account == null) return NotFound();

        account.AccessToken = dto.AccessToken;
        if (dto.RefreshToken != null) account.RefreshToken = dto.RefreshToken;
        account.ConnectedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new { message = "Token updated" });
    }

}