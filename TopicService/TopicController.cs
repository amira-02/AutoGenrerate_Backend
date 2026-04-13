using AutoGenerate.Caption.Dto;  // ← ajoute cette ligne
using AutoGenerate.Shared.Data;
using AutoGenerate.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TopicsController : ControllerBase
{
    private readonly AppDbContext _db;
    public TopicsController(AppDbContext db) { _db = db; }

    private async Task<User?> GetCurrentUserAsync()
    {
        var email = User.FindFirst(ClaimTypes.Name)?.Value
                 ?? User.FindFirst("email")?.Value;
        if (email == null) return null;
        return await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
    }

    // GET api/topics
    [HttpGet]
    public async Task<IActionResult> GetTopics()
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var topics = await _db.Topics
            .Where(t => t.UserId == user.Id)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                id = t.Id,
                name = t.Name,
                description = t.Description,
                platform = t.Platform,
                createdAt = t.CreatedAt,
                postCount = t.Posts.Count,
            })
            .ToListAsync();

        return Ok(topics);
    }

    // GET api/topics/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetTopic(int id)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var topic = await _db.Topics
            .Where(t => t.Id == id && t.UserId == user.Id)
            .Include(t => t.Posts)
                .ThenInclude(p => p.Captions)
            .Include(t => t.Posts)
                .ThenInclude(p => p.Images)
            .Select(t => new
            {
                id = t.Id,
                name = t.Name,
                description = t.Description,
                platform = t.Platform,
                createdAt = t.CreatedAt,
                posts = t.Posts.OrderByDescending(p => p.CreatedAt).Select(p => new
                {
                    id = p.Id,
                    status = p.Status.ToString().ToLower(),
                    scheduledAt = p.ScheduledAt,
                    publishedAt = p.PublishedAt,
                    createdAt = p.CreatedAt,
                    caption = p.Captions
                        .Where(c => c.IsSelected)
                        .Select(c => c.Content)
                        .FirstOrDefault(),
                    tone = p.Captions
                        .Where(c => c.IsSelected)
                        .Select(c => c.ToneOfVoice)
                        .FirstOrDefault(),
                    imageCount = p.Images.Count,
                    imageUrl = p.Images
                        .OrderBy(i => i.Order)
                        .Select(i => i.Url)
                        .FirstOrDefault(),
                })
            })
            .FirstOrDefaultAsync();

        if (topic == null) return NotFound();
        return Ok(topic);
    }

    // POST api/topics
    [HttpPost]
    public async Task<IActionResult> CreateTopic([FromBody] CreateTopicDto dto)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "Name is required" });

        var topic = new Topic
        {
            UserId = user.Id,
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            Platform = dto.Platform?.Trim() ?? "",
            CreatedAt = DateTime.UtcNow,
        };

        _db.Topics.Add(topic);
        await _db.SaveChangesAsync();

        return Ok(new { id = topic.Id, name = topic.Name, description = topic.Description, platform = topic.Platform, createdAt = topic.CreatedAt, postCount = 0 });
    }

    // DELETE api/topics/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTopic(int id)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var topic = await _db.Topics.FirstOrDefaultAsync(t => t.Id == id && t.UserId == user.Id);
        if (topic == null) return NotFound();

        _db.Topics.Remove(topic);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Deleted" });
    }
}