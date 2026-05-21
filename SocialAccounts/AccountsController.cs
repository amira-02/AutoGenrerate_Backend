using AutoGenerate.Shared.Data;
using AutoGenerate.Shared.Models;
using AutoGenerate.SocialAccounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;
using System.Text.Json;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[Tags("Accounts")]
public class AccountsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _http;

    public AccountsController(AppDbContext db, IConfiguration config, IHttpClientFactory http)
    {
        _db = db;
        _config = config;
        _http = http;
    }

    // Step 1: exchanges a short-lived user token for a 60-day long-lived user token.
    private async Task<string> ExchangeForLongLivedTokenAsync(string shortLivedToken)
    {
        var appId     = _config["Facebook:AppId"];
        var appSecret = _config["Facebook:AppSecret"];
        if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(appSecret))
            return shortLivedToken;

        try
        {
            var client = _http.CreateClient();
            var url = $"https://graph.facebook.com/v20.0/oauth/access_token" +
                      $"?grant_type=fb_exchange_token" +
                      $"&client_id={appId}" +
                      $"&client_secret={appSecret}" +
                      $"&fb_exchange_token={shortLivedToken}";

            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return shortLivedToken;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("access_token", out var tokenProp))
                return tokenProp.GetString() ?? shortLivedToken;
        }
        catch { }

        return shortLivedToken;
    }

    // Step 2 (Facebook only): uses a long-lived user token to fetch a permanent Page Access Token.
    // Page tokens derived from long-lived user tokens never expire.
    private async Task<string> GetPageAccessTokenAsync(string longLivedUserToken, string pageId)
    {
        try
        {
            var client   = _http.CreateClient();
            var url      = $"https://graph.facebook.com/v20.0/{pageId}?fields=access_token&access_token={longLivedUserToken}";
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return longLivedUserToken;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("access_token", out var tokenProp))
                return tokenProp.GetString() ?? longLivedUserToken;
        }
        catch { }

        return longLivedUserToken;
    }

    // Resolves the best possible token for an FB/IG account:
    // - Instagram (1): exchanges user token → 60-day long-lived token
    // - Facebook  (2): exchanges user token → long-lived → fetches permanent page token
    private async Task<string> ResolveTokenAsync(int platformId, string token, string? pageId)
    {
        if (platformId == 1)
            return await ExchangeForLongLivedTokenAsync(token);

        if (platformId == 2)
        {
            var longLived = await ExchangeForLongLivedTokenAsync(token);
            if (!string.IsNullOrEmpty(pageId) && longLived != token)
                return await GetPageAccessTokenAsync(longLived, pageId);
            return longLived;
        }

        return token;
    }

    private async Task<User?> GetCurrentUserAsync()
    {
        var email = User.FindFirst(ClaimTypes.Name)?.Value
                 ?? User.FindFirst("email")?.Value;
        if (email == null) return null;
        return await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
    }

    private async Task<bool> ClientBelongsToUser(int clientId, int userId) =>
        await _db.Clients.AnyAsync(c => c.Id == clientId && c.UserId == userId);

    // GET /api/accounts?clientId=1
    [HttpGet]
    public async Task<IActionResult> GetAccounts([FromQuery] int clientId)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();
        if (!await ClientBelongsToUser(clientId, user.Id)) return Forbid();

        var accounts = await _db.SocialAccounts
            .Where(a => a.ClientId == clientId)
            .Select(a => new {
                a.Id,
                a.ClientId,
                a.PlatformId,
                PlatformName    = a.Platform.Name,
                PlatformDisplay = a.Platform.DisplayName,
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

        if (dto.Id > 0)
        {
            var existing = await _db.SocialAccounts
                .Include(a => a.Client)
                .FirstOrDefaultAsync(a => a.Id == dto.Id && a.Client.UserId == user.Id);
            if (existing == null) return NotFound();

            string? resolvedToken = dto.AccessToken;
            if (!string.IsNullOrEmpty(dto.AccessToken) && (existing.PlatformId == 1 || existing.PlatformId == 2))
            {
                var pageId = dto.AccountId ?? existing.AccountId;
                resolvedToken = await ResolveTokenAsync(existing.PlatformId, dto.AccessToken, pageId);
            }

            if (!string.IsNullOrEmpty(resolvedToken))
                existing.AccessToken = resolvedToken;
            if (dto.RefreshToken != null) existing.RefreshToken = dto.RefreshToken;
            existing.AccountId   = dto.AccountId ?? existing.AccountId;
            existing.Username    = dto.Username  ?? existing.Username;
            existing.IsConnected = true;
            existing.ConnectedAt = DateTime.UtcNow;
        }
        else
        {
            if (!await ClientBelongsToUser(dto.ClientId, user.Id))
                return Forbid();
            var platformExists = await _db.Platforms.AnyAsync(p => p.Id == dto.PlatformId);
            if (!platformExists) return BadRequest(new { message = "Invalid platformId" });

            string? resolvedToken = dto.AccessToken;
            if (!string.IsNullOrEmpty(dto.AccessToken) && (dto.PlatformId == 1 || dto.PlatformId == 2))
                resolvedToken = await ResolveTokenAsync(dto.PlatformId, dto.AccessToken, dto.AccountId);

            _db.SocialAccounts.Add(new SocialAccount
            {
                ClientId     = dto.ClientId,
                PlatformId   = dto.PlatformId,
                AccessToken  = resolvedToken,
                RefreshToken = dto.RefreshToken,
                AccountId    = dto.AccountId,
                Username     = dto.Username,
                IsConnected  = true,
                ConnectedAt  = DateTime.UtcNow,
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
            .Include(a => a.Client)
            .FirstOrDefaultAsync(a => a.Id == id && a.Client.UserId == user.Id);

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
                s.PlatformId,
                Platform     = s.Platform.Name,
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