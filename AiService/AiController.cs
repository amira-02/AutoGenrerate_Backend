using AutoGenerate.AiService;
using AutoGenerate.Shared.Data;
using AutoGenerate.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[Tags("AI")]
public class AiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly HttpClient _http;
    private readonly IConfiguration _config;

    public AiController(AppDbContext db, IHttpClientFactory httpClientFactory, IConfiguration config)
    {
        _db = db;
        _http = httpClientFactory.CreateClient();
        _config = config;
    }

    [HttpPost("recommendations")]
    public async Task<IActionResult> GetRecommendations([FromBody] RecommendationsRequestDto dto)
    {
        var apiKey = _config["Groq:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
            return StatusCode(500, new { message = "Groq API key not configured" });

        // ── Cache : max 1 génération par jour ─────────────────────────────
        var today = DateTime.UtcNow.Date;
        var cached = await _db.AiRecommendations
            .Where(r => r.CreatedAt.Date == today)
            .FirstOrDefaultAsync();

        if (cached != null)
            return Ok(JsonDocument.Parse(cached.Content).RootElement);

        // ── Fetch Google Trends RSS (gratuit, pas de clé) ─────────────────
        var trendsText = "";
        try
        {
            var trendsRes = await _http.GetStringAsync(
                "https://trends.google.com/trends/trendingsearches/daily/rss?geo=US"
            );
            // Extrait les titres des trends depuis le XML
            var titles = new List<string>();
            var lines = trendsRes.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("<title>") && !trimmed.Contains("Google Trends"))
                {
                    var title = trimmed.Replace("<title>", "").Replace("</title>", "")
                                       .Replace("<![CDATA[", "").Replace("]]>", "").Trim();
                    if (!string.IsNullOrEmpty(title)) titles.Add(title);
                }
            }
            trendsText = string.Join(", ", titles.Take(10));
        }
        catch
        {
            trendsText = "social media marketing, content creation, brand engagement";
        }

        // ── Appel Groq ─────────────────────────────────────────────────────
        var todayStr = DateTime.Now.ToString("dddd, MMMM d, yyyy");

        var prompt = $@"You are a professional social media strategist. Today is {todayStr}.
 
Current trending topics on Google: {trendsText}
 
Account context:
- Instagram: {dto.IgFollowers} followers, {dto.IgEngRate}% engagement rate, {dto.IgMediaCount} posts
- Facebook: {dto.FbFans} fans
- Recent post topics: {dto.RecentCaptions ?? "none yet"}
 
Based on these real trending topics and the account context, generate 5 specific actionable social media recommendations.
 
Return ONLY a valid JSON array, no markdown, no explanation:
[
  {{
    ""type"": ""trend or event or content or engagement"",
    ""title"": ""short title max 8 words"",
    ""description"": ""2 sentences explaining the opportunity"",
    ""action"": ""one specific action starting with a verb"",
    ""urgency"": ""high or medium or low"",
    ""platform"": [""instagram"", ""facebook""]
  }}
]";

        var groqBody = new
        {
            model = "llama-3.1-8b-instant",
            temperature = 0.4,
            max_tokens = 1000,
            messages = new[]
            {
                new { role = "system", content = "You are a social media strategist. Always respond with valid JSON only, no markdown." },
                new { role = "user",   content = prompt }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        request.Content = new StringContent(
            JsonSerializer.Serialize(groqBody), Encoding.UTF8, "application/json"
        );

        var response = await _http.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            return StatusCode(502, new { message = "Groq API error", details = raw });

        try
        {
            var root = JsonDocument.Parse(raw).RootElement;
            var content = root
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "[]";

            var clean = content.Replace("```json", "").Replace("```", "").Trim();

            // Valide que c'est bien un JSON array
            var parsed = JsonDocument.Parse(clean);
            if (parsed.RootElement.ValueKind != JsonValueKind.Array)
                return StatusCode(502, new { message = "Invalid response format from Groq" });

            // Sauvegarde en cache
            _db.AiRecommendations.Add(new AiRecommendation
            {
                Content = clean,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            return Ok(parsed.RootElement);
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { message = "Failed to parse Groq response", details = ex.Message });
        }
    }
}