using AutoGenerate.CaptionService.Models;
using AutoGenerate.Shared.Data;
using AutoGenerate.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

[ApiController]
[Route("api/posts/{postId}/captions")]
[Authorize]
[Tags("Captions")]
public class CaptionController : ControllerBase
{
    private readonly AppDbContext _db;

    public CaptionController(AppDbContext db)
    {
        _db = db;
    }

    private async Task<User?> GetCurrentUserAsync()
    {
        var email = User.FindFirst(ClaimTypes.Name)?.Value
                 ?? User.FindFirst("email")?.Value;
        if (email == null) return null;
        return await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET api/posts/{postId}/captions
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetCaptions(int postId)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .Include(p => p.Captions)
            .FirstOrDefaultAsync(p => p.Id == postId && p.UserId == user.Id);
        if (post == null) return NotFound(new { message = "Post not found" });

        var captions = post.Captions.Select(c => new
        {
            id = c.Id,
            content = c.Content,
            tone = c.ToneOfVoice,
            length = c.CaptionLength,
            hashtags = c.Hashtags,
            platforms = c.Platforms,
            isSelected = c.IsSelected,
            generatedBy = c.GeneratedBy
        });

        return Ok(captions);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PATCH api/posts/{postId}/captions  — update/replace selected caption
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPatch]
    public async Task<IActionResult> UpdateCaption(int postId, [FromBody] UpdateCaptionDto dto)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .Include(p => p.Captions)
            .FirstOrDefaultAsync(p => p.Id == postId && p.UserId == user.Id);
        if (post == null) return NotFound(new { message = "Post not found" });

        foreach (var c in post.Captions)
            c.IsSelected = false;

        post.Captions.Add(new Caption
        {
            Content = dto.Content,
            ToneOfVoice = dto.Tone ?? "Casual",
            CaptionLength = dto.CaptionLength ?? "Medium",
            Hashtags = dto.Hashtags,
            Platforms = dto.Platforms != null
                ? JsonSerializer.Serialize(dto.Platforms)
                : null,
            GeneratedBy = dto.GeneratedBy ?? "manual",
            IsSelected = true
        });

        await _db.SaveChangesAsync();
        return Ok(new { message = "Caption updated" });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST api/posts/{postId}/captions/generate  — generate via n8n
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("generate")]
    public async Task<IActionResult> GenerateCaption(int postId, [FromBody] GenerateCaptionDto dto)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .Include(p => p.Topic)
            .Include(p => p.Captions)
            .FirstOrDefaultAsync(p => p.Id == postId && p.UserId == user.Id);
        if (post == null) return NotFound(new { message = "Post not found" });

        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(60);

        try
        {
            var response = await client.PostAsJsonAsync(
                "http://localhost:5678/webhook-test/chatbot",
                new
                {
                    message = dto.Message ?? "",
                    topic = post.Topic?.Name ?? "",
                    tone = dto.Tone ?? "Casual",
                    captionLength = dto.CaptionLength ?? "Medium",
                    hashtags = dto.Hashtags ?? "",
                    platforms = dto.Platforms ?? new List<string>(),
                    fileContent = dto.FileContent ?? "",
                    sessionId = user.Id.ToString()
                }
            );

            if (!response.IsSuccessStatusCode)
                return StatusCode(502, new { message = "n8n error" });

            var result = await response.Content.ReadAsStringAsync();
            return Content(result, "application/json");
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DELETE api/posts/{postId}/captions/{captionId}
    // ─────────────────────────────────────────────────────────────────────────
    [HttpDelete("{captionId}")]
    public async Task<IActionResult> DeleteCaption(int postId, int captionId)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .Include(p => p.Captions)
            .FirstOrDefaultAsync(p => p.Id == postId && p.UserId == user.Id);
        if (post == null) return NotFound(new { message = "Post not found" });

        var caption = post.Captions.FirstOrDefault(c => c.Id == captionId);
        if (caption == null) return NotFound(new { message = "Caption not found" });

        _db.Captions.Remove(caption);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Caption deleted" });
    }
}