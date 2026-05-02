using AutoGenerate.ImageService;
using AutoGenerate.Shared.Data;
using AutoGenerate.Shared.Models;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

[ApiController]
[Authorize]
[Tags("Images")]
public class ImageController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly Cloudinary _cloudinary;

    public ImageController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;

        var account = new Account(
            config["Cloudinary:CloudName"],
            config["Cloudinary:ApiKey"],
            config["Cloudinary:ApiSecret"]
        );
        _cloudinary = new Cloudinary(account);
        _cloudinary.Api.Secure = true;
    }

    private async Task<User?> GetCurrentUserAsync()
    {
        var email = User.FindFirst(ClaimTypes.Name)?.Value
                 ?? User.FindFirst("email")?.Value;
        if (email == null) return null;
        return await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
    }

    // ── Cloudinary helpers ────────────────────────────────────────────────────

    private async Task<string> UploadBytesAsync(byte[] bytes, string fileName)
    {
        using var ms = new MemoryStream(bytes);
        var result = await _cloudinary.UploadAsync(new ImageUploadParams
        {
            File = new FileDescription(fileName, ms),
            PublicId = $"autogenerate/posts/{Guid.NewGuid()}",
            Overwrite = true,
            Transformation = new Transformation().Quality("auto").FetchFormat("auto"),
        });
        if (result.Error != null) throw new Exception(result.Error.Message);
        return result.SecureUrl.ToString();
    }

    private async Task<string> UploadFormFileAsync(IFormFile file)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;
        var result = await _cloudinary.UploadAsync(new ImageUploadParams
        {
            File = new FileDescription(file.FileName, ms),
            PublicId = $"autogenerate/posts/{Guid.NewGuid()}",
            Overwrite = true,
            Transformation = new Transformation().Quality("auto").FetchFormat("auto"),
        });
        if (result.Error != null) throw new Exception(result.Error.Message);
        return result.SecureUrl.ToString();
    }

    private async Task DeleteFromCloudinaryAsync(string url)
    {
        if (!url.Contains("cloudinary.com")) return;
        try
        {
            var segments = new Uri(url).AbsolutePath.Split('/');
            var uploadIdx = Array.IndexOf(segments, "upload");
            if (uploadIdx < 0) return;
            var afterUpload = segments.Skip(uploadIdx + 1).ToArray();
            var start = afterUpload[0].StartsWith("v") ? 1 : 0;
            var publicIdExt = string.Join("/", afterUpload.Skip(start));
            var publicId = Path.GetFileNameWithoutExtension(publicIdExt);
            var folder = string.Join("/", afterUpload.Skip(start).Take(afterUpload.Length - start - 1));
            var fullPublicId = string.IsNullOrEmpty(folder) ? publicId : $"{folder}/{publicId}";
            await _cloudinary.DestroyAsync(new DeletionParams(fullPublicId));
        }
        catch { /* ignore */ }
    }

    private async Task<string> GenerateImageFromHuggingFace(string prompt, string? style)
    {
        var styleMap = new Dictionary<string, string>
        {
            ["realistic"] = "photorealistic, high detail, sharp focus",
            ["cartoon"] = "cartoon style, vibrant colors",
            ["watercolor"] = "watercolor painting, soft brush strokes",
            ["cinematic"] = "cinematic lighting, dramatic film look",
            ["minimalist"] = "minimalist, clean composition",
            ["oil-painting"] = "oil painting style, textured canvas",
        };
        var styleHint = styleMap.GetValueOrDefault(style ?? "realistic");
        var fullPrompt = $"{prompt}. Style: {styleHint}.";

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config["HuggingFace:ApiKey"]);

        var hfResponse = await http.PostAsJsonAsync(
            "https://router.huggingface.co/hf-inference/models/black-forest-labs/FLUX.1-schnell",
            new { inputs = fullPrompt });

        if (!hfResponse.IsSuccessStatusCode)
            throw new Exception(await hfResponse.Content.ReadAsStringAsync());

        var contentType = hfResponse.Content.Headers.ContentType?.MediaType ?? "image/png";
        if (contentType.Contains("application/json"))
            throw new Exception(await hfResponse.Content.ReadAsStringAsync());

        var bytes = await hfResponse.Content.ReadAsByteArrayAsync();
        if (bytes.Length == 0) throw new Exception("Empty image returned from HuggingFace");

        return await UploadBytesAsync(bytes, "generated.png");
    }

    // ── Helper: get or create PostImage for a post ────────────────────────────

    private async Task<PostImage> GetOrCreateMedia(Post post)
    {
        if (post.Media != null) return post.Media;

        var media = new PostImage { PostId = post.Id };
        _db.PostImages.Add(media);
        post.Media = media;
        return media;
    }

    // ── POST /api/images/generate ─────────────────────────────────────────────

    [HttpPost("/api/images/generate")]
    public async Task<IActionResult> GenerateOnly([FromBody] GenerateImageDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Prompt))
            return BadRequest(new { message = "Prompt is required" });
        try
        {
            var url = await GenerateImageFromHuggingFace(dto.Prompt, dto.Style);
            return Ok(new { message = "Image generated", url });
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { message = "Generation error", details = ex.Message });
        }
    }

    // ── POST /api/images/upload-temp ──────────────────────────────────────────

    [HttpPost("/api/images/upload-temp")]
    public async Task<IActionResult> UploadTemp(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file provided" });

        try
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            ms.Position = 0;

            string url;
            if (file.ContentType.StartsWith("video/"))
            {
                var result = await _cloudinary.UploadAsync(new VideoUploadParams
                {
                    File = new FileDescription(file.FileName, ms),
                    Folder = "autogenerate/videos",
                });
                if (result.Error != null)
                    return StatusCode(500, new { message = result.Error.Message });
                url = result.SecureUrl.ToString();
            }
            else
            {
                var result = await _cloudinary.UploadAsync(new ImageUploadParams
                {
                    File = new FileDescription(file.FileName, ms),
                    Folder = "autogenerate/posts",
                    Transformation = new Transformation().Quality("auto").FetchFormat("auto"),
                });
                if (result.Error != null)
                    return StatusCode(500, new { message = result.Error.Message });
                url = result.SecureUrl.ToString();
            }

            return Ok(new { url });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    // ── GET /api/posts/{postId}/images ────────────────────────────────────────

    [HttpGet("/api/posts/{postId}/images")]
    public async Task<IActionResult> GetImages(int postId)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .Include(p => p.Media)
            .FirstOrDefaultAsync(p => p.Id == postId && p.UserId == user.Id);
        if (post == null) return NotFound();

        return Ok(post.Media?.GetUrls() ?? new List<string>());
    }

    // ── POST /api/posts/{postId}/images/upload ────────────────────────────────

    [HttpPost("/api/posts/{postId}/images/upload")]
    public async Task<IActionResult> UploadImage(int postId, IFormFile file)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .Include(p => p.Media)
            .FirstOrDefaultAsync(p => p.Id == postId && p.UserId == user.Id);
        if (post == null) return NotFound();
        if (file == null || file.Length == 0) return BadRequest(new { message = "No file provided" });

        var imageUrl = await UploadFormFileAsync(file);
        var media = await GetOrCreateMedia(post);
        var urls = media.GetUrls();
        urls.Add(imageUrl);
        media.SetUrls(urls);

        await _db.SaveChangesAsync();
        return Ok(new { message = "Image uploaded", url = imageUrl });
    }

    // ── POST /api/posts/{postId}/images/url ──────────────────────────────────

    [HttpPost("/api/posts/{postId}/images/url")]
    [AllowAnonymous]
    public async Task<IActionResult> SetImageUrl(int postId, [FromBody] SetImageUrlDto dto)
    {
        var post = await _db.Posts
            .Include(p => p.Media)
            .FirstOrDefaultAsync(p => p.Id == postId);
        if (post == null) return NotFound();
        if (string.IsNullOrWhiteSpace(dto.Url)) return BadRequest(new { message = "URL is required" });

        string finalUrl;
        if (dto.Url.StartsWith("data:image"))
        {
            var bytes = Convert.FromBase64String(dto.Url.Substring(dto.Url.IndexOf(',') + 1));
            finalUrl = await UploadBytesAsync(bytes, "upload.png");
        }
        else if (dto.Url.StartsWith("http"))
            finalUrl = dto.Url;
        else
            return BadRequest(new { message = "URL must be http(s) or data:image base64" });

        var media = await GetOrCreateMedia(post);
        var urls = media.GetUrls();
        if (!urls.Contains(finalUrl)) urls.Add(finalUrl);
        media.SetUrls(urls);

        await _db.SaveChangesAsync();
        return Ok(new { message = "Image saved", url = finalUrl });
    }

    // ── POST /api/posts/{postId}/images/generate ──────────────────────────────

    [HttpPost("/api/posts/{postId}/images/generate")]
    public async Task<IActionResult> GenerateImage(int postId, [FromBody] GenerateImageDto dto)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .Include(p => p.Topic)
            .Include(p => p.Captions)
            .Include(p => p.Media)
            .FirstOrDefaultAsync(p => p.Id == postId && p.UserId == user.Id);
        if (post == null) return NotFound();

        try
        {
            var caption = post.Captions.FirstOrDefault(c => c.IsSelected)?.Content ?? "";
            var prompt = $"{dto.Prompt ?? caption}. Topic: {post.Topic?.Name ?? ""}.";
            var imageUrl = await GenerateImageFromHuggingFace(prompt, dto.Style);

            var media = await GetOrCreateMedia(post);
            var urls = media.GetUrls();
            urls.Insert(0, imageUrl);
            media.SetUrls(urls);

            await _db.SaveChangesAsync();
            return Ok(new { message = "Image generated", url = imageUrl });
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { message = "Generation error", details = ex.Message });
        }
    }

    // ── DELETE /api/posts/{postId}/images ─────────────────────────────────────

    [HttpDelete("/api/posts/{postId}/images")]
    public async Task<IActionResult> DeleteImage(int postId, [FromQuery] string url)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .Include(p => p.Media)
            .FirstOrDefaultAsync(p => p.Id == postId && p.UserId == user.Id);
        if (post == null) return NotFound();
        if (post.Media == null) return NotFound(new { message = "No media found" });

        var urls = post.Media.GetUrls();
        if (!urls.Remove(url)) return NotFound(new { message = "URL not found" });

        post.Media.SetUrls(urls);
        await DeleteFromCloudinaryAsync(url);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Image deleted" });
    }
}