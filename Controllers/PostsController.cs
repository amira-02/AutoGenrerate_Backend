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

    // ← Dictionnaire qui garde les "promesses" en attente
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
                 ?? User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name")?.Value
                 ?? User.FindFirst("email")?.Value;

        if (email == null) return null;
        return await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
    }

    private PostResponseDto ToDto(Post post) => new PostResponseDto
    {
        Id = post.Id,
        Topic = post.Topic,
        Hashtags = post.Hashtags,
        Caption = post.Caption,
        ImageUrl = post.ImageUrl,
        Status = post.Status,
        CreatedAt = post.CreatedAt,
        ScheduledDate = post.ScheduledDate,
        UserId = post.UserId
    };

    [HttpPost]
    public async Task<IActionResult> CreatePost([FromBody] CreatePostDto dto)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized(new { message = "User not found" });

        var post = new Post
        {
            Topic = dto.Topic,
            Hashtags = dto.Hashtags,
            Status = "GENERATING",
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow
        };

        _db.Posts.Add(post);
        await _db.SaveChangesAsync();

        // ← Crée une "promesse" pour ce post
        var tcs = new TaskCompletionSource<Post>();
        _pending[post.Id] = tcs;

        var client = _httpClientFactory.CreateClient();
        var n8nUrl = _config["N8n:WebhookUrl"];
        var baseUrl = _config["App:BaseUrl"];

        await client.PostAsJsonAsync(n8nUrl, new
        {
            postId = post.Id,
            topic = post.Topic,
            hashtags = post.Hashtags,
            callbackUrl = $"{baseUrl}/api/posts/{post.Id}/result"
        });

        // ← Attend que n8n appelle /result (max 60 secondes)
        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(60)));

        _pending.TryRemove(post.Id, out _);

        if (completedTask != tcs.Task)
            return StatusCode(504, new { message = "n8n took too long to respond" });

        var updatedPost = tcs.Task.Result;
        return Ok(ToDto(updatedPost));
    }

    // ← n8n appelle ce endpoint → débloque la promesse
    [HttpPut("{id}/result")]
    [AllowAnonymous]
    public async Task<IActionResult> SaveResult(int id, [FromBody] PostResultDto dto)
    {
        var post = await _db.Posts.FindAsync(id);
        if (post == null) return NotFound(new { message = $"Post {id} not found" });

        post.Caption = dto.Caption;
        post.ImageUrl = dto.ImageUrl;
        post.Status = "PENDING_APPROVAL";

        await _db.SaveChangesAsync();

        // ← Débloque la promesse → le POST répond au frontend avec la caption
        if (_pending.TryGetValue(id, out var tcs))
            tcs.SetResult(post);

        return Ok(ToDto(post));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetPost(int id)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == user.Id);

        if (post == null) return NotFound(new { message = $"Post {id} not found" });
        return Ok(ToDto(post));
    }

    [HttpGet]
    public async Task<IActionResult> GetMyPosts()
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var posts = await _db.Posts
            .Where(p => p.UserId == user.Id)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        return Ok(posts.Select(ToDto));
    }

    [HttpPut("{id}/draft")]
    public async Task<IActionResult> SaveDraft(int id)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == user.Id);

        if (post == null) return NotFound(new { message = $"Post {id} not found" });
        post.Status = "DRAFT";
        await _db.SaveChangesAsync();
        return Ok(ToDto(post));
    }

    [HttpPut("{id}/approve")]
    public async Task<IActionResult> Approve(int id)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == user.Id);

        if (post == null) return NotFound(new { message = $"Post {id} not found" });
        post.Status = "APPROVED";
        await _db.SaveChangesAsync();
        return Ok(ToDto(post));
    }

    [HttpPut("{id}/schedule")]
    public async Task<IActionResult> Schedule(int id, [FromBody] ScheduleDto dto)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == user.Id);

        if (post == null) return NotFound(new { message = $"Post {id} not found" });
        post.Status = "SCHEDULED";
        post.ScheduledDate = dto.ScheduledAt;
        await _db.SaveChangesAsync();
        return Ok(ToDto(post));
    }
}