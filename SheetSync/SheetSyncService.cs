using AutoGenerate.Shared.Data;
using AutoGenerate.Shared.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AutoGenerate.SheetSync;

// ── Column header aliases (normalized: lowercase, no accents) ──────────────────
// The sheet has two header rows:
//   Row 1 : group labels (TEXTE / PROGRAMMATION / SPONSORISATION)
//   Row 2 : actual column names (N° post, Texte posts rédigé, …)
// We find the real header row by looking for the row that contains "n post" (normalised "N° post").

public class SheetSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory   _http;
    private readonly ILogger<SheetSyncService> _log;
    private static readonly TimeSpan POLL_INTERVAL = TimeSpan.FromMinutes(5);

    public SheetSyncService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory   http,
        ILogger<SheetSyncService> log)
    {
        _scopeFactory = scopeFactory;
        _http         = http;
        _log          = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Wait a bit at startup so the DB is ready
        await Task.Delay(TimeSpan.FromSeconds(15), ct);

        while (!ct.IsCancellationRequested)
        {
            try { await SyncAllClientsAsync(ct); }
            catch (Exception ex) { _log.LogError(ex, "Sheet sync cycle failed"); }

            await Task.Delay(POLL_INTERVAL, ct);
        }
    }

    // Called by the background loop AND by the manual endpoint
    public async Task<SyncResult> SyncClientAsync(int clientId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == clientId, ct);
        if (client == null || string.IsNullOrEmpty(client.SheetUrl))
            return new SyncResult { Error = "Client not found or no sheet URL" };

        return await SyncOneClientAsync(db, client, ct);
    }

    private async Task SyncAllClientsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var clients = await db.Clients
            .Where(c => c.SheetUrl != null && c.SheetUrl != "")
            .ToListAsync(ct);

        foreach (var client in clients)
        {
            try { await SyncOneClientAsync(db, client, ct); }
            catch (Exception ex) { _log.LogWarning(ex, "Failed to sync sheet for client {ClientId}", client.Id); }
        }
    }

    private async Task<SyncResult> SyncOneClientAsync(AppDbContext db, Client client, CancellationToken ct)
    {
        var csv = await FetchCsvAsync(client.SheetUrl!, ct);
        if (csv == null)
            return new SyncResult { Error = "Could not fetch sheet (check public sharing)" };

        var rows = ParseSheet(csv);
        if (rows.Count == 0)
            return new SyncResult { Error = "Sheet is empty or could not be parsed" };

        var defaultUserId = await db.Users.Select(u => u.Id).FirstOrDefaultAsync(ct);
        if (defaultUserId == 0)
            return new SyncResult { Error = "No users found in database" };

        // Load all existing SheetRows for this client
        var existing = await db.SheetRows
            .Where(sr => sr.ClientId == client.Id)
            .ToListAsync(ct);
        var existingByKey = existing.ToDictionary(sr => sr.RowKey);

        var result = new SyncResult();
        var seenKeys = new HashSet<string>();

        foreach (var row in rows)
        {
            var key     = row.RowKey;
            var hash    = ComputeHash(row.RawValues);
            var caption = row.Caption;

            seenKeys.Add(key);

            if (existingByKey.TryGetValue(key, out var tracked))
            {
                // Row already known — check if it changed
                if (tracked.ContentHash == hash) continue;

                tracked.ContentHash = hash;
                tracked.LastSeenAt  = DateTime.UtcNow;

                if (string.IsNullOrWhiteSpace(caption))
                {
                    // Caption was cleared → mark post as Draft (cancelled)
                    if (tracked.PostId.HasValue)
                    {
                        var post = await db.Posts.FindAsync(new object[] { tracked.PostId.Value }, ct);
                        if (post != null) post.Status = PostStatus.Draft;
                        result.Cancelled++;

                        db.Notifications.Add(new Notification
                        {
                            Title     = $"Post annulé : {client.Name}",
                            Message   = $"📊 Le post N°{key} a été annulé dans le planning.",
                            Type      = "sheet_sync",
                            ReferenceId = client.Id.ToString(),
                            IsRead    = false,
                            CreatedAt = DateTime.UtcNow,
                        });
                    }
                }
                else
                {
                    // Caption changed → update the post
                    if (tracked.PostId.HasValue)
                    {
                        await UpdatePostAsync(db, tracked.PostId.Value, row, client.Id, defaultUserId, ct);
                        result.Updated++;

                        db.Notifications.Add(new Notification
                        {
                            Title     = $"Post mis à jour : {client.Name}",
                            Message   = BuildSyncNotifMessage(key, row),
                            Type      = "sheet_sync",
                            ReferenceId = client.Id.ToString(),
                            IsRead    = false,
                            CreatedAt = DateTime.UtcNow,
                        });
                    }
                }
            }
            else
            {
                // New row
                var sheetRow = new SheetRow
                {
                    ClientId    = client.Id,
                    RowKey      = key,
                    ContentHash = hash,
                    LastSeenAt  = DateTime.UtcNow,
                };
                db.SheetRows.Add(sheetRow);

                if (!string.IsNullOrWhiteSpace(caption))
                {
                    var post = await CreatePostAsync(db, row, client.Id, defaultUserId, key, ct);
                    sheetRow.PostId = post.Id;
                    result.Created++;

                    db.Notifications.Add(new Notification
                    {
                        Title     = $"Nouveau post : {client.Name}",
                        Message   = BuildSyncNotifMessage(key, row),
                        Type      = "sheet_sync",
                        ReferenceId = client.Id.ToString(),
                        IsRead    = false,
                        CreatedAt = DateTime.UtcNow,
                    });
                }
            }
        }

        // Rows that disappeared from the sheet → remove them (cancel posts if any)
        foreach (var (key, tracked) in existingByKey)
        {
            if (!seenKeys.Contains(key))
            {
                if (tracked.PostId.HasValue)
                {
                    var post = await db.Posts.FindAsync(new object[] { tracked.PostId.Value }, ct);
                    if (post != null) post.Status = PostStatus.Draft;
                    result.Cancelled++;
                }
                db.SheetRows.Remove(tracked);
            }
        }

        client.SheetLastSyncAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        _log.LogInformation("Sheet sync client {ClientId}: +{C} updated {U} cancelled {X}",
            client.Id, result.Created, result.Updated, result.Cancelled);

        return result;
    }

    // ── Post create / update ─────────────────────────────────────────────────

    private async Task<Post> CreatePostAsync(
        AppDbContext db, SheetDataRow row, int clientId, int userId, string sheetRowKey, CancellationToken ct)
    {
        var topic = await GetOrCreateTopicAsync(db, clientId, userId, row.TopicName ?? "Planning", row.Platforms, ct);

        var post = new Post
        {
            TopicId     = topic.Id,
            ClientId    = clientId,
            UserId      = userId,
            Status      = PostStatus.InReview,
            ScheduledAt = ParseScheduledAt(row.Date, row.Time),
            SheetRowKey = sheetRowKey,
            BriefData   = JsonSerializer.Serialize(row.Brief),
            CreatedAt   = DateTime.UtcNow,
        };
        db.Posts.Add(post);
        await db.SaveChangesAsync(ct);

        db.Captions.Add(new AutoGenerate.CaptionService.Models.Caption
        {
            PostId        = post.Id,
            Content       = row.Caption,
            Hashtags      = row.Tags,
            Platforms     = JsonSerializer.Serialize(row.Platforms),
            ToneOfVoice   = "Casual",
            CaptionLength = "Medium",
            GeneratedBy   = "import",
            Version       = 1,
            IsSelected    = true,
        });

        if (!string.IsNullOrEmpty(row.ImageInfo))
        {
            // ImageInfo might be a URL or a description — store as-is
            if (row.ImageInfo.StartsWith("http"))
            {
                var img = new PostImage { PostId = post.Id };
                img.SetUrls(new List<string> { row.ImageInfo });
                db.PostImages.Add(img);
            }
        }

        await db.SaveChangesAsync(ct);
        return post;
    }

    private async Task UpdatePostAsync(
        AppDbContext db, int postId, SheetDataRow row, int clientId, int userId, CancellationToken ct)
    {
        var post = await db.Posts
            .Include(p => p.Captions)
            .FirstOrDefaultAsync(p => p.Id == postId, ct);
        if (post == null) return;

        post.ScheduledAt = ParseScheduledAt(row.Date, row.Time);
        post.BriefData   = JsonSerializer.Serialize(row.Brief);
        post.Status      = PostStatus.InReview;

        var cap = post.Captions.FirstOrDefault(c => c.IsSelected) ?? post.Captions.FirstOrDefault();
        if (cap != null)
        {
            cap.Content   = row.Caption;
            cap.Hashtags  = row.Tags;
            cap.Platforms = JsonSerializer.Serialize(row.Platforms);
        }
        else
        {
            db.Captions.Add(new AutoGenerate.CaptionService.Models.Caption
            {
                PostId      = postId,
                Content     = row.Caption,
                Hashtags    = row.Tags,
                Platforms   = JsonSerializer.Serialize(row.Platforms),
                ToneOfVoice = "Casual",
                CaptionLength = "Medium",
                GeneratedBy = "import",
                Version     = 1,
                IsSelected  = true,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<Topic> GetOrCreateTopicAsync(
        AppDbContext db, int clientId, int userId, string name, List<string> platforms, CancellationToken ct)
    {
        var topic = await db.Topics
            .FirstOrDefaultAsync(t => t.ClientId == clientId && t.Name == name, ct);
        if (topic != null) return topic;

        topic = new Topic
        {
            ClientId  = clientId,
            UserId    = userId,
            Name      = name,
            Platform  = platforms.Count == 1 ? platforms[0] : "multi",
            CreatedAt = DateTime.UtcNow,
        };
        db.Topics.Add(topic);
        await db.SaveChangesAsync(ct);
        return topic;
    }

    // ── CSV fetch ────────────────────────────────────────────────────────────

    private async Task<string?> FetchCsvAsync(string sheetUrl, CancellationToken ct)
    {
        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);

        var gsMatch = Regex.Match(sheetUrl, @"spreadsheets/d/([a-zA-Z0-9_-]+)");
        if (gsMatch.Success)
        {
            var id       = gsMatch.Groups[1].Value;
            var gidMatch = Regex.Match(sheetUrl, @"[?&#]gid=(\d+)");
            var gidParam = gidMatch.Success ? $"&gid={gidMatch.Groups[1].Value}" : "";
            var csvUrl   = $"https://docs.google.com/spreadsheets/d/{id}/export?format=csv{gidParam}";
            try
            {
                var resp = await client.GetAsync(csvUrl, ct);
                if (resp.IsSuccessStatusCode)
                    return await resp.Content.ReadAsStringAsync(ct);
            }
            catch { }
            return null;
        }

        // OneDrive: try download=1
        if (sheetUrl.Contains("onedrive.live.com") || sheetUrl.Contains("1drv.ms"))
        {
            var sep   = sheetUrl.Contains('?') ? "&" : "?";
            var dlUrl = sheetUrl + sep + "download=1";
            try
            {
                var resp = await client.GetAsync(dlUrl, ct);
                if (resp.IsSuccessStatusCode)
                {
                    var ct2 = resp.Content.Headers.ContentType?.MediaType ?? "";
                    if (ct2.Contains("text/csv") || ct2.Contains("text/plain"))
                        return await resp.Content.ReadAsStringAsync(ct);
                }
            }
            catch { }
        }

        return null;
    }

    // ── Sheet parser ─────────────────────────────────────────────────────────
    // The sheet has:
    //   Row 1 : group headers (TEXTE / PROGRAMMATION / SPONSORISATION)
    //   Row 2 : column names
    //   Row 3+: data
    // We detect the header row by looking for the normalised keyword "n post" (from "N° post").

    private static List<SheetDataRow> ParseSheet(string csv)
    {
        // Proper row split that respects quoted multiline cells
        var lines = SplitCsvRows(csv);

        // Find header row — scan up to 30 rows to skip intro/context rows the client may have
        int headerIdx = -1;
        for (int i = 0; i < Math.Min(lines.Count, 30); i++)
        {
            var cols = ParseCsvLine(lines[i]);
            if (cols.Any(c => Norm(c).Contains("n post") || Norm(c).Contains("numero") || Norm(c) == "n"))
            { headerIdx = i; break; }
        }
        if (headerIdx < 0) return new(); // could not find header row

        var headers = ParseCsvLine(lines[headerIdx]).Select(Norm).ToArray();

        // Column indices
        int idxNo      = FindCol(headers, "n post", "n°", "numero", "num");
        int idxBrief   = FindCol(headers, "details", "brief", "creatif", "crea");
        int idxCharte  = FindCol(headers, "charte");
        int idxFormat  = FindCol(headers, "format");
        int idxTexteV  = FindCol(headers, "texte a integrer", "texte integrer", "integrer dans le visuel");
        int idxImages  = FindCol(headers, "images", "photos");
        int idxInfos   = FindCol(headers, "infos a integrer", "infos integrer");
        int idxCaption = FindCol(headers, "texte posts", "texte post", "posts redige", "redige");
        int idxDate    = FindCol(headers, "date de publication", "date pub", "date");
        int idxTime    = FindCol(headers, "heure de publication", "heure pub", "heure");
        int idxPages   = FindCol(headers, "pages", "publier", "fb", "ig");
        int idxBudget  = FindCol(headers, "budget");
        int idxObj     = FindCol(headers, "objectif");
        int idxDupIG   = FindCol(headers, "duplication", "instagram a cocher");
        int idxSponsoEnd = FindCol(headers, "fin de la sponso", "fin sponso");
        int idxAudience = FindCol(headers, "audience");
        int idxTags    = FindCol(headers, "tags", "tag");
        int idxComments = FindCol(headers, "commentaires", "comments");

        var result = new List<SheetDataRow>();

        for (int i = headerIdx + 1; i < lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var cols = ParseCsvLine(lines[i]);
            if (cols.All(c => string.IsNullOrWhiteSpace(c))) continue;

            var rowNo   = GetCol(cols, idxNo).Trim();
            var caption = GetCol(cols, idxCaption).Trim();
            var pages   = GetCol(cols, idxPages).Trim();

            // Sub-row: N° cell is merged across multiple rows in the sheet.
            // In CSV export, only the first row keeps the number — subsequent rows
            // have blank N°. Merge those sub-rows into the last result entry.
            if (string.IsNullOrEmpty(rowNo))
            {
                if (result.Count > 0)
                {
                    var last = result[result.Count - 1];
                    if (string.IsNullOrEmpty(last.Caption) && !string.IsNullOrEmpty(caption))
                        last.Caption = caption;
                    if (string.IsNullOrEmpty(last.Date))      last.Date      = GetCol(cols, idxDate).Trim();
                    if (string.IsNullOrEmpty(last.Time))      last.Time      = GetCol(cols, idxTime).Trim();
                    if (string.IsNullOrEmpty(last.Tags))      last.Tags      = GetCol(cols, idxTags).Trim();
                    if (string.IsNullOrEmpty(last.ImageInfo)) last.ImageInfo = GetCol(cols, idxImages).Trim();
                    if (last.Platforms.Count == 0 && !string.IsNullOrEmpty(pages))
                        last.Platforms = ParsePlatforms(pages);
                    if (last.Brief != null)
                    {
                        if (string.IsNullOrEmpty(last.Brief.Brief))        last.Brief.Brief        = GetCol(cols, idxBrief).Trim();
                        if (string.IsNullOrEmpty(last.Brief.Charte))       last.Brief.Charte       = GetCol(cols, idxCharte).Trim();
                        if (string.IsNullOrEmpty(last.Brief.Format))       last.Brief.Format       = GetCol(cols, idxFormat).Trim();
                        if (string.IsNullOrEmpty(last.Brief.TexteVisuel))  last.Brief.TexteVisuel  = GetCol(cols, idxTexteV).Trim();
                        if (string.IsNullOrEmpty(last.Brief.ImagesPhotos)) last.Brief.ImagesPhotos = GetCol(cols, idxImages).Trim();
                        if (string.IsNullOrEmpty(last.Brief.InfosPost))    last.Brief.InfosPost    = GetCol(cols, idxInfos).Trim();
                        if (string.IsNullOrEmpty(last.Brief.Budget))       last.Brief.Budget       = GetCol(cols, idxBudget).Trim();
                        if (string.IsNullOrEmpty(last.Brief.Objectif))     last.Brief.Objectif     = GetCol(cols, idxObj).Trim();
                        if (string.IsNullOrEmpty(last.Brief.Audience))     last.Brief.Audience     = GetCol(cols, idxAudience).Trim();
                        if (string.IsNullOrEmpty(last.Brief.Commentaires)) last.Brief.Commentaires = GetCol(cols, idxComments).Trim();
                        if (string.IsNullOrEmpty(last.Brief.DuplicateIG))  last.Brief.DuplicateIG  = GetCol(cols, idxDupIG).Trim();
                        if (string.IsNullOrEmpty(last.Brief.SponsoEndDate))last.Brief.SponsoEndDate= GetCol(cols, idxSponsoEnd).Trim();
                        if (string.IsNullOrEmpty(last.Brief.Pages) && !string.IsNullOrEmpty(pages))
                            last.Brief.Pages = pages;
                    }
                    last.RawValues += "|" + string.Join("|", cols);
                }
                continue;
            }

            var platforms = ParsePlatforms(pages);

            var brief = new BriefInfo
            {
                Brief           = GetCol(cols, idxBrief),
                Charte          = GetCol(cols, idxCharte),
                Format          = GetCol(cols, idxFormat),
                TexteVisuel     = GetCol(cols, idxTexteV),
                ImagesPhotos    = GetCol(cols, idxImages),
                InfosPost       = GetCol(cols, idxInfos),
                Budget          = GetCol(cols, idxBudget),
                Objectif        = GetCol(cols, idxObj),
                DuplicateIG     = GetCol(cols, idxDupIG),
                SponsoEndDate   = GetCol(cols, idxSponsoEnd),
                Audience        = GetCol(cols, idxAudience),
                Commentaires    = GetCol(cols, idxComments),
                Pages           = pages,
            };

            result.Add(new SheetDataRow
            {
                RowKey     = rowNo,
                Caption    = caption,
                Date       = GetCol(cols, idxDate).Trim(),
                Time       = GetCol(cols, idxTime).Trim(),
                Tags       = GetCol(cols, idxTags).Trim(),
                ImageInfo  = GetCol(cols, idxImages).Trim(),
                Platforms  = platforms,
                Brief      = brief,
                TopicName  = "Planning",
                RawValues  = string.Join("|", cols),
            });
        }

        return result;
    }

    private static List<string> ParsePlatforms(string raw)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "FB",       "facebook"  },
            { "Facebook", "facebook"  },
            { "IG",       "instagram" },
            { "Instagram","instagram" },
            { "LK",       "linkedin"  },
            { "LinkedIn", "linkedin"  },
            { "TK",       "tiktok"    },
            { "TikTok",   "tiktok"    },
            { "TW",       "twitter"   },
            { "Twitter",  "twitter"   },
        };

        var result = new List<string>();
        foreach (var part in Regex.Split(raw, @"[,;/\s]+"))
        {
            var p = part.Trim();
            if (string.IsNullOrEmpty(p)) continue;
            if (map.TryGetValue(p, out var normalized)) result.Add(normalized);
        }
        return result.Count > 0 ? result : new List<string> { "instagram" };
    }

    private static DateTime? ParseScheduledAt(string date, string time)
    {
        if (string.IsNullOrWhiteSpace(date)) return null;
        var combined = string.IsNullOrWhiteSpace(time) ? date : $"{date} {time}";
        if (DateTime.TryParse(combined, out var dt))
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        return null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string ComputeHash(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }

    private static string BuildSyncNotifMessage(string rowKey, SheetDataRow row)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"📌 N° {rowKey}");
        if (!string.IsNullOrEmpty(row.Caption))
            sb.AppendLine($"📝 {row.Caption[..Math.Min(120, row.Caption.Length)]}…");
        if (!string.IsNullOrEmpty(row.Date))
            sb.AppendLine($"📅 {row.Date}" + (row.Time.Length > 0 ? $" à {row.Time}" : ""));
        if (row.Platforms.Count > 0)
            sb.AppendLine($"📢 {string.Join(", ", row.Platforms)}");
        if (!string.IsNullOrEmpty(row.Brief?.Budget))
            sb.AppendLine($"💰 Budget : {row.Brief.Budget}");
        return sb.ToString().TrimEnd();
    }

    private static int FindCol(string[] headers, params string[] candidates)
    {
        for (int i = 0; i < headers.Length; i++)
            foreach (var c in candidates)
                if (headers[i].Contains(c)) return i;
        return -1;
    }

    private static string GetCol(List<string> cols, int idx)
        => idx >= 0 && idx < cols.Count ? cols[idx] : "";

    private static string Norm(string s)
        => Regex.Replace(
            s.Trim().ToLowerInvariant()
             .Replace("é", "e").Replace("è", "e").Replace("ê", "e")
             .Replace("à", "a").Replace("â", "a").Replace("ô", "o")
             .Replace("û", "u").Replace("î", "i").Replace("ç", "c")
             .Replace("°", "").Replace("№", ""),
            @"\s+", " ");

    // Splits the full CSV into logical rows, respecting quoted multiline cells
    private static List<string> SplitCsvRows(string csv)
    {
        var rows    = new List<string>();
        var current = new StringBuilder();
        bool inQ    = false;

        for (int i = 0; i < csv.Length; i++)
        {
            char c = csv[i];
            if (c == '"')
            {
                // Escaped quote inside quoted field
                if (inQ && i + 1 < csv.Length && csv[i + 1] == '"')
                { current.Append('"'); current.Append('"'); i++; }
                else { inQ = !inQ; current.Append(c); }
            }
            else if ((c == '\n' || c == '\r') && !inQ)
            {
                // Skip \r in \r\n pairs
                if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                rows.Add(current.ToString());
                current.Clear();
            }
            else current.Append(c);
        }
        if (current.Length > 0) rows.Add(current.ToString());
        return rows;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result  = new List<string>();
        var current = new StringBuilder();
        bool inQ    = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQ && i + 1 < line.Length && line[i + 1] == '"')
                { current.Append('"'); i++; }
                else inQ = !inQ;
            }
            else if ((c == ',' || c == ';') && !inQ)
            { result.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        result.Add(current.ToString());
        return result;
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

public class SyncResult
{
    public int     Created   { get; set; }
    public int     Updated   { get; set; }
    public int     Cancelled { get; set; }
    public string? Error     { get; set; }
}

public class SheetDataRow
{
    public string       RowKey    { get; set; } = "";
    public string       Caption   { get; set; } = "";
    public string       Date      { get; set; } = "";
    public string       Time      { get; set; } = "";
    public string       Tags      { get; set; } = "";
    public string       ImageInfo { get; set; } = "";
    public List<string> Platforms { get; set; } = new();
    public BriefInfo?   Brief     { get; set; }
    public string?      TopicName { get; set; }
    public string       RawValues { get; set; } = ""; // for hashing
}

public class BriefInfo
{
    public string? Brief           { get; set; }
    public string? Charte          { get; set; }
    public string? Format          { get; set; }
    public string? TexteVisuel     { get; set; }
    public string? ImagesPhotos    { get; set; }
    public string? InfosPost       { get; set; }
    public string? Budget          { get; set; }
    public string? Objectif        { get; set; }
    public string? DuplicateIG     { get; set; }
    public string? SponsoEndDate   { get; set; }
    public string? Audience        { get; set; }
    public string? Commentaires    { get; set; }
    public string? Pages           { get; set; }
}
