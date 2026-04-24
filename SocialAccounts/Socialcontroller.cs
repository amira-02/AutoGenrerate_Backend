using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[Tags("Social")]
public class SocialController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly HttpClient _http;

    public SocialController(IConfiguration config, IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _http = httpClientFactory.CreateClient();
    }

    private string Token => _config["Instagram:AccessToken"] ?? "";
    private string AccountId => _config["Instagram:AccountId"] ?? "";
    private const string BASE = "https://graph.facebook.com/v20.0";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<JsonElement?> GetGraph(string path)
    {
        var url = $"{BASE}/{path}";
        var sep = url.Contains("?") ? "&" : "?";
        url += $"{sep}access_token={Token}";

        var res = await _http.GetAsync(url);
        var raw = await res.Content.ReadAsStringAsync();

        try
        {
            var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("error", out var err))
                return null;
            return doc.RootElement;
        }
        catch { return null; }
    }

    // ── GET /api/social/instagram/overview ────────────────────────────────────
    // Followers, media count, profile info

    [HttpGet("instagram/overview")]
    public async Task<IActionResult> GetOverview()
    {
        var data = await GetGraph(
            $"{AccountId}?fields=followers_count,media_count,profile_picture_url,name,biography,website"
        );

        if (data == null)
            return StatusCode(502, new { message = "Failed to fetch Instagram overview" });

        return Ok(data);
    }

    // ── GET /api/social/instagram/insights ────────────────────────────────────
    // Reach, impressions, follower count over time

    [HttpGet("instagram/insights")]
    public async Task<IActionResult> GetInsights([FromQuery] string period = "day", [FromQuery] int days = 30)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeSeconds();
        var until = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var data = await GetGraph(
            $"{AccountId}/insights?metric=reach,follower_count&period={period}&since={since}&until={until}"
        );

        if (data == null)
            return StatusCode(502, new { message = "Failed to fetch Instagram insights" });

        return Ok(data);
    }

    // ── GET /api/social/instagram/media ───────────────────────────────────────
    // Recent posts with like/comment counts

    [HttpGet("instagram/media")]
    public async Task<IActionResult> GetMedia([FromQuery] int limit = 12)
    {
        var data = await GetGraph(
            $"{AccountId}/media?fields=id,caption,media_type,media_url,thumbnail_url,timestamp,like_count,comments_count,permalink&limit={limit}"
        );

        if (data == null)
            return StatusCode(502, new { message = "Failed to fetch Instagram media" });

        return Ok(data);
    }

    // ── GET /api/social/instagram/media/{mediaId}/insights ────────────────────
    // Insights for a specific post

    [HttpGet("instagram/media/{mediaId}/insights")]
    public async Task<IActionResult> GetMediaInsights(string mediaId)
    {
        var data = await GetGraph(
            $"{mediaId}/insights?metric=impressions,reach,likes,comments,shares,saved"
        );

        if (data == null)
            return StatusCode(502, new { message = "Failed to fetch media insights" });

        return Ok(data);
    }

    // ── GET /api/social/instagram/audience ────────────────────────────────────
    // Audience demographics

    [HttpGet("instagram/audience")]
    public async Task<IActionResult> GetAudience()
    {
        var data = await GetGraph(
            $"{AccountId}/insights?metric=audience_country,audience_city,audience_gender_age&period=lifetime"
        );

        if (data == null)
            return StatusCode(502, new { message = "Failed to fetch audience data" });

        return Ok(data);
    }

    // ── GET /api/social/instagram/summary ─────────────────────────────────────
    // All-in-one summary for the Analytics dashboard

    [HttpGet("instagram/summary")]
    public async Task<IActionResult> GetSummary()
    {
        // Fetch all in parallel
        var overviewTask = GetGraph($"{AccountId}?fields=followers_count,media_count,name,profile_picture_url");
        var insightsTask = GetGraph($"{AccountId}/insights?metric=reach,follower_count&period=day&since={DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds()}&until={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
        var mediaTask = GetGraph($"{AccountId}/media?fields=id,caption,media_type,timestamp,like_count,comments_count,permalink,media_url,thumbnail_url&limit=6");

        await Task.WhenAll(overviewTask, insightsTask, mediaTask);

        var overview = await overviewTask;
        var insights = await insightsTask;
        var media = await mediaTask;

        // Parse media for top posts
        var topPosts = new List<object>();
        if (media.HasValue && media.Value.TryGetProperty("data", out var mediaData))
        {
            foreach (var item in mediaData.EnumerateArray())
            {
                topPosts.Add(new
                {
                    id = item.TryGetProperty("id", out var id) ? id.GetString() : "",
                    caption = item.TryGetProperty("caption", out var cap) ? cap.GetString() : "",
                    mediaType = item.TryGetProperty("media_type", out var mt) ? mt.GetString() : "",
                    mediaUrl = item.TryGetProperty("media_url", out var mu) ? mu.GetString() : item.TryGetProperty("thumbnail_url", out var tu) ? tu.GetString() : "",
                    timestamp = item.TryGetProperty("timestamp", out var ts) ? ts.GetString() : "",
                    likeCount = item.TryGetProperty("like_count", out var lc) ? lc.GetInt32() : 0,
                    commentCount = item.TryGetProperty("comments_count", out var cc) ? cc.GetInt32() : 0,
                    permalink = item.TryGetProperty("permalink", out var pl) ? pl.GetString() : "",
                });
            }
        }

        // Parse reach timeline
        var reachTimeline = new List<object>();
        var followerTimeline = new List<object>();

        if (insights.HasValue && insights.Value.TryGetProperty("data", out var insightData))
        {
            foreach (var metric in insightData.EnumerateArray())
            {
                var metricName = metric.TryGetProperty("name", out var mn) ? mn.GetString() : "";
                if (!metric.TryGetProperty("values", out var vals)) continue;

                foreach (var val in vals.EnumerateArray())
                {
                    var value = val.TryGetProperty("value", out var v) ? v.GetInt32() : 0;
                    var endTime = val.TryGetProperty("end_time", out var et) ? et.GetString() : "";

                    if (metricName == "reach")
                        reachTimeline.Add(new { date = endTime, value });
                    else if (metricName == "follower_count")
                        followerTimeline.Add(new { date = endTime, value });
                }
            }
        }

        return Ok(new
        {
            followers = overview.HasValue && overview.Value.TryGetProperty("followers_count", out var fc) ? fc.GetInt32() : 0,
            mediaCount = overview.HasValue && overview.Value.TryGetProperty("media_count", out var mc) ? mc.GetInt32() : 0,
            name = overview.HasValue && overview.Value.TryGetProperty("name", out var nm) ? nm.GetString() : "",
            profilePicture = overview.HasValue && overview.Value.TryGetProperty("profile_picture_url", out var pp) ? pp.GetString() : "",
            reachTimeline,
            followerTimeline,
            topPosts,
            totalLikes = topPosts.Sum(p => (int)p.GetType().GetProperty("likeCount")!.GetValue(p)!),
            totalComments = topPosts.Sum(p => (int)p.GetType().GetProperty("commentCount")!.GetValue(p)!),
        });
    }
}