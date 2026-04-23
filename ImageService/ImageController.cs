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

    // ── Helpers Cloudinary ────────────────────────────────────────────────────

    private async Task<string> UploadBytesAsync(byte[] bytes, string fileName)
    {
        using var stream = new MemoryStream(bytes);
        var result = await _cloudinary.UploadAsync(new ImageUploadParams
        {
            File = new FileDescription(fileName, stream),
            PublicId = $"autogenerate/posts/{Guid.NewGuid()}",
            Overwrite = true,
            Transformation = new Transformation().Quality("auto").FetchFormat("auto")
        });
        if (result.Error != null) throw new Exception(result.Error.Message);
        return result.SecureUrl.ToString();
    }

    private async Task<string> UploadFormFileAsync(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        var result = await _cloudinary.UploadAsync(new ImageUploadParams
        {
            File = new FileDescription(file.FileName, stream),
            PublicId = $"autogenerate/posts/{Guid.NewGuid()}",
            Overwrite = true,
            Transformation = new Transformation().Quality("auto").FetchFormat("auto")
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
            var publicIdWithExt = string.Join("/", afterUpload.Skip(start));
            var publicId = Path.GetFileNameWithoutExtension(publicIdWithExt);
            var folder = string.Join("/", afterUpload.Skip(start).Take(afterUpload.Length - start - 1));
            var fullPublicId = string.IsNullOrEmpty(folder) ? publicId : $"{folder}/{publicId}";
            await _cloudinary.DestroyAsync(new DeletionParams(fullPublicId));
        }
        catch { /* Ignore */ }
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
            ["oil-painting"] = "oil painting style, textured canvas"
        };
        var styleHint = styleMap.GetValueOrDefault(style ?? "realistic");
        var fullPrompt = $"{prompt}. Style: {styleHint}.";

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config["HuggingFace:ApiKey"]);

        var hfResponse = await httpClient.PostAsJsonAsync(
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

    // ═════════════════════════════════════════════════════════════════════════
    // ROUTES sans postId  →  /api/images/...
    // Utilisées par NewPostModal AVANT la création du post
    // ═════════════════════════════════════════════════════════════════════════

    // POST /api/images/generate
    // Génère une image et renvoie l'URL Cloudinary — aucun post créé
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

    // ═════════════════════════════════════════════════════════════════════════
    // ROUTES avec postId  →  /api/posts/{postId}/images/...
    // Utilisées quand le post existe déjà
    // ═════════════════════════════════════════════════════════════════════════

    // GET /api/posts/{postId}/images
    [HttpGet("/api/posts/{postId}/images")]
    public async Task<IActionResult> GetImages(int postId)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts.Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == postId && p.UserId == user.Id);
        if (post == null) return NotFound();

        return Ok(post.Images.OrderBy(i => i.Order).Select(i => new
        {
            id = i.Id,
            url = i.Url,
            source = i.Source.ToString().ToLower(),
            altText = i.AltText,
            order = i.Order
        }));
    }

    // POST /api/posts/{postId}/images/upload
    [HttpPost("/api/posts/{postId}/images/upload")]
    public async Task<IActionResult> UploadImage(int postId, IFormFile file)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts.Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == postId && p.UserId == user.Id);
        if (post == null) return NotFound();
        if (file == null || file.Length == 0) return BadRequest(new { message = "No file provided" });

        var allowed = new[] { "image/jpeg", "image/png", "image/webp", "image/gif" };
        if (!allowed.Contains(file.ContentType.ToLower()))
            return BadRequest(new { message = "Invalid file type" });

        var imageUrl = await UploadFormFileAsync(file);

        var old = post.Images.Where(i => i.Source == ImageSource.Upload).ToList();
        foreach (var img in old) await DeleteFromCloudinaryAsync(img.Url);
        _db.PostImages.RemoveRange(old);

        post.Images.Add(new Image { Url = imageUrl, Source = ImageSource.Upload, AltText = file.FileName, Order = 0 });
        await _db.SaveChangesAsync();
        return Ok(new { message = "Image uploaded", url = imageUrl });
    }

    // POST /api/posts/{postId}/images/url
    // Reçoit une URL Cloudinary (déjà uploadée) ou base64 → sauvegarde en DB
    [HttpPost("/api/posts/{postId}/images/url")]
    [AllowAnonymous]
    public async Task<IActionResult> SetImageUrl(int postId, [FromBody] SetImageUrlDto dto)
    {
        var post = await _db.Posts.Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == postId);
        if (post == null) return NotFound();
        if (string.IsNullOrWhiteSpace(dto.Url)) return BadRequest(new { message = "URL is required" });

        string finalUrl;

        if (dto.Url.StartsWith("data:image"))
        {
            // base64 → Cloudinary (fallback, normalement plus utilisé)
            var base64Data = dto.Url.Substring(dto.Url.IndexOf(',') + 1);
            var bytes = Convert.FromBase64String(base64Data);
            finalUrl = await UploadBytesAsync(bytes, "upload.png");
        }
        else if (dto.Url.StartsWith("http"))
        {
            // URL Cloudinary déjà uploadée → stocker directement
            finalUrl = dto.Url;
        }
        else
        {
            return BadRequest(new { message = "URL must be http(s) or data:image base64" });
        }

        var old = post.Images.Where(i => i.Source == ImageSource.Generated).ToList();
        _db.PostImages.RemoveRange(old);

        post.Images.Add(new Image { Url = finalUrl, Source = ImageSource.Generated, AltText = dto.AltText ?? "", Order = 0 });
        await _db.SaveChangesAsync();
        return Ok(new { message = "Image saved", url = finalUrl });
    }

    // POST /api/posts/{postId}/images/generate
    // Génère + upload Cloudinary + associe directement au post
    [HttpPost("/api/posts/{postId}/images/generate")]
    public async Task<IActionResult> GenerateImage(int postId, [FromBody] GenerateImageDto dto)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts
            .Include(p => p.Topic).Include(p => p.Captions).Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == postId && p.UserId == user.Id);
        if (post == null) return NotFound();

        try
        {
            var caption = post.Captions.FirstOrDefault(c => c.IsSelected)?.Content ?? "";
            var prompt = $"{dto.Prompt ?? caption}. Topic: {post.Topic?.Name ?? ""}.";
            var imageUrl = await GenerateImageFromHuggingFace(prompt, dto.Style);

            var old = post.Images.Where(i => i.Source == ImageSource.Generated).ToList();
            foreach (var img in old) await DeleteFromCloudinaryAsync(img.Url);
            _db.PostImages.RemoveRange(old);

            post.Images.Add(new Image { Url = imageUrl, Source = ImageSource.Generated, AltText = dto.Prompt ?? caption, Order = 0 });
            await _db.SaveChangesAsync();
            return Ok(new { message = "Image generated", url = imageUrl });
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { message = "Generation error", details = ex.Message });
        }
    }

    // DELETE /api/posts/{postId}/images/{imageId}
    [HttpDelete("/api/posts/{postId}/images/{imageId}")]
    public async Task<IActionResult> DeleteImage(int postId, int imageId)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var post = await _db.Posts.Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == postId && p.UserId == user.Id);
        if (post == null) return NotFound();

        var image = post.Images.FirstOrDefault(i => i.Id == imageId);
        if (image == null) return NotFound();

        await DeleteFromCloudinaryAsync(image.Url);
        _db.PostImages.Remove(image);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Image deleted" });
    }
}