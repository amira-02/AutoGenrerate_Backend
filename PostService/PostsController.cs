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

    public PostsController(AppDbContext db) => _db = db;

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private async Task<User?> GetCurrentUserAsync()
    {
        var email = User.FindFirst(ClaimTypes.Name)?.Value
                 ?? User.FindFirst("email")?.Value;
        if (email == null) return null;
        return await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
    }

    private static PostStatus ParseStatusOrDefault(string? raw, PostStatus fallback = PostStatus.Draft)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        var n = raw.Replace("-", "").Replace("_", "").ToLowerInvariant();
        return n switch
        {
            "draft" => PostStatus.Draft,
            "inreview" => PostStatus.InReview,
            "approved" => PostStatus.Approved,
            "scheduled" => PostStatus.Scheduled,
            "published" => PostStatus.Published,
            "failed" => PostStatus.Failed,
            _ => fallback
        };
    }

    private static List<string> ParsePlatforms(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new();
        try { return JsonSerializer.Deserialize<List<string>>(raw) ?? new(); }
        catch { return new(); }
    }

    private static object MapPostResponse(Post p)
    {
        var sel = p.Captions.FirstOrDefault(c => c.IsSelected);
        var mediaUrls = p.GetMediaUrls();
        return new
        {
            id = p.Id,
            topicId = p.TopicId,
            topicName = p.Topic?.Name ?? "",
            status = p.Status.ToString().ToLowerInvariant(),
            scheduledAt = p.ScheduledAt,
            createdAt = p.CreatedAt,
            caption = sel?.Content,
            tone = sel?.ToneOfVoice,
            hashtags = sel?.Hashtags,
            platforms = ParsePlatforms(sel?.Platforms),
            imageUrl = mediaUrls.FirstOrDefault(),
            imageUrls = mediaUrls,
        };
    }

    // ─── GET /api/posts ───────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetPosts()
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var posts = await _db.Posts
            .Where(p => p.UserId == user.Id)
            .Include(p => p.Topic)
            .Include(p => p.Captions)
            .Include(p => p.Media)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        return Ok(posts.Select(p => {
            var sel = p.Captions.FirstOrDefault(c => c.IsSelected);
            var mediaUrls = p.GetMediaUrls();
            return new
            {
                id = p.Id,
                topicId = p.TopicId,
                topicName = p.Topic?.Name ?? "",
                status = p.Status.ToString().ToLower(),
                scheduledAt = p.ScheduledAt,
                createdAt = p.CreatedAt,
                caption = sel?.Content,
                tone = sel?.ToneOfVoice,
                hashtags = sel?.Hashtags,
                platforms = ParsePlatforms(sel?.Platforms),
                imageUrl = mediaUrls.FirstOrDefault(),
                imageUrls = mediaUrls,
            };
        }));
    }

    // ─── POST /api/posts/chat ─────────────────────────────────────────────────

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatDto dto)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var topic = await _db.Topics
            .FirstOrDefaultAsync(t => t.Id == dto.TopicId && t.UserId == user.Id);
        if (topic == null) return NotFound(new { message = "Topic not found" });

        var client = new HttpClient();
        var response = await client.PostAsJsonAsync(
            "http://localhost:5678/webhook/chatbot",
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

        return Content(await response.Content.ReadAsStringAsync(), "application/json");
    }

    // ─── POST /api/posts/save ─────────────────────────────────────────────────

    [HttpPost("save")]
    [AllowAnonymous]
    public async Task<IActionResult> SavePost([FromBody] SaveCaptionDto dto)
    {
        var email = User.FindFirst(ClaimTypes.Name)?.Value
                 ?? User.FindFirst("email")?.Value;

        int resolvedUserId = dto.UserId;
        if (email != null)
        {
            var u = await _db.Users.FirstOrDefaultAsync(x => x.Email == email);
            if (u != null) resolvedUserId = u.Id;
        }
        if (resolvedUserId == 0) return BadRequest(new { message = "User not found" });

        int resolvedTopicId = dto.TopicId;
        if (resolvedTopicId == 0 && !string.IsNullOrWhiteSpace(dto.TopicName))
        {
            var topic = await _db.Topics
                .FirstOrDefaultAsync(t => t.UserId == resolvedUserId && t.Name == dto.TopicName);
            if (topic == null)
            {
                topic = new Topic { UserId = resolvedUserId, Name = dto.TopicName.Trim(), CreatedAt = DateTime.UtcNow };
                _db.Topics.Add(topic);
                await _db.SaveChangesAsync();
            }
            resolvedTopicId = topic.Id;
        }
        if (resolvedTopicId == 0) return BadRequest(new { message = "topicId or topicName is required" });

        var requestedStatus = ParseStatusOrDefault(dto.Status, PostStatus.Draft);
        var scheduledAt = dto.ScheduledAt ?? dto.ScheduledFor;

        if (requestedStatus == PostStatus.Scheduled && !scheduledAt.HasValue)
            requestedStatus = PostStatus.Approved;
        if (scheduledAt.HasValue && requestedStatus != PostStatus.Scheduled)
            requestedStatus = PostStatus.Scheduled;

        // ✅ Build media URLs — ["url1","url2","url3"]
        var allUrls = new List<string>();
        if (dto.ImageUrls?.Count > 0)
            allUrls.AddRange(dto.ImageUrls.Where(u => !string.IsNullOrWhiteSpace(u)));
        else if (!string.IsNullOrWhiteSpace(dto.ImageUrl))
            allUrls.Add(dto.ImageUrl);
        if (!string.IsNullOrWhiteSpace(dto.VideoUrl) && !allUrls.Contains(dto.VideoUrl))
            allUrls.Add(dto.VideoUrl);

        var post = new Post
        {
            TopicId = resolvedTopicId,
            UserId = resolvedUserId,
            Status = requestedStatus,
            ScheduledAt = scheduledAt,
            CreatedAt = DateTime.UtcNow,
        };

        if (allUrls.Count > 0)
        {
            post.Media = new PostImage();
            post.Media.SetUrls(allUrls);
        }

        post.Captions.Add(new Caption
        {
            Content = dto.Caption,
            ToneOfVoice = dto.Tone ?? "Casual",
            CaptionLength = dto.CaptionLength ?? "Medium",
            Hashtags = dto.Hashtags,
            Platforms = dto.Platforms != null ? JsonSerializer.Serialize(dto.Platforms) : null,
            GeneratedBy = "ai",
            IsSelected = true,
        });

        _db.Posts.Add(post);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Post saved", postId = post.Id, post = MapPostResponse(post) });
    }

    // ─── PATCH /api/posts/{id}/params ────────────────────────────────────────

    [HttpPatch("{id}/params")]
    public async Task<IActionResult> UpdateParams(int id, [FromBody] UpdatePostParamsDto dto)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .Include(p => p.Captions).Include(p => p.Topic)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == user.Id);
        if (post == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(dto.Status))
        {
            var nextStatus = ParseStatusOrDefault(dto.Status, post.Status);
            if (nextStatus == PostStatus.InReview || nextStatus == PostStatus.Draft)
                post.ScheduledAt = null;
            else if (nextStatus == PostStatus.Scheduled)
            {
                var newDate = dto.ScheduledAt ?? post.ScheduledAt;
                if (!newDate.HasValue)
                    return BadRequest(new { message = "scheduledAt is required to schedule a post." });
                post.ScheduledAt = newDate;
            }
            else if (dto.ScheduledAt.HasValue)
                post.ScheduledAt = dto.ScheduledAt.Value;
            post.Status = nextStatus;
        }
        else if (dto.ScheduledAt.HasValue)
            post.ScheduledAt = dto.ScheduledAt.Value;

        var selected = post.Captions.FirstOrDefault(c => c.IsSelected);
        if (selected != null)
        {
            if (dto.Tone != null) selected.ToneOfVoice = dto.Tone;
            if (dto.Hashtags != null) selected.Hashtags = dto.Hashtags;
            if (dto.Platforms != null) selected.Platforms = JsonSerializer.Serialize(dto.Platforms);
        }

        await _db.SaveChangesAsync();
        return Ok(MapPostResponse(post));
    }

    // ─── PATCH /api/posts/{id}/caption ───────────────────────────────────────

    [HttpPatch("{id}/caption")]
    public async Task<IActionResult> UpdateCaption(int id, [FromBody] SaveCaptionDto dto)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .Include(p => p.Topic).Include(p => p.Captions)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == user.Id);
        if (post == null) return NotFound();

        var selected = post.Captions.FirstOrDefault(c => c.IsSelected);
        if (selected == null)
        {
            selected = new Caption { IsSelected = true, GeneratedBy = "manual" };
            post.Captions.Add(selected);
        }

        selected.Content = dto.Caption ?? selected.Content;
        if (!string.IsNullOrWhiteSpace(dto.Tone)) selected.ToneOfVoice = dto.Tone;
        if (dto.Hashtags != null) selected.Hashtags = dto.Hashtags;
        if (dto.Platforms != null) selected.Platforms = JsonSerializer.Serialize(dto.Platforms);

        post.Status = PostStatus.InReview;
        post.ScheduledAt = null;

        await _db.SaveChangesAsync();
        return Ok(MapPostResponse(post));
    }

    // ─── PATCH /api/posts/{id}/media ─────────────────────────────────────────

    [HttpPatch("{id}/media")]
    public async Task<IActionResult> UpdateMedia(int id, [FromBody] UpdateMediaDto dto)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .Include(p => p.Topic).Include(p => p.Captions)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == user.Id);
        if (post == null) return NotFound();

        var urls = (dto.ImageUrls ?? new()).Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
        if (!string.IsNullOrWhiteSpace(dto.VideoUrl) && !urls.Contains(dto.VideoUrl))
            urls.Add(dto.VideoUrl);

        if (post.Media == null)
        {
            post.Media = new PostImage { PostId = post.Id };
            _db.PostImages.Add(post.Media);
        }
        post.Media.SetUrls(urls);
        post.Status = PostStatus.InReview;
        post.ScheduledAt = null;

        await _db.SaveChangesAsync();
        return Ok(MapPostResponse(post));
    }

    // ─── POST /api/posts/{id}/schedule ───────────────────────────────────────

    [HttpPost("{id}/schedule")]
    public async Task<IActionResult> SchedulePost(int id, [FromBody] AutoGenerate.PostService.DTOs.ScheduleDto dto)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .Include(p => p.Topic).Include(p => p.Captions)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == user.Id);
        if (post == null) return NotFound();

        if (post.Status == PostStatus.Published)
            return BadRequest(new { message = "Cannot reschedule a published post." });

        post.ScheduledAt = dto.ScheduledAt;
        post.Status = PostStatus.Scheduled;
        await _db.SaveChangesAsync();
        return Ok(MapPostResponse(post));
    }

    // ─── POST /api/posts/{id}/publish ────────────────────────────────────────

    [HttpPost("{id}/publish")]
    public async Task<IActionResult> PublishPost(int id)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .Include(p => p.Topic).Include(p => p.Captions)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == user.Id);
        if (post == null) return NotFound();

        post.Status = PostStatus.Published;
        post.PublishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(MapPostResponse(post));
    }

    // ─── DELETE /api/posts/{id} ──────────────────────────────────────────────

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePost(int id)
    {
        var post = await _db.Posts
            .Include(p => p.Captions)
            .Include(p => p.Media)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (post == null) return NotFound();

        // ── Détache les ExternalTasks liées ──────────────────────────────
        var externalTasks = await _db.ExternalTasks
            .Where(t => t.PostId == id)
            .ToListAsync();
        foreach (var et in externalTasks)
            et.PostId = null;
        await _db.SaveChangesAsync();

        // ── Supprime captions + media + post ─────────────────────────────
        if (post.Media != null) _db.PostImages.Remove(post.Media);
        if (post.Captions != null) _db.Captions.RemoveRange(post.Captions);

        _db.Posts.Remove(post);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    // ─── GET /api/posts/due ──────────────────────────────────────────────────

    [HttpGet("due")]
    [AllowAnonymous]
    public async Task<IActionResult> GetDuePosts()
    {
        var now = DateTime.Now;
        var posts = await _db.Posts
            .Include(p => p.Captions).Include(p => p.Topic).Include(p => p.Media)
            .Where(p => p.Status == PostStatus.Scheduled
                     && p.ScheduledAt.HasValue
                     && p.ScheduledAt.Value <= now)
            .ToListAsync();

        return Ok(posts.Select(p => {
            var sel = p.Captions.FirstOrDefault(c => c.IsSelected);
            var mediaUrls = p.GetMediaUrls();
            return new
            {
                id = p.Id,
                caption = sel?.Content,
                hashtags = sel?.Hashtags,
                platforms = ParsePlatforms(sel?.Platforms),
                imageUrl = mediaUrls.FirstOrDefault(),
                imageUrls = mediaUrls,   // ✅ all URLs for n8n
                topicName = p.Topic?.Name ?? "",
            };
        }));
    }

    // ─── PATCH /api/posts/{id}/status ────────────────────────────────────────

    [HttpPatch("{id}/status")]
    [AllowAnonymous]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateStatusDto dto)
    {
        var post = await _db.Posts.FindAsync(id);
        if (post == null) return NotFound();

        if (Enum.TryParse<PostStatus>(dto.Status, out var status))
            post.Status = status;

        await _db.SaveChangesAsync();
        return Ok(new { message = "Status updated" });
    }
}