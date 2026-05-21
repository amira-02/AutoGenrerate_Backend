
using AutoGenerate.Shared.Data;
using AutoGenerate.Shared.Models;
using AutoGenerate.SocialAccounts;
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

    private async Task<SocialAccount?> GetInstagramAccountAsync(int clientId = 0, int accountId = 0)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return null;
        return await _db.SocialAccounts
            .FirstOrDefaultAsync(a =>
                a.Client.UserId == user.Id &&
                (clientId  == 0 || a.ClientId == clientId) &&
                (accountId == 0 || a.Id       == accountId) &&
                a.PlatformId == 1 &&
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
            if (doc.RootElement.TryGetProperty("error", out _)) return null;
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

    // ── GET /api/social/instagram/overview ────────────────────────────────────

    [HttpGet("instagram/overview")]
    public async Task<IActionResult> GetOverview([FromQuery] int clientId = 0, [FromQuery] int accountId = 0)
    {
        var ig = await GetInstagramAccountAsync(clientId, accountId);
        if (ig == null) return NotFound(new { message = "Instagram account not connected" });
        var data = await GetGraph(
            $"{ig.AccountId}?fields=followers_count,media_count,profile_picture_url,name,biography,website",
            ig.AccessToken);
        if (data == null) return StatusCode(502, new { message = "Failed to fetch Instagram overview" });
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
            ig.AccessToken);
        if (data == null) return StatusCode(502, new { message = "Failed to fetch Instagram insights" });
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
            ig.AccessToken);
        if (data == null) return StatusCode(502, new { message = "Failed to fetch Instagram media" });
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
            ig.AccessToken);
        if (data == null) return StatusCode(502, new { message = "Failed to fetch media insights" });
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
            ig.AccessToken);
        if (data == null) return StatusCode(502, new { message = "Failed to fetch media comments" });
        return Ok(data);
    }

    // ── POST /api/social/instagram/sentiment ──────────────────────────────────

    [HttpPost("instagram/sentiment")]
    public async Task<IActionResult> AnalyzeSentiment([FromBody] SentimentRequestDto dto)
    {
        var ig = await GetInstagramAccountAsync();
        if (ig == null) return NotFound("No Instagram account");

        var igUrl = $"https://graph.facebook.com/v20.0/{dto.PostId}/comments" +
                    $"?fields=text&limit=50&access_token={ig.AccessToken}";
        var igRes = await _http.GetFromJsonAsync<JsonElement>(igUrl);

        var comments = igRes.GetProperty("data")
            .EnumerateArray()
            .Select(c => c.GetProperty("text").GetString() ?? "")
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();

        if (comments.Count == 0)
            return Ok(new { positive = 0, negative = 0, neutral = 0, total = 0, summary = "No comments." });

        var payload = new { comments, caption = dto.Caption };
        var response = await _http.PostAsJsonAsync("http://localhost:8001/analyze", payload);
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
            ig.AccessToken);
        if (data == null) return StatusCode(502, new { message = "Failed to fetch audience data" });
        return Ok(data);
    }

    // ── GET /api/social/instagram/summary ─────────────────────────────────────

    [HttpGet("instagram/summary")]
    public async Task<IActionResult> GetSummary([FromQuery] int clientId = 0, [FromQuery] int accountId = 0)
    {
        var ig = await GetInstagramAccountAsync(clientId, accountId);
        if (ig == null)
            return NotFound(new { message = "Instagram account not connected. Please connect your account in the Accounts section." });

        var igPageId    = ig.AccountId!;
        var accessToken = ig.AccessToken;

        var overviewTask = GetGraph($"{igPageId}?fields=followers_count,media_count,name,profile_picture_url", accessToken);
        var insightsTask = GetGraph($"{igPageId}/insights?metric=reach,follower_count&period=day&since={DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds()}&until={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}", accessToken);
        var mediaTask = GetGraph($"{igPageId}/media?fields=id,caption,media_type,timestamp,like_count,comments_count,permalink,media_url,thumbnail_url&limit=6", accessToken);

        await Task.WhenAll(overviewTask, insightsTask, mediaTask);

        var overview = await overviewTask;
        var insights = await insightsTask;
        var media = await mediaTask;

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

                var postComments = new List<object>();
                if (!string.IsNullOrWhiteSpace(mediaId) && commentCount > 0)
                {
                    var commentsData = await GetGraph(
                        $"{mediaId}/comments?fields=id,text,timestamp,username&limit=50", accessToken);
                    if (commentsData.HasValue && commentsData.Value.TryGetProperty("data", out var commentsArray))
                    {
                        foreach (var comment in commentsArray.EnumerateArray())
                        {
                            postComments.Add(new
                            {
                                id = comment.TryGetProperty("id", out var cid) ? cid.GetString() : "",
                                text = comment.TryGetProperty("text", out var txt) ? txt.GetString() : "",
                                timestamp = comment.TryGetProperty("timestamp", out var cts) ? cts.GetString() : "",
                                username = comment.TryGetProperty("username", out var un) ? un.GetString() : "",
                            });
                        }
                    }
                }

                topPosts.Add(new { id = mediaId, caption, mediaType, mediaUrl, timestamp, likeCount, commentCount, permalink, comments = postComments });
                totalLikes += likeCount;
                totalComments += commentCount;
            }
        }

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
                    if (metricName == "reach") reachTimeline.Add(new { date = endTime, value });
                    else if (metricName == "follower_count") followerTimeline.Add(new { date = endTime, value });
                }
            }
        }

        var followers = overview.HasValue && overview.Value.TryGetProperty("followers_count", out var fc) ? fc.GetInt32() : 0;
        var mediaCount = overview.HasValue && overview.Value.TryGetProperty("media_count", out var mc) ? mc.GetInt32() : 0;
        var name = overview.HasValue && overview.Value.TryGetProperty("name", out var nm) ? nm.GetString() : "";
        var picture = overview.HasValue && overview.Value.TryGetProperty("profile_picture_url", out var pp) ? pp.GetString() : "";

        ig.FollowersCount = followers;
        ig.ProfilePicture = picture;
        ig.Username = name;
        await _db.SaveChangesAsync();

        return Ok(new { followers, mediaCount, name, profilePicture = picture, reachTimeline, followerTimeline, topPosts, totalLikes, totalComments });
    }

    // ── Facebook helpers ──────────────────────────────────────────────────────

    private async Task<SocialAccount?> GetFacebookAccountAsync(int clientId = 0, int accountId = 0)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return null;
        return await _db.SocialAccounts
            .FirstOrDefaultAsync(a =>
                a.Client.UserId == user.Id &&
                (clientId  == 0 || a.ClientId == clientId) &&
                (accountId == 0 || a.Id       == accountId) &&
                a.PlatformId == 2 &&
                a.IsConnected &&
                !string.IsNullOrEmpty(a.AccessToken));
    }

    // ── GET /api/social/facebook/summary ──────────────────────────────────────

    [HttpGet("facebook/summary")]
    public async Task<IActionResult> GetFacebookSummary([FromQuery] int clientId = 0, [FromQuery] int accountId = 0)
    {
        var fb = await GetFacebookAccountAsync(clientId, accountId);
        if (fb == null)
            return NotFound(new { message = "Facebook account not connected. Please connect your Page in the Accounts section." });

        var pageId = fb.AccountId!;
        var accessToken = fb.AccessToken;

        var overviewTask = GetGraph($"{pageId}?fields=name,fan_count,followers_count,picture.type(large),about,website", accessToken);
        var insightsTask = GetGraph($"{pageId}/insights?metric=page_impressions,page_engaged_users,page_post_engagements,page_fan_adds&period=day&since={DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds()}&until={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}", accessToken);
        var postsTask = GetGraph($"{pageId}/posts?fields=id,message,story,created_time,full_picture,permalink_url&limit=6", accessToken);

        await Task.WhenAll(overviewTask, insightsTask, postsTask);

        var overview = await overviewTask;
        var insights = await insightsTask;
        var posts = await postsTask;

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

    // ── GET /api/social/tiktok/auth ───────────────────────────────────────────

    [HttpGet("tiktok/auth")]
    [AllowAnonymous]
    public IActionResult TikTokAuth([FromQuery] string token = "", [FromQuery] int clientId = 0)
    {
        var clientKey = _config["TikTok:ClientKey"];
        var redirectUri = _config["TikTok:RedirectUri"];
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);

        // Extract email from JWT to embed in state
        string email = "";
        try
        {
            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var parsed = handler.ReadJwtToken(token);
            email = parsed.Claims.FirstOrDefault(c =>
                c.Type == ClaimTypes.Name ||
                c.Type == "unique_name" ||
                c.Type == "email")?.Value ?? "";
        }
        catch { }

        // Encode email + verifier in state (base64)
        var stateData = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{email}||{codeVerifier}||{clientId}")
        ).Replace("+", "-").Replace("/", "_").Replace("=", "");

        var url = "https://www.tiktok.com/v2/auth/authorize/" +
            $"?client_key={clientKey}" +
            $"&scope=user.info.basic,user.info.stats,video.list" +
            $"&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri!)}" +
            $"&state={stateData}" +
            $"&code_challenge={codeChallenge}" +
            $"&code_challenge_method=S256";

        return Redirect(url);
    }

    // ── GET /api/social/tiktok/callback ──────────────────────────────────────

    [HttpGet("tiktok/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> TikTokCallback([FromQuery] string code, [FromQuery] string state)
    {
        var clientKey = _config["TikTok:ClientKey"];
        var clientSecret = _config["TikTok:ClientSecret"];
        var redirectUri = _config["TikTok:RedirectUri"];

        // ── Decode email + verifier + clientId from state ────────────────
        string email = "";
        string codeVerifier = "";
        int tikTokClientId = 0;
        try
        {
            // TikTok may URL-encode the state — decode it first
            var raw = Uri.UnescapeDataString(state ?? "");
            var padded = raw.Replace("-", "+").Replace("_", "/");
            var pad = padded.Length % 4;
            if (pad > 0) padded += new string('=', 4 - pad);
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            Console.WriteLine($"[TikTok Callback] decoded state: {decoded}");
            var parts = decoded.Split("||");
            email = parts.Length > 0 ? parts[0] : "";
            codeVerifier = parts.Length > 1 ? parts[1] : "";
            if (parts.Length > 2) int.TryParse(parts[2], out tikTokClientId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TikTok Callback] state decode error: {ex.Message} | raw state: {state}");
        }

        if (string.IsNullOrEmpty(email))
            return Redirect("http://localhost:5173/dashboard?error=tiktok_auth_failed");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null)
            return Redirect("http://localhost:5173/dashboard?error=tiktok_auth_failed");

        // ── Exchange code for token ───────────────────────────────────────
        var tokenRes = await _http.PostAsync("https://open.tiktokapis.com/v2/oauth/token/",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_key"] = clientKey!,
                ["client_secret"] = clientSecret!,
                ["code"] = code,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri!,
                ["code_verifier"] = codeVerifier,
            }));

        var tokenRaw = await tokenRes.Content.ReadAsStringAsync();
        Console.WriteLine($"[TikTok Callback] token response: {tokenRaw}");

        JsonElement tokenJson;
        try { tokenJson = JsonDocument.Parse(tokenRaw).RootElement; }
        catch { return Redirect("http://localhost:5173/dashboard?error=tiktok_token_failed"); }

        var accessToken = tokenJson.TryGetProperty("access_token", out var at) ? at.GetString() ?? "" : "";
        var refreshToken = tokenJson.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? "" : "";
        var openId = tokenJson.TryGetProperty("open_id", out var oi) ? oi.GetString() ?? "" : "";

        if (string.IsNullOrEmpty(accessToken))
            return Redirect("http://localhost:5173/dashboard?error=tiktok_token_failed");

        // ── Fetch user info ───────────────────────────────────────────────
        var userReq = new HttpRequestMessage(HttpMethod.Get,
            "https://open.tiktokapis.com/v2/user/info/?fields=open_id,display_name,avatar_url,follower_count,following_count,likes_count,video_count");
        userReq.Headers.Add("Authorization", $"Bearer {accessToken}");
        var userRes = await _http.SendAsync(userReq);
        var userRaw = await userRes.Content.ReadAsStringAsync();
        Console.WriteLine($"[TikTok Callback] user info: {userRaw}");

        string ttUsername = "";
        string ttPicture = "";
        try
        {
            var userJson = JsonDocument.Parse(userRaw).RootElement;
            var userData = userJson.GetProperty("data").GetProperty("user");
            ttUsername = userData.TryGetProperty("display_name", out var dn) ? dn.GetString() ?? "" : "";
            ttPicture = userData.TryGetProperty("avatar_url", out var av) ? av.GetString() ?? "" : "";
        }
        catch { }

        // ── Save to DB ────────────────────────────────────────────────────
        var existing = await _db.SocialAccounts
            .FirstOrDefaultAsync(a => a.ClientId == tikTokClientId && a.PlatformId == 4);

        if (existing != null)
        {
            existing.AccessToken = accessToken;
            existing.RefreshToken = refreshToken;
            existing.AccountId = openId;
            existing.Username = ttUsername;
            existing.ProfilePicture = ttPicture;
            existing.IsConnected = true;
            existing.ConnectedAt = DateTime.UtcNow;
        }
        else
        {
            _db.SocialAccounts.Add(new SocialAccount
            {
                ClientId = tikTokClientId,
                PlatformId = 4,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                AccountId = openId,
                Username = ttUsername,
                ProfilePicture = ttPicture,
                IsConnected = true,
                ConnectedAt = DateTime.UtcNow,
            });
        }

        await _db.SaveChangesAsync();
        return Redirect("http://localhost:5173/dashboard?connected=tiktok");
    }

    // ── GET /api/social/tiktok/summary ───────────────────────────────────────

    [HttpGet("tiktok/summary")]
    public async Task<IActionResult> GetTikTokSummary([FromQuery] int clientId = 0, [FromQuery] int accountId = 0)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var account = await _db.SocialAccounts
            .FirstOrDefaultAsync(a =>
                a.Client.UserId == user.Id &&
                (clientId  == 0 || a.ClientId == clientId) &&
                (accountId == 0 || a.Id       == accountId) &&
                a.PlatformId == 4 &&
                a.IsConnected);
        if (account == null)
            return NotFound(new { message = "TikTok account not connected" });

        // Fetch user stats
        var userReq = new HttpRequestMessage(HttpMethod.Get,
            "https://open.tiktokapis.com/v2/user/info/?fields=open_id,display_name,avatar_url,follower_count,following_count,likes_count,video_count");
        userReq.Headers.Add("Authorization", $"Bearer {account.AccessToken}");
        var userRes = await _http.SendAsync(userReq);
        var userRaw = await userRes.Content.ReadAsStringAsync();
        var userJson = JsonDocument.Parse(userRaw).RootElement;
        var userData = userJson.GetProperty("data").GetProperty("user");

        // Fetch recent videos
        var videoReq = new HttpRequestMessage(HttpMethod.Post,
            "https://open.tiktokapis.com/v2/video/list/?fields=id,title,cover_image_url,share_url,view_count,like_count,comment_count,share_count,create_time");
        videoReq.Headers.Add("Authorization", $"Bearer {account.AccessToken}");
        videoReq.Content = new StringContent(JsonSerializer.Serialize(new { max_count = 10 }), Encoding.UTF8, "application/json");
        var videoRes = await _http.SendAsync(videoReq);
        var videoRaw = await videoRes.Content.ReadAsStringAsync();
        var videoJson = JsonDocument.Parse(videoRaw).RootElement;

        var videos = new List<object>();
        var totalViews = 0;
        var totalLikes = 0;
        var totalComments = 0;
        var totalShares = 0;

        if (videoJson.TryGetProperty("data", out var videoData) &&
            videoData.TryGetProperty("videos", out var videoList))
        {
            foreach (var v in videoList.EnumerateArray())
            {
                var views = v.TryGetProperty("view_count", out var vc) ? vc.GetInt32() : 0;
                var likes = v.TryGetProperty("like_count", out var lc) ? lc.GetInt32() : 0;
                var comments = v.TryGetProperty("comment_count", out var cc) ? cc.GetInt32() : 0;
                var shares = v.TryGetProperty("share_count", out var sc) ? sc.GetInt32() : 0;

                totalViews += views;
                totalLikes += likes;
                totalComments += comments;
                totalShares += shares;

                videos.Add(new
                {
                    id = v.TryGetProperty("id", out var id) ? id.GetString() : "",
                    title = v.TryGetProperty("title", out var ti) ? ti.GetString() : "",
                    coverUrl = v.TryGetProperty("cover_image_url", out var cu) ? cu.GetString() : "",
                    shareUrl = v.TryGetProperty("share_url", out var su) ? su.GetString() : "",
                    createTime = v.TryGetProperty("create_time", out var ct) ? ct.GetInt64().ToString() : "",
                    viewCount = views,
                    likeCount = likes,
                    commentCount = comments,
                    shareCount = shares,
                });
            }
        }

        var followers = userData.TryGetProperty("follower_count", out var fc) ? fc.GetInt32() : 0;
        var following = userData.TryGetProperty("following_count", out var fg) ? fg.GetInt32() : 0;
        var totalAccountLikes = userData.TryGetProperty("likes_count", out var lk) ? lk.GetInt32() : 0;
        var videoCount = userData.TryGetProperty("video_count", out var vn) ? vn.GetInt32() : 0;
        var name = userData.TryGetProperty("display_name", out var nm) ? nm.GetString() : "";
        var picture = userData.TryGetProperty("avatar_url", out var av) ? av.GetString() : "";

        account.FollowersCount = followers;
        account.ProfilePicture = picture;
        account.Username = name;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            followers,
            following,
            likes = totalAccountLikes,
            videoCount,
            name,
            profilePicture = picture,
            recentVideos = videos,
            totalViews,
            totalLikes,
            totalComments,
            totalShares,
            viewsTimeline = new List<object>(),
            likesTimeline = new List<object>(),
        });
    }

    // ── GET /api/social/accounts/tokens ──────────────────────────────────────

    [HttpGet("accounts/tokens")]
    [AllowAnonymous]
    public async Task<IActionResult> GetTokens()
    {
        var accounts = await _db.SocialAccounts
            .Where(s => s.IsConnected)
            .Select(s => new {
                s.Id,
                s.PlatformId,
                Platform = s.Platform.Name,
                s.AccessToken,
                s.RefreshToken,
                s.AccountId,
                s.Username
            })
            .ToListAsync();
        return Ok(accounts);
    }

    [HttpPatch("accounts/tokens/{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> UpdateToken(int id, [FromBody] UpdateTokenDto dto)
    {
        var account = await _db.SocialAccounts.FindAsync(id);
        if (account == null) return NotFound();
        account.AccessToken = dto.AccessToken;
        if (dto.RefreshToken != null) account.RefreshToken = dto.RefreshToken;
        account.ConnectedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Token updated" });
    }

    // ── POST /api/social/tokens/refresh-all — called by n8n on schedule ────────

    [HttpPost("tokens/refresh-all")]
    [AllowAnonymous]
    public async Task<IActionResult> RefreshAllTokens()
    {
        var accounts = await _db.SocialAccounts
            .Where(a => a.IsConnected)
            .ToListAsync();

        var refreshed = new List<object>();
        var failed    = new List<object>();

        // ── Facebook / Instagram (platformId 1 & 2): extend long-lived token ──
        var fbAppId     = _config["Facebook:AppId"];
        var fbAppSecret = _config["Facebook:AppSecret"];
        var fbAccounts  = accounts.Where(a => a.PlatformId == 1 || a.PlatformId == 2).ToList();

        if (!string.IsNullOrEmpty(fbAppId) && !string.IsNullOrEmpty(fbAppSecret))
        {
            foreach (var acc in fbAccounts)
            {
                if (string.IsNullOrEmpty(acc.AccessToken)) continue;
                try
                {
                    var url = $"https://graph.facebook.com/v20.0/oauth/access_token" +
                              $"?grant_type=fb_exchange_token" +
                              $"&client_id={fbAppId}" +
                              $"&client_secret={fbAppSecret}" +
                              $"&fb_exchange_token={acc.AccessToken}";

                    var res = await _http.GetAsync(url);
                    var raw = await res.Content.ReadAsStringAsync();
                    var json = JsonDocument.Parse(raw).RootElement;

                    if (json.TryGetProperty("access_token", out var newToken))
                    {
                        acc.AccessToken  = newToken.GetString()!;
                        acc.ConnectedAt  = DateTime.UtcNow;
                        refreshed.Add(new { acc.Id, acc.Username, platform = acc.PlatformId == 1 ? "instagram" : "facebook" });
                    }
                    else
                    {
                        var err = json.TryGetProperty("error", out var e) ? e.ToString() : raw;
                        failed.Add(new { acc.Id, acc.Username, error = err });
                    }
                }
                catch (Exception ex)
                {
                    failed.Add(new { acc.Id, acc.Username, error = ex.Message });
                }
            }
        }

        // ── TikTok (platformId 4): use refresh_token to get new access_token ──
        var ttKey     = _config["TikTok:ClientKey"];
        var ttSecret  = _config["TikTok:ClientSecret"];
        var ttAccounts = accounts.Where(a => a.PlatformId == 4 && !string.IsNullOrEmpty(a.RefreshToken)).ToList();

        foreach (var acc in ttAccounts)
        {
            try
            {
                var body = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_key"]     = ttKey!,
                    ["client_secret"]  = ttSecret!,
                    ["grant_type"]     = "refresh_token",
                    ["refresh_token"]  = acc.RefreshToken!,
                });

                var res = await _http.PostAsync("https://open.tiktokapis.com/v2/oauth/token/", body);
                var raw = await res.Content.ReadAsStringAsync();
                var json = JsonDocument.Parse(raw).RootElement;

                if (json.TryGetProperty("access_token", out var newAt))
                {
                    acc.AccessToken  = newAt.GetString()!;
                    if (json.TryGetProperty("refresh_token", out var newRt))
                        acc.RefreshToken = newRt.GetString();
                    acc.ConnectedAt = DateTime.UtcNow;
                    refreshed.Add(new { acc.Id, acc.Username, platform = "tiktok" });
                }
                else
                {
                    var err = json.TryGetProperty("error", out var e) ? e.ToString() : raw;
                    failed.Add(new { acc.Id, acc.Username, error = err });
                }
            }
            catch (Exception ex)
            {
                failed.Add(new { acc.Id, acc.Username, error = ex.Message });
            }
        }

        await _db.SaveChangesAsync();

        return Ok(new
        {
            refreshedCount = refreshed.Count,
            failedCount    = failed.Count,
            refreshed,
            failed,
            runAt = DateTime.UtcNow,
        });
    }

    // ── PKCE Helpers ──────────────────────────────────────────────────────────

    private static string GenerateCodeVerifier()
    {
        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
    }

    private static string GenerateCodeChallenge(string verifier)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(verifier));
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
    }
}