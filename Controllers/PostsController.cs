//using AutoPost.Api.Data;
//using AutoPost.Api.DTOs;
//using AutoPost.Api.Models;
//using Microsoft.AspNetCore.Authorization;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.EntityFrameworkCore;
//using System.Collections.Concurrent;
//using System.Security.Claims;

//[ApiController]
//[Route("api/[controller]")]
//[Authorize]
//public class PostsController : ControllerBase
//{
//    private readonly AppDbContext _db;
//    private readonly IHttpClientFactory _httpClientFactory;
//    private readonly IConfiguration _config;

//    // ✅ Stocke les "promesses" en attente par postId
//    private static readonly ConcurrentDictionary<int, TaskCompletionSource<Post>> _pending = new();

//    public PostsController(AppDbContext db, IHttpClientFactory httpClientFactory, IConfiguration config)
//    {
//        _db = db;
//        _httpClientFactory = httpClientFactory;
//        _config = config;
//    }

//    private async Task<User?> GetCurrentUserAsync()
//    {
//        var email = User.FindFirst(ClaimTypes.Name)?.Value
//                 ?? User.FindFirst("email")?.Value;

//        if (email == null) return null;

//        return await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
//    }

//    private PostResponseDto ToDto(Post post)
//    {
//        var caption = post.Captions.FirstOrDefault(c => c.IsSelected);

//        return new PostResponseDto
//        {
//            Id = post.Id,
//            Topic = post.Topic,
//            Hashtags = post.Hashtags,
//            Caption = caption?.Content,
//            ToneOfVoice = caption?.ToneOfVoice,
//            CaptionLength = caption?.CaptionLength,
//            ImageUrl = post.ImageUrl,
//            Status = post.Status,
//            CreatedAt = post.CreatedAt,
//            ScheduledDate = post.ScheduledDate,
//            UserId = post.UserId
//        };
//    }

//    [HttpPost]
//    public async Task<IActionResult> CreatePost([FromBody] CreatePostDto dto)
//    {
//        var user = await GetCurrentUserAsync();
//        if (user == null) return Unauthorized();

//        var post = new Post
//        {
//            Topic = dto.Topic,
//            Hashtags = dto.Hashtags,
//            Status = "GENERATING",
//            UserId = user.Id
//        };

//        _db.Posts.Add(post);
//        await _db.SaveChangesAsync();

//        var caption = new Caption
//        {
//            PostId = post.Id,
//            ToneOfVoice = dto.ToneOfVoice,
//            CaptionLength = dto.CaptionLength,
//            IsSelected = true
//        };

//        _db.Captions.Add(caption);
//        await _db.SaveChangesAsync();

//        // ✅ Crée la promesse AVANT d'appeler n8n
//        var tcs = new TaskCompletionSource<Post>();
//        _pending[post.Id] = tcs;

//        var client = _httpClientFactory.CreateClient();
//        var n8nUrl = _config["N8n:WebhookUrl"];
//        var baseUrl = _config["App:BaseUrl"];

//        // ✅ Envoie topic, hashtags, tone, length à n8n
//        await client.PostAsJsonAsync(n8nUrl, new
//        {
//            postId = post.Id,
//            captionId = caption.Id,
//            topic = post.Topic,
//            hashtags = post.Hashtags,
//            tone = caption.ToneOfVoice,
//            length = caption.CaptionLength,
//            callbackUrl = $"{baseUrl}/api/posts/{post.Id}/result"
//        });

//        // ✅ Attend que n8n rappelle /result (timeout 2 minutes)
//        var timeoutTask = Task.Delay(TimeSpan.FromMinutes(2));
//        var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

//        _pending.TryRemove(post.Id, out _);

//        if (completedTask == timeoutTask)
//        {
//            // n8n n'a pas répondu à temps
//            post.Status = "ERROR";
//            await _db.SaveChangesAsync();
//            return StatusCode(504, new { message = "n8n did not respond in time." });
//        }

//        // ✅ Retourne le post complet avec caption + image
//        var completedPost = await tcs.Task;
//        return Ok(ToDto(completedPost));
//    }

//    [HttpPut("{id}/result")]
//    [AllowAnonymous]
//    public async Task<IActionResult> SaveResult(int id, [FromBody] PostResultDto dto)
//    {
//        var post = await _db.Posts
//            .Include(p => p.Captions)
//            .FirstOrDefaultAsync(p => p.Id == id);

//        if (post == null) return NotFound();

//        var caption = post.Captions.FirstOrDefault();
//        if (caption == null) return BadRequest("Caption not found");

//        caption.Content = dto.Caption;
//        post.ImageUrl = dto.ImageUrl;
//        post.Status = "PENDING_APPROVAL";

//        await _db.SaveChangesAsync();

//        // ✅ Débloque le CreatePost qui attendait
//        if (_pending.TryGetValue(id, out var tcs))
//        {
//            tcs.SetResult(post);
//        }

//        return Ok(ToDto(post));
//    }

//    [HttpGet("{id}")]
//    public async Task<IActionResult> GetPost(int id)
//    {
//        var user = await GetCurrentUserAsync();
//        if (user == null) return Unauthorized();

//        var post = await _db.Posts
//            .Include(p => p.Captions)
//            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == user.Id);

//        if (post == null) return NotFound();

//        return Ok(ToDto(post));
//    }
//}

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

    // ✅ CHAT BOT — sends message to n8n, returns reply + confirmed + finalCaption
    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatDto dto)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(60);

        try
        {
            var response = await client.PostAsJsonAsync(
                "http://localhost:5678/webhook/chatbot",   // ← production URL (no webhook-test)
                new
                {
                    message = dto.Message ?? "",
                    topic = dto.Topic ?? "",
                    tone = dto.ToneOfVoice ?? "",
                    captionLength = dto.CaptionLength ?? "",
                    hashtags = dto.Hashtags ?? "",
                    platforms = dto.Platforms ?? new List<string>(),
                    fileContent = dto.FileContent ?? "",
                    sessionId = user.Id.ToString()   // ← used by n8n Window Buffer Memory
                }
            );

            if (!response.IsSuccessStatusCode)
                return StatusCode(502, new { message = "Erreur n8n" });

            var result = await response.Content.ReadAsStringAsync();

            // n8n returns: { "reply": "...", "confirmed": bool, "finalCaption": "..." }
            return Content(result, "application/json");
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    // ✅ SAVE CONFIRMED CAPTION — called by n8n when confirmed = true
    [HttpPost("save")]
    [AllowAnonymous]
    public async Task<IActionResult> SaveCaption([FromBody] SaveCaptionDto dto)
    {
        try
        {
            var post = new Post
            {
                Topic = dto.Topic ?? "",
                Hashtags = dto.Hashtags ?? "",
                CreatedAt = DateTime.UtcNow,
                UserId = dto.UserId
            };

            // 👇 ajouter une caption liée au post
            if (!string.IsNullOrWhiteSpace(dto.Caption))
            {
                post.Captions.Add(new Caption
                {
                    Content = dto.Caption,
                    ToneOfVoice = dto.Tone ?? "Casual",
                    CaptionLength = dto.CaptionLength ?? "Medium",
                    IsSelected = true
                });
            }

            _db.Posts.Add(post);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "Caption saved",
                postId = post.Id
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }





}