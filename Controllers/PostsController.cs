//using AutoGenerate.DTOs;
using AutoPost.Api.DTOs;
using AutoPost.Api.Models;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class PostsController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;

    public PostsController(IHttpClientFactory httpClientFactory, IConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
    }

    [HttpPost]
    public async Task<IActionResult> CreatePost([FromBody] CreatePostDto dto)
    {
        var post = new Post
        {
            Id = new Random().Next(1, 10000),
            Topic = dto.Topic,
            Hashtags = dto.Hashtags,
            Status = "GENERATING"
        };

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

        return Ok(post);
    }

    [HttpPut("{id}/result")]
    public IActionResult SaveResult(int id, [FromBody] PostResultDto dto)
    {
        return Ok(new
        {
            postId = id,
            status = "PENDING_APPROVAL",
            caption = dto.Caption,
            imageUrl = dto.ImageUrl
        });
    }

    [HttpPut("{id}/approve")]
    public IActionResult Approve(int id)
    {
        return Ok(new { postId = id, status = "APPROVED" });
    }

    [HttpPut("{id}/schedule")]
    public IActionResult Schedule(int id, [FromBody] ScheduleDto dto)
    {
        return Ok(new { postId = id, status = "SCHEDULED", scheduledAt = dto.ScheduledAt });
    }
}