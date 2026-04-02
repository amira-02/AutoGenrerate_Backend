using AutoPost.Api.Data;
using AutoPost.Api.DTOs;
using AutoPost.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PostsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;

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

    // Convertit Post → DTO simple sans boucle infinie
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

        return Ok(ToDto(post));
    }

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