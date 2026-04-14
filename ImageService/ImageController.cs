using AutoGenerate.ImageService;
using AutoGenerate.Shared.Data;
using AutoGenerate.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

[ApiController]
[Route("api/posts/{postId}/images")]
[Authorize]
[Tags("Images")]
public class ImageController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;

    public ImageController(AppDbContext db, IWebHostEnvironment env, IConfiguration config)
    {
        _db = db;
        _env = env;
        _config = config;
    }

    private async Task<User?> GetCurrentUserAsync()
    {
        var email = User.FindFirst(ClaimTypes.Name)?.Value
                 ?? User.FindFirst("email")?.Value;
        if (email == null) return null;
        return await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET api/posts/{postId}/images
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetImages(int postId)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == postId && p.UserId == user.Id);
        if (post == null) return NotFound(new { message = "Post not found" });

        var images = post.Images.OrderBy(i => i.Order).Select(i => new
        {
            id = i.Id,
            url = i.Url,
            source = i.Source.ToString().ToLower(),
            altText = i.AltText,
            order = i.Order
        });

        return Ok(images);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST api/posts/{postId}/images/upload
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("upload")]
    public async Task<IActionResult> UploadImage(int postId, IFormFile file)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == postId && p.UserId == user.Id);
        if (post == null) return NotFound(new { message = "Post not found" });

        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file provided" });

        var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp", "image/gif" };
        if (!allowedTypes.Contains(file.ContentType.ToLower()))
            return BadRequest(new { message = "Invalid file type. Allowed: jpeg, png, webp, gif" });

        var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "posts");
        Directory.CreateDirectory(uploadsFolder);

        var ext = Path.GetExtension(file.FileName);
        var fileName = $"{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(uploadsFolder, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
            await file.CopyToAsync(stream);

        var imageUrl = $"/uploads/posts/{fileName}";

        var existing = post.Images.Where(i => i.Source == ImageSource.Upload).ToList();
        foreach (var img in existing)
            _db.PostImages.Remove(img);

        post.Images.Add(new Image
        {
            Url = imageUrl,
            Source = ImageSource.Upload,
            AltText = file.FileName,
            Order = 0
        });

        await _db.SaveChangesAsync();
        return Ok(new { message = "Image uploaded", url = imageUrl });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST api/posts/{postId}/images/url
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("url")]
    public async Task<IActionResult> SetImageUrl(int postId, [FromBody] SetImageUrlDto dto)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == postId && p.UserId == user.Id);
        if (post == null) return NotFound(new { message = "Post not found" });

        if (string.IsNullOrWhiteSpace(dto.Url))
            return BadRequest(new { message = "URL is required" });

        var existing = post.Images.Where(i => i.Source == ImageSource.Generated).ToList();
        foreach (var img in existing)
            _db.PostImages.Remove(img);

        post.Images.Add(new Image
        {
            Url = dto.Url,
            Source = ImageSource.Generated,
            AltText = dto.AltText ?? "",
            Order = 0
        });

        await _db.SaveChangesAsync();
        return Ok(new { message = "Image URL set", url = dto.Url });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST api/posts/{postId}/images/generate
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("generate")]
    public async Task<IActionResult> GenerateImage(int postId, [FromBody] GenerateImageDto dto)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .Include(p => p.Topic)
            .Include(p => p.Captions)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == postId && p.UserId == user.Id);

        if (post == null)
            return NotFound(new { message = "Post not found" });

        try
        {
            var caption = post.Captions.FirstOrDefault(c => c.IsSelected)?.Content ?? "";

            var styleMap = new Dictionary<string, string>
            {
                ["realistic"] = "photorealistic, high detail, sharp focus",
                ["cartoon"] = "cartoon style, vibrant colors",
                ["watercolor"] = "watercolor painting, soft brush strokes",
                ["cinematic"] = "cinematic lighting, dramatic film look",
                ["minimalist"] = "minimalist, clean composition",
                ["oil-painting"] = "oil painting style, textured canvas"
            };

            var styleHint = styleMap.GetValueOrDefault(dto.Style ?? "realistic");
            var prompt = $"{dto.Prompt ?? caption}. Topic: {post.Topic?.Name ?? ""}. Style: {styleHint}.";

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(2);

            var apiKey = _config["HuggingFace:ApiKey"];
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            client.DefaultRequestHeaders.Add("Accept", "image/png");

            var hfUrl = "https://router.huggingface.co/hf-inference/models/black-forest-labs/FLUX.1-schnell";
            var hfResponse = await client.PostAsJsonAsync(hfUrl, new { inputs = prompt });
            var contentType = hfResponse.Content.Headers.ContentType?.MediaType ?? "image/png";

            if (!hfResponse.IsSuccessStatusCode)
            {
                var errorBody = await hfResponse.Content.ReadAsStringAsync();
                return StatusCode(502, new { message = "HuggingFace error", details = errorBody });
            }

            if (contentType.Contains("application/json"))
            {
                var jsonBody = await hfResponse.Content.ReadAsStringAsync();
                return StatusCode(502, new { message = "HF returned JSON instead of image", details = jsonBody });
            }

            // ✅ Lire UNE SEULE FOIS
            var imageBytes = await hfResponse.Content.ReadAsByteArrayAsync();

            if (imageBytes == null || imageBytes.Length == 0)
                return StatusCode(500, new { message = "Empty image returned" });

            // ✅ Stocker en base64 dans la colonne Url existante — pas de migration
            var base64 = Convert.ToBase64String(imageBytes);
            var dataUrl = $"data:{contentType};base64,{base64}";

            var existing = post.Images.Where(i => i.Source == ImageSource.Generated).ToList();
            _db.PostImages.RemoveRange(existing);

            post.Images.Add(new Image
            {
                Url = dataUrl,   // ← data:image/png;base64,xxx dans la colonne Url
                Source = ImageSource.Generated,
                AltText = dto.Prompt ?? caption,
                Order = 0
            });

            await _db.SaveChangesAsync();

            return Ok(new { message = "Image generated successfully", url = dataUrl });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Internal error", error = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DELETE api/posts/{postId}/images/{imageId}
    // ─────────────────────────────────────────────────────────────────────────
    [HttpDelete("{imageId}")]
    public async Task<IActionResult> DeleteImage(int postId, int imageId)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == postId && p.UserId == user.Id);
        if (post == null) return NotFound(new { message = "Post not found" });

        var image = post.Images.FirstOrDefault(i => i.Id == imageId);
        if (image == null) return NotFound(new { message = "Image not found" });

        if (image.Source == ImageSource.Upload && !string.IsNullOrEmpty(image.Url))
        {
            var filePath = Path.Combine(_env.WebRootPath, image.Url.TrimStart('/'));
            if (System.IO.File.Exists(filePath))
                System.IO.File.Delete(filePath);
        }

        _db.PostImages.Remove(image);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Image deleted" });
    }
}