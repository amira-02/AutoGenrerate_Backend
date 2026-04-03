using AutoPost.Api.Data;
using AutoPost.Api.DTOs;
using AutoPost.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PostsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;

    // ✅ Stocke les "promesses" en attente par postId
    private static readonly ConcurrentDictionary<int, TaskCompletionSource<Post>> _pending = new();

    public PostsController(AppDbContext db, IHttpClientFactory httpClientFactory, IConfiguration config)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _config = config;
    }

    private async Task<User?> GetCurrentUserAsync()
    {
        var email = User.FindFirst(ClaimTypes.Name)?.Value
                 ?? User.FindFirst("email")?.Value;

        if (email == null) return null;

        return await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
    }

    private PostResponseDto ToDto(Post post)
    {
        var caption = post.Captions.FirstOrDefault(c => c.IsSelected);

        return new PostResponseDto
        {
            Id = post.Id,
            Topic = post.Topic,
            Hashtags = post.Hashtags,
            Caption = caption?.Content,
            ToneOfVoice = caption?.ToneOfVoice,
            CaptionLength = caption?.CaptionLength,
            ImageUrl = post.ImageUrl,
            Status = post.Status,
            CreatedAt = post.CreatedAt,
            ScheduledDate = post.ScheduledDate,
            UserId = post.UserId
        };
    }

    [HttpPost]
    public async Task<IActionResult> CreatePost([FromBody] CreatePostDto dto)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = new Post
        {
            Topic = dto.Topic,
            Hashtags = dto.Hashtags,
            Status = "GENERATING",
            UserId = user.Id
        };

        _db.Posts.Add(post);
        await _db.SaveChangesAsync();

        var caption = new Caption
        {
            PostId = post.Id,
            ToneOfVoice = dto.ToneOfVoice,
            CaptionLength = dto.CaptionLength,
            IsSelected = true
        };

        _db.Captions.Add(caption);
        await _db.SaveChangesAsync();

        // ✅ Crée la promesse AVANT d'appeler n8n
        var tcs = new TaskCompletionSource<Post>();
        _pending[post.Id] = tcs;

        var client = _httpClientFactory.CreateClient();
        var n8nUrl = _config["N8n:WebhookUrl"];
        var baseUrl = _config["App:BaseUrl"];

        // ✅ Envoie topic, hashtags, tone, length à n8n
        await client.PostAsJsonAsync(n8nUrl, new
        {
            postId = post.Id,
            captionId = caption.Id,
            topic = post.Topic,
            hashtags = post.Hashtags,
            tone = caption.ToneOfVoice,
            length = caption.CaptionLength,
            callbackUrl = $"{baseUrl}/api/posts/{post.Id}/result"
        });

        // ✅ Attend que n8n rappelle /result (timeout 2 minutes)
        var timeoutTask = Task.Delay(TimeSpan.FromMinutes(2));
        var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

        _pending.TryRemove(post.Id, out _);

        if (completedTask == timeoutTask)
        {
            // n8n n'a pas répondu à temps
            post.Status = "ERROR";
            await _db.SaveChangesAsync();
            return StatusCode(504, new { message = "n8n did not respond in time." });
        }

        // ✅ Retourne le post complet avec caption + image
        var completedPost = await tcs.Task;
        return Ok(ToDto(completedPost));
    }

    [HttpPut("{id}/result")]
    [AllowAnonymous]
    public async Task<IActionResult> SaveResult(int id, [FromBody] PostResultDto dto)
    {
        var post = await _db.Posts
            .Include(p => p.Captions)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (post == null) return NotFound();

        var caption = post.Captions.FirstOrDefault();
        if (caption == null) return BadRequest("Caption not found");

        caption.Content = dto.Caption;
        post.ImageUrl = dto.ImageUrl;
        post.Status = "PENDING_APPROVAL";

        await _db.SaveChangesAsync();

        // ✅ Débloque le CreatePost qui attendait
        if (_pending.TryGetValue(id, out var tcs))
        {
            tcs.SetResult(post);
        }

        return Ok(ToDto(post));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetPost(int id)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .Include(p => p.Captions)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == user.Id);

        if (post == null) return NotFound();

        return Ok(ToDto(post));
    }
}