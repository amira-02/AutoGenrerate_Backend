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

    private static PostStatus ParseStatusOrDefault(string? raw, PostStatus fallback = PostStatus.Draft)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        var normalized = raw.Replace("-", "", StringComparison.OrdinalIgnoreCase).Replace("_", "", StringComparison.OrdinalIgnoreCase);
        return normalized.ToLowerInvariant() switch
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
        if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(raw) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static object MapPostResponse(Post p)
    {
        var selected = p.Captions.FirstOrDefault(c => c.IsSelected);
        return new
        {
            id = p.Id,
            topicId = p.TopicId,
            topicName = p.Topic?.Name ?? "",
            status = p.Status.ToString().ToLowerInvariant(),
            scheduledAt = p.ScheduledAt,
            createdAt = p.CreatedAt,
            caption = selected?.Content,
            tone = selected?.ToneOfVoice,
            hashtags = selected?.Hashtags,
            platforms = ParsePlatforms(selected?.Platforms),
            imageUrl = p.Images.OrderBy(i => i.Order).Select(i => i.Url).FirstOrDefault()
        };
    }

    private static bool HasCaptionAndImage(Post post)
    {
        var selected = post.Captions.FirstOrDefault(c => c.IsSelected);
        var hasCaption = !string.IsNullOrWhiteSpace(selected?.Content);
        var hasImage = post.Images.Any(i => !string.IsNullOrWhiteSpace(i.Url));
        return hasCaption && hasImage;
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
        // ✅ Récupère l'userId depuis le JWT au lieu de dto.UserId
        var email = User.FindFirst(ClaimTypes.Name)?.Value
                 ?? User.FindFirst("email")?.Value;

        int resolvedUserId = dto.UserId;

        if (email != null)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user != null) resolvedUserId = user.Id;
        }

        if (resolvedUserId == 0)
            return BadRequest(new { message = "Utilisateur introuvable" });

        // Remplace dto.UserId par resolvedUserId partout dans la méthode
        int resolvedTopicId = dto.TopicId;

        if (resolvedTopicId == 0 && !string.IsNullOrWhiteSpace(dto.TopicName))
        {
            var topic = await _db.Topics
                .FirstOrDefaultAsync(t => t.UserId == resolvedUserId && t.Name == dto.TopicName);

            if (topic == null)
            {
                topic = new Topic
                {
                    UserId = resolvedUserId,
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

        var requestedStatus = ParseStatusOrDefault(dto.Status, PostStatus.Draft);
        var scheduledAt = dto.ScheduledAt ?? dto.ScheduledFor;
        if (scheduledAt.HasValue && string.IsNullOrWhiteSpace(dto.Status))
            requestedStatus = PostStatus.Draft;

        var post = new Post
        {
            TopicId = resolvedTopicId,
            UserId = resolvedUserId,   // ✅ userId réel
            Status = requestedStatus,
            ScheduledAt = scheduledAt,
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

        if (!string.IsNullOrWhiteSpace(dto.ImageUrl))
        {
            post.Images.Add(new Image
            {
                Url = dto.ImageUrl!,
                Source = ImageSource.Generated,
                AltText = "Generated image",
                Order = 0
            });
        }

        if (requestedStatus == PostStatus.Approved && !HasCaptionAndImage(post))
            return BadRequest(new { message = "Post needs caption and image before Approved status." });

        if (requestedStatus == PostStatus.Scheduled)
            return BadRequest(new { message = "Post must be Approved before Scheduled status." });

        _db.Posts.Add(post);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Post saved", postId = post.Id, post = MapPostResponse(post) });
    }




    // ✅ UPDATE PARAMS
    [HttpPatch("{id}/params")]
    public async Task<IActionResult> UpdateParams(int id, [FromBody] UpdatePostParamsDto dto)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .Include(p => p.Captions)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == user.Id);

        if (post == null) return NotFound();

        if (dto.ScheduledAt.HasValue)
            post.ScheduledAt = dto.ScheduledAt.Value;

        if (!string.IsNullOrWhiteSpace(dto.Status))
        {
            var nextStatus = ParseStatusOrDefault(dto.Status, post.Status);

            if (nextStatus == PostStatus.Approved && !HasCaptionAndImage(post))
                return BadRequest(new { message = "Post needs caption and image before Approved status." });

            if (nextStatus == PostStatus.Scheduled && post.Status != PostStatus.Approved)
                return BadRequest(new { message = "Post must be Approved before Scheduled status." });

            post.Status = nextStatus;
        }

        var selected = post.Captions.FirstOrDefault(c => c.IsSelected);

        if (selected != null)
        {
            if (dto.Tone != null) selected.ToneOfVoice = dto.Tone;
            if (dto.Hashtags != null) selected.Hashtags = dto.Hashtags;
            if (dto.Platforms != null)
                selected.Platforms = JsonSerializer.Serialize(dto.Platforms);
        }

        await _db.SaveChangesAsync();

        await _db.Entry(post).Reference(p => p.Topic).LoadAsync();
        await _db.Entry(post).Collection(p => p.Images).LoadAsync();
        await _db.Entry(post).Collection(p => p.Captions).LoadAsync();
        return Ok(MapPostResponse(post));
    }

    [HttpPatch("{id}/caption")]
    public async Task<IActionResult> UpdateCaption(int id, [FromBody] SaveCaptionDto dto)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .Include(p => p.Topic)
            .Include(p => p.Captions)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == user.Id);

        if (post == null) return NotFound();

        var selected = post.Captions.FirstOrDefault(c => c.IsSelected);
        if (selected == null)
        {
            selected = new Caption { IsSelected = true, GeneratedBy = dto.Caption == null ? "ai" : "manual" };
            post.Captions.Add(selected);
        }

        selected.Content = dto.Caption ?? selected.Content;
        if (!string.IsNullOrWhiteSpace(dto.Tone)) selected.ToneOfVoice = dto.Tone;
        if (dto.Hashtags != null) selected.Hashtags = dto.Hashtags;
        if (dto.Platforms != null) selected.Platforms = JsonSerializer.Serialize(dto.Platforms);

        await _db.SaveChangesAsync();
        return Ok(MapPostResponse(post));
    }

    [HttpPost("{id}/schedule")]
    public async Task<IActionResult> SchedulePost(int id, [FromBody] AutoGenerate.PostService.DTOs.ScheduleDto dto)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .Include(p => p.Topic)
            .Include(p => p.Captions)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == user.Id);

        if (post == null) return NotFound();

        if (post.Status != PostStatus.Approved)
            return BadRequest(new { message = "Post must be Approved before scheduling." });

        post.ScheduledAt = dto.ScheduledAt;
        post.Status = PostStatus.Scheduled;
        await _db.SaveChangesAsync();
        return Ok(MapPostResponse(post));
    }

    [HttpPost("{id}/publish")]
    public async Task<IActionResult> PublishPost(int id)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .Include(p => p.Topic)
            .Include(p => p.Captions)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == user.Id);

        if (post == null) return NotFound();

        post.Status = PostStatus.Published;
        post.PublishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(MapPostResponse(post));
    }




    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePost(int id)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .Include(p => p.Captions)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == user.Id);

        if (post == null) return NotFound();

        // 🔥 delete captions
        _db.Captions.RemoveRange(post.Captions);

        // 🔥 delete images
        _db.PostImages.RemoveRange(post.Images);

        // 🔥 delete post
        _db.Posts.Remove(post);

        await _db.SaveChangesAsync();

        return Ok(new { message = "Post + relations deleted" });
    }


    [HttpGet("due")]
    [AllowAnonymous]
    public async Task<IActionResult> GetDuePosts()
    {
        var now = DateTime.Now; // ✅ heure locale au lieu de UtcNow
        var posts = await _db.Posts
            .Include(p => p.Captions)
            .Include(p => p.Images)
            .Include(p => p.Topic)
            .Where(p => p.Status == PostStatus.Scheduled
                     && p.ScheduledAt.HasValue
                     && p.ScheduledAt.Value <= now)
            .Select(p => new {
                id = p.Id,
                caption = p.Captions.Where(c => c.IsSelected)
                                    .Select(c => c.Content).FirstOrDefault(),
                hashtags = p.Captions.Where(c => c.IsSelected)
                                     .Select(c => c.Hashtags).FirstOrDefault(),
                platforms = p.Captions.Where(c => c.IsSelected)
                                      .Select(c => c.Platforms).FirstOrDefault(),
                imageUrl = p.Images.OrderBy(i => i.Order)
                                   .Select(i => i.Url).FirstOrDefault(),
                topicName = p.Topic != null ? p.Topic.Name : ""
            })
            .ToListAsync();

        return Ok(posts);
    }

    // PATCH /api/posts/{id}/status
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