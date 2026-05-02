using AutoGenerate.Shared.Data;
using AutoGenerate.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[Tags("Social")]
public class SocialController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly HttpClient _http;
    private readonly IConfiguration _config;

    public SocialController(AppDbContext db, IHttpClientFactory httpClientFactory, IConfiguration config)
    {
        _db = db;
        _http = httpClientFactory.CreateClient();
        _config = config;
    }

    private const string BASE = "https://graph.facebook.com/v20.0";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<User?> GetCurrentUserAsync()
    {
        var email = User.FindFirst(ClaimTypes.Name)?.Value
                 ?? User.FindFirst("email")?.Value;
        if (email == null) return null;
        return await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
    }

    // Get Instagram account from DB for the current user
    private async Task<SocialAccount?> GetInstagramAccountAsync()
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return null;

        return await _db.SocialAccounts
            .FirstOrDefaultAsync(a =>
                a.UserId == user.Id &&
                a.Platform == "instagram" &&
                a.IsConnected &&
                !string.IsNullOrEmpty(a.AccessToken));
    }

    private async Task<JsonElement?> GetGraph(string path, string accessToken)
    {
        var url = $"{BASE}/{path}";
        var sep = url.Contains("?") ? "&" : "?";
        url += $"{sep}access_token={accessToken}";

        var res = await _http.GetAsync(url);
        var raw = await res.Content.ReadAsStringAsync();

        try
        {
            var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("error", out _))
                return null;
            return doc.RootElement;
        }
        catch { return null; }
    }

    private sealed class RawIgComment
    {
        public string Id { get; init; } = "";
        public string Text { get; init; } = "";
        public string Timestamp { get; init; } = "";
        public string Username { get; init; } = "";
    }

    private sealed class AiSentimentComment
    {
        public string Id { get; init; } = "";
        public string Text { get; init; } = "";
        public string Timestamp { get; init; } = "";
        public string Username { get; init; } = "";
        public string Sentiment { get; init; } = "neutral";
        public double Confidence { get; init; } = 0.5;
    }

    //private async Task<(bool Success, string Error, List<AiSentimentComment> Comments, string Summary)> AnalyzeCommentsWithGroqAsync(List<RawIgComment> comments)
    //{
    //    var apiKey = _config["Groq:ApiKey"];
    //    if (string.IsNullOrWhiteSpace(apiKey))
    //        return (false, "Groq API key is missing.", new List<AiSentimentComment>(), "");

    //    var promptComments = comments.Select((c, idx) => new
    //    {
    //        index = idx + 1,
    //        id = c.Id,
    //        text = c.Text
    //    });

    //    var body = new
    //    {
    //        model = "llama-3.1-8b-instant",
    //        temperature = 0.1,
    //        response_format = new { type = "json_object" },
    //        messages = new object[]
    //        {
    //            new
    //            {
    //                role = "system",
    //                content = "You are a strict sentiment classifier for social-media comments. Classify each comment as positive, neutral, or negative."
    //            },
    //            new
    //            {
    //                role = "user",
    //                content =
    //                    "Analyze the sentiment of each Instagram comment and return strict JSON with this shape: " +
    //                    "{\"sentiments\":[{\"index\":1,\"sentiment\":\"positive|neutral|negative\",\"confidence\":0.0}],\"summary\":\"short summary\"}. " +
    //                    "Do not add extra keys. Here are the comments:\n" + JsonSerializer.Serialize(promptComments)
    //            }
    //        }
    //    };

    //    var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
    //    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    //    request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    //    var response = await _http.SendAsync(request);
    //    var raw = await response.Content.ReadAsStringAsync();
    //    if (!response.IsSuccessStatusCode)
    //        return (false, $"Groq request failed: {(int)response.StatusCode}", new List<AiSentimentComment>(), "");

    //    try
    //    {
    //        var root = JsonDocument.Parse(raw).RootElement;
    //        var content = root
    //            .GetProperty("choices")[0]
    //            .GetProperty("message")
    //            .GetProperty("content")
    //            .GetString() ?? "{}";

    //        var parsed = JsonDocument.Parse(content).RootElement;
    //        var summary = parsed.TryGetProperty("summary", out var s) ? (s.GetString() ?? "") : "";

    //        var sentimentByIndex = new Dictionary<int, (string Sentiment, double Confidence)>();
    //        if (parsed.TryGetProperty("sentiments", out var sentiments) && sentiments.ValueKind == JsonValueKind.Array)
    //        {
    //            foreach (var item in sentiments.EnumerateArray())
    //            {
    //                var index = item.TryGetProperty("index", out var i) ? i.GetInt32() : 0;
    //                var sentiment = item.TryGetProperty("sentiment", out var st) ? (st.GetString() ?? "neutral").ToLowerInvariant() : "neutral";
    //                var confidence = item.TryGetProperty("confidence", out var cf) && cf.ValueKind == JsonValueKind.Number ? cf.GetDouble() : 0.5;
    //                if (sentiment is not ("positive" or "neutral" or "negative")) sentiment = "neutral";
    //                if (index > 0) sentimentByIndex[index] = (sentiment, confidence);
    //            }
    //        }

    //        var enriched = comments.Select((c, idx) =>
    //        {
    //            var key = idx + 1;
    //            var val = sentimentByIndex.TryGetValue(key, out var x) ? x : ("neutral", 0.5);
    //            return new AiSentimentComment
    //            {
    //                Id = c.Id,
    //                Text = c.Text,
    //                Timestamp = c.Timestamp,
    //                Username = c.Username,
    //                Sentiment = val.Item1,
    //                Confidence = Math.Round(val.Item2, 3)
    //            };
    //        }).ToList();

    //        return (true, "", enriched, summary);
    //    }
    //    catch
    //    {
    //        return (false, "Failed to parse Groq sentiment response.", new List<AiSentimentComment>(), "");
    //    }
    //}

    // ── GET /api/social/instagram/overview ────────────────────────────────────

    [HttpGet("instagram/overview")]
    public async Task<IActionResult> GetOverview()
    {
        var ig = await GetInstagramAccountAsync();
        if (ig == null) return NotFound(new { message = "Instagram account not connected" });

        var data = await GetGraph(
            $"{ig.AccountId}?fields=followers_count,media_count,profile_picture_url,name,biography,website",
            ig.AccessToken
        );

        if (data == null)
            return StatusCode(502, new { message = "Failed to fetch Instagram overview" });

        return Ok(data);
    }

    // ── GET /api/social/instagram/insights ────────────────────────────────────

    [HttpGet("instagram/insights")]
    public async Task<IActionResult> GetInsights([FromQuery] string period = "day", [FromQuery] int days = 30)
    {
        var ig = await GetInstagramAccountAsync();
        if (ig == null) return NotFound(new { message = "Instagram account not connected" });

        var since = DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeSeconds();
        var until = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var data = await GetGraph(
            $"{ig.AccountId}/insights?metric=reach,follower_count&period={period}&since={since}&until={until}",
            ig.AccessToken
        );

        if (data == null)
            return StatusCode(502, new { message = "Failed to fetch Instagram insights" });

        return Ok(data);
    }

    // ── GET /api/social/instagram/media ───────────────────────────────────────

    [HttpGet("instagram/media")]
    public async Task<IActionResult> GetMedia([FromQuery] int limit = 12)
    {
        var ig = await GetInstagramAccountAsync();
        if (ig == null) return NotFound(new { message = "Instagram account not connected" });

        var data = await GetGraph(
            $"{ig.AccountId}/media?fields=id,caption,media_type,media_url,thumbnail_url,timestamp,like_count,comments_count,permalink&limit={limit}",
            ig.AccessToken
        );

        if (data == null)
            return StatusCode(502, new { message = "Failed to fetch Instagram media" });

        return Ok(data);
    }

    // ── GET /api/social/instagram/media/{mediaId}/insights ────────────────────

    [HttpGet("instagram/media/{mediaId}/insights")]
    public async Task<IActionResult> GetMediaInsights(string mediaId)
    {
        var ig = await GetInstagramAccountAsync();
        if (ig == null) return NotFound(new { message = "Instagram account not connected" });

        var data = await GetGraph(
            $"{mediaId}/insights?metric=impressions,reach,likes,comments,shares,saved",
            ig.AccessToken
        );

        if (data == null)
            return StatusCode(502, new { message = "Failed to fetch media insights" });

        return Ok(data);
    }

    // ── GET /api/social/instagram/media/{mediaId}/comments ────────────────────

    [HttpGet("instagram/media/{mediaId}/comments")]
    public async Task<IActionResult> GetMediaComments(string mediaId, [FromQuery] int limit = 50)
    {
        var ig = await GetInstagramAccountAsync();
        if (ig == null) return NotFound(new { message = "Instagram account not connected" });
        if (string.IsNullOrWhiteSpace(mediaId)) return BadRequest(new { message = "mediaId is required" });

        var safeLimit = Math.Clamp(limit, 1, 100);
        var data = await GetGraph(
            $"{mediaId}/comments?fields=id,text,timestamp,username&limit={safeLimit}",
            ig.AccessToken
        );

        if (data == null)
            return StatusCode(502, new { message = "Failed to fetch media comments" });

        return Ok(data);
    }

    // ── GET /api/social/instagram/media/{mediaId}/sentiment ───────────────────

    //[HttpGet("instagram/media/{mediaId}/sentiment")]
    //public async Task<IActionResult> GetMediaSentiment(string mediaId, [FromQuery] int limit = 50)
    //{
    //    var ig = await GetInstagramAccountAsync();
    //    if (ig == null) return NotFound(new { message = "Instagram account not connected" });
    //    if (string.IsNullOrWhiteSpace(mediaId)) return BadRequest(new { message = "mediaId is required" });

    //    var safeLimit = Math.Clamp(limit, 1, 100);
    //    var commentsData = await GetGraph(
    //        $"{mediaId}/comments?fields=id,text,timestamp,username&limit={safeLimit}",
    //        ig.AccessToken
    //    );

    //    if (commentsData == null)
    //        return StatusCode(502, new { message = "Failed to fetch media comments" });

    //    var comments = new List<RawIgComment>();
    //    if (commentsData.Value.TryGetProperty("data", out var dataArray))
    //    {
    //        foreach (var comment in dataArray.EnumerateArray())
    //        {
    //            comments.Add(new RawIgComment
    //            {
    //                Id = comment.TryGetProperty("id", out var cid) ? cid.GetString() ?? "" : "",
    //                Text = comment.TryGetProperty("text", out var txt) ? txt.GetString() ?? "" : "",
    //                Timestamp = comment.TryGetProperty("timestamp", out var cts) ? cts.GetString() ?? "" : "",
    //                Username = comment.TryGetProperty("username", out var un) ? un.GetString() ?? "" : "",
    //            });
    //        }
    //    }

    //    if (comments.Count == 0)
    //    {
    //        return Ok(new
    //        {
    //            postId = mediaId,
    //            summary = "No comments to analyze.",
    //            positive = 0,
    //            neutral = 0,
    //            negative = 0,
    //            comments = Array.Empty<object>()
    //        });
    //    }

    //    var ai = await AnalyzeCommentsWithGroqAsync(comments);
    //    if (!ai.Success)
    //        return StatusCode(502, new { message = ai.Error });

    //    var positive = ai.Comments.Count(c => c.Sentiment == "positive");
    //    var neutral = ai.Comments.Count(c => c.Sentiment == "neutral");
    //    var negative = ai.Comments.Count(c => c.Sentiment == "negative");

    //    return Ok(new
    //    {
    //        postId = mediaId,
    //        summary = ai.Summary,
    //        positive,
    //        neutral,
    //        negative,
    //        comments = ai.Comments
    //    });
    //}



    [HttpPost("instagram/sentiment")]
    public async Task<IActionResult> AnalyzeSentiment([FromBody] SentimentRequestDto dto)
    {
        // 1. Récupère les commentaires depuis Instagram
        var httpClient = _httpClientFactory.CreateClient();
        var account = await _db.SocialAccounts
            .FirstOrDefaultAsync(a => a.Platform == "instagram" && a.IsConnected);
        if (account == null) return NotFound("No Instagram account");

        var igUrl = $"https://graph.facebook.com/v20.0/{dto.PostId}/comments" +
                    $"?fields=text&limit=50&access_token={account.AccessToken}";
        var igRes = await httpClient.GetFromJsonAsync<JsonElement>(igUrl);
        var comments = igRes.GetProperty("data")
            .EnumerateArray()
            .Select(c => c.GetProperty("text").GetString() ?? "")
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();

        if (comments.Count == 0)
            return Ok(new { positive = 0, negative = 0, neutral = 0, total = 0, summary = "No comments." });

        // 2. Appelle le service Python
        var payload = new { comments, caption = dto.Caption };
        var response = await httpClient.PostAsJsonAsync("http://localhost:8001/analyze", payload);
        var result = await response.Content.ReadFromJsonAsync<object>();

        return Ok(result);
    }

    // ── GET /api/social/instagram/audience ────────────────────────────────────

    [HttpGet("instagram/audience")]
    public async Task<IActionResult> GetAudience()
    {
        var ig = await GetInstagramAccountAsync();
        if (ig == null) return NotFound(new { message = "Instagram account not connected" });

        var data = await GetGraph(
            $"{ig.AccountId}/insights?metric=audience_country,audience_city,audience_gender_age&period=lifetime",
            ig.AccessToken
        );

        if (data == null)
            return StatusCode(502, new { message = "Failed to fetch audience data" });

        return Ok(data);
    }

    // ── GET /api/social/instagram/summary ─────────────────────────────────────

    [HttpGet("instagram/summary")]
    public async Task<IActionResult> GetSummary()
    {
        var ig = await GetInstagramAccountAsync();
        if (ig == null)
            return NotFound(new { message = "Instagram account not connected. Please connect your account in the Accounts section." });

        var accountId = ig.AccountId!;
        var accessToken = ig.AccessToken;

        // Fetch all in parallel
        var overviewTask = GetGraph(
            $"{accountId}?fields=followers_count,media_count,name,profile_picture_url",
            accessToken
        );
        var insightsTask = GetGraph(
            $"{accountId}/insights?metric=reach,follower_count&period=day&since={DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds()}&until={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            accessToken
        );
        var mediaTask = GetGraph(
            $"{accountId}/media?fields=id,caption,media_type,timestamp,like_count,comments_count,permalink,media_url,thumbnail_url&limit=6",
            accessToken
        );

        await Task.WhenAll(overviewTask, insightsTask, mediaTask);

        var overview = await overviewTask;
        var insights = await insightsTask;
        var media = await mediaTask;

        // Parse media + comments per post
        var topPosts = new List<object>();
        var totalLikes = 0;
        var totalComments = 0;
        if (media.HasValue && media.Value.TryGetProperty("data", out var mediaData))
        {
            foreach (var item in mediaData.EnumerateArray())
            {
                var mediaId = item.TryGetProperty("id", out var id) ? id.GetString() : "";
                var caption = item.TryGetProperty("caption", out var cap) ? cap.GetString() : "";
                var mediaType = item.TryGetProperty("media_type", out var mt) ? mt.GetString() : "";
                var mediaUrl = item.TryGetProperty("media_url", out var mu) ? mu.GetString() :
                               item.TryGetProperty("thumbnail_url", out var tu) ? tu.GetString() : "";
                var timestamp = item.TryGetProperty("timestamp", out var ts) ? ts.GetString() : "";
                var likeCount = item.TryGetProperty("like_count", out var lc) ? lc.GetInt32() : 0;
                var commentCount = item.TryGetProperty("comments_count", out var cc) ? cc.GetInt32() : 0;
                var permalink = item.TryGetProperty("permalink", out var pl) ? pl.GetString() : "";

                var comments = new List<object>();
                if (!string.IsNullOrWhiteSpace(mediaId) && commentCount > 0)
                {
                    var commentsData = await GetGraph(
                        $"{mediaId}/comments?fields=id,text,timestamp,username&limit=50",
                        accessToken
                    );

                    if (commentsData.HasValue && commentsData.Value.TryGetProperty("data", out var commentsArray))
                    {
                        foreach (var comment in commentsArray.EnumerateArray())
                        {
                            comments.Add(new
                            {
                                id = comment.TryGetProperty("id", out var cid) ? cid.GetString() : "",
                                text = comment.TryGetProperty("text", out var txt) ? txt.GetString() : "",
                                timestamp = comment.TryGetProperty("timestamp", out var cts) ? cts.GetString() : "",
                                username = comment.TryGetProperty("username", out var un) ? un.GetString() : "",
                            });
                        }
                    }
                }

                topPosts.Add(new
                {
                    id = mediaId,
                    caption,
                    mediaType,
                    mediaUrl,
                    timestamp,
                    likeCount,
                    commentCount,
                    permalink,
                    comments,
                });

                totalLikes += likeCount;
                totalComments += commentCount;
            }
        }

        // Parse insights
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

        var followers = overview.HasValue && overview.Value.TryGetProperty("followers_count", out var fc) ? fc.GetInt32() : 0;
        var mediaCount = overview.HasValue && overview.Value.TryGetProperty("media_count", out var mc) ? mc.GetInt32() : 0;
        var name = overview.HasValue && overview.Value.TryGetProperty("name", out var nm) ? nm.GetString() : "";
        var picture = overview.HasValue && overview.Value.TryGetProperty("profile_picture_url", out var pp) ? pp.GetString() : "";

        // ✅ Update DB with latest profile data
        ig.FollowersCount = followers;
        ig.ProfilePicture = picture;
        ig.Username = name;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            followers,
            mediaCount,
            name,
            profilePicture = picture,
            reachTimeline,
            followerTimeline,
            topPosts,
            totalLikes,
            totalComments,
        });
    }

    // ── Facebook helpers ──────────────────────────────────────────────────────

    private async Task<SocialAccount?> GetFacebookAccountAsync()
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return null;

        return await _db.SocialAccounts
            .FirstOrDefaultAsync(a =>
                a.UserId == user.Id &&
                a.Platform == "facebook" &&
                a.IsConnected &&
                !string.IsNullOrEmpty(a.AccessToken));
    }

    // ── GET /api/social/facebook/summary ──────────────────────────────────────

    [HttpGet("facebook/summary")]
    public async Task<IActionResult> GetFacebookSummary()
    {
        var fb = await GetFacebookAccountAsync();
        if (fb == null)
            return NotFound(new { message = "Facebook account not connected. Please connect your Page in the Accounts section." });

        var pageId = fb.AccountId!;
        var accessToken = fb.AccessToken;

        // Fetch in parallel
        var overviewTask = GetGraph(
            $"{pageId}?fields=name,fan_count,followers_count,picture.type(large),about,website",
            accessToken
        );
        var insightsTask = GetGraph(
            $"{pageId}/insights?metric=page_impressions,page_engaged_users,page_post_engagements,page_fan_adds&period=day&since={DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds()}&until={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            accessToken
        );
        var postsTask = GetGraph(
            $"{pageId}/posts?fields=id,message,story,created_time,full_picture,permalink_url&limit=6",
            accessToken
        );

        await Task.WhenAll(overviewTask, insightsTask, postsTask);

        var overview = await overviewTask;
        var insights = await insightsTask;
        var posts = await postsTask;

        // Parse posts
        var recentPosts = new List<object>();
        if (posts.HasValue && posts.Value.TryGetProperty("data", out var postsData))
        {
            foreach (var item in postsData.EnumerateArray())
            {
                recentPosts.Add(new
                {
                    id = item.TryGetProperty("id", out var id) ? id.GetString() : "",
                    message = item.TryGetProperty("message", out var msg) ? msg.GetString() :
                                   item.TryGetProperty("story", out var st) ? st.GetString() : "",
                    createdTime = item.TryGetProperty("created_time", out var ct) ? ct.GetString() : "",
                    fullPicture = item.TryGetProperty("full_picture", out var fp) ? fp.GetString() : "",
                    permalinkUrl = item.TryGetProperty("permalink_url", out var pu) ? pu.GetString() : "",
                });
            }
        }

        // Parse insights into timelines
        var impressionsTimeline = new List<object>();
        var engagedUsersTimeline = new List<object>();
        var fanAddsTimeline = new List<object>();

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

                    if (metricName == "page_impressions") impressionsTimeline.Add(new { date = endTime, value });
                    else if (metricName == "page_engaged_users") engagedUsersTimeline.Add(new { date = endTime, value });
                    else if (metricName == "page_fan_adds") fanAddsTimeline.Add(new { date = endTime, value });
                }
            }
        }

        var fans = overview.HasValue && overview.Value.TryGetProperty("fan_count", out var fc) ? fc.GetInt32() : 0;
        var followers = overview.HasValue && overview.Value.TryGetProperty("followers_count", out var fo) ? fo.GetInt32() : 0;
        var name = overview.HasValue && overview.Value.TryGetProperty("name", out var nm) ? nm.GetString() : "";
        var picture = overview.HasValue && overview.Value.TryGetProperty("picture", out var pp)
            ? (pp.TryGetProperty("data", out var ppData) && ppData.TryGetProperty("url", out var ppUrl) ? ppUrl.GetString() : "")
            : "";

        // Update DB
        fb.FollowersCount = followers > 0 ? followers : fans;
        fb.ProfilePicture = picture;
        fb.Username = name;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            fans,
            followers,
            name,
            profilePicture = picture,
            impressionsTimeline,
            engagedUsersTimeline,
            fanAddsTimeline,
            recentPosts,
            totalImpressions = impressionsTimeline.Sum(p => (int)p.GetType().GetProperty("value")!.GetValue(p)!),
            totalEngagedUsers = engagedUsersTimeline.Sum(p => (int)p.GetType().GetProperty("value")!.GetValue(p)!),
        });
    }
}