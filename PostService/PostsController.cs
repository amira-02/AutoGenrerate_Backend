using AutoGenerate.Caption.Dto;
using AutoGenerate.CaptionService.Models;
using AutoGenerate.Shared.Data;
using AutoGenerate.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[Tags("Posts")]
public class PostsController : ControllerBase
{
    private readonly AppDbContext _db;

    public PostsController(AppDbContext db)
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

    // ✅ GET all posts
    [HttpGet]
    public async Task<IActionResult> GetPosts()
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var posts = await _db.Posts
            .Where(p => p.UserId == user.Id)
            .Include(p => p.Topic)
            .Include(p => p.Captions)
            .Include(p => p.Images)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                id = p.Id,
                topicId = p.TopicId,
                topicName = p.Topic != null ? p.Topic.Name : "",
                status = p.Status.ToString().ToLower(),
                scheduledAt = p.ScheduledAt,
                createdAt = p.CreatedAt,
                caption = p.Captions.Where(c => c.IsSelected).Select(c => c.Content).FirstOrDefault(),
                tone = p.Captions.Where(c => c.IsSelected).Select(c => c.ToneOfVoice).FirstOrDefault(),
                hashtags = p.Captions.Where(c => c.IsSelected).Select(c => c.Hashtags).FirstOrDefault(),
                platforms = p.Captions.Where(c => c.IsSelected).Select(c => c.Platforms).FirstOrDefault(),
                imageUrl = p.Images.OrderBy(i => i.Order).Select(i => i.Url).FirstOrDefault()
            })
            .ToListAsync();

        return Ok(posts);
    }

    // ✅ CHAT → n8n
    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatDto dto)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var topic = await _db.Topics
            .FirstOrDefaultAsync(t => t.Id == dto.TopicId && t.UserId == user.Id);

        if (topic == null)
            return NotFound(new { message = "Topic not found" });

        var client = new HttpClient();

        var response = await client.PostAsJsonAsync(
            "http://localhost:5678/webhook-test/chatbot",
            new
            {
                message = dto.Message ?? "",
                topic = topic.Name,
                tone = dto.ToneOfVoice ?? "",
                captionLength = dto.CaptionLength ?? "",
                hashtags = dto.Hashtags ?? "",
                platforms = dto.Platforms ?? new List<string>(),
                sessionId = user.Id.ToString()
            });

        var result = await response.Content.ReadAsStringAsync();
        return Content(result, "application/json");
    }

    // ✅ SAVE (n8n)
    [HttpPost("save")]
    [AllowAnonymous]
    public async Task<IActionResult> SavePost([FromBody] SaveCaptionDto dto)
    {
        int resolvedTopicId = dto.TopicId;

        if (resolvedTopicId == 0 && !string.IsNullOrWhiteSpace(dto.TopicName))
        {
            var topic = await _db.Topics
                .FirstOrDefaultAsync(t => t.UserId == dto.UserId && t.Name == dto.TopicName);

            if (topic == null)
            {
                topic = new Topic
                {
                    UserId = dto.UserId,
                    Name = dto.TopicName.Trim(),
                    CreatedAt = DateTime.UtcNow
                };
                _db.Topics.Add(topic);
                await _db.SaveChangesAsync();
            }

            resolvedTopicId = topic.Id;
        }

        if (resolvedTopicId == 0)
            return BadRequest(new { message = "topicId or topicName is required" });

        var post = new Post
        {
            TopicId = resolvedTopicId,
            UserId = dto.UserId,
            Status = PostStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };

        post.Captions.Add(new Caption
        {
            Content = dto.Caption,
            ToneOfVoice = dto.Tone ?? "Casual",
            CaptionLength = dto.CaptionLength ?? "Medium",
            Hashtags = dto.Hashtags,
            Platforms = dto.Platforms != null ? JsonSerializer.Serialize(dto.Platforms) : null,
            GeneratedBy = "ai",
            IsSelected = true
        });

        _db.Posts.Add(post);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Post saved", postId = post.Id });
    }

    // ✅ UPDATE PARAMS
    [HttpPatch("{id}/params")]
    public async Task<IActionResult> UpdateParams(int id, [FromBody] UpdatePostParamsDto dto)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .Include(p => p.Captions)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == user.Id);

        if (post == null) return NotFound();

        if (dto.ScheduledAt.HasValue)
            post.ScheduledAt = dto.ScheduledAt.Value;

        var selected = post.Captions.FirstOrDefault(c => c.IsSelected);

        if (selected != null)
        {
            if (dto.Tone != null) selected.ToneOfVoice = dto.Tone;
            if (dto.Hashtags != null) selected.Hashtags = dto.Hashtags;
            if (dto.Platforms != null)
                selected.Platforms = JsonSerializer.Serialize(dto.Platforms);
        }

        await _db.SaveChangesAsync();

        return Ok(new { message = "Params updated" });
    }

    // ✅ DELETE
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePost(int id)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == user.Id);

        if (post == null) return NotFound();

        _db.Posts.Remove(post);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Post deleted" });
    }
}