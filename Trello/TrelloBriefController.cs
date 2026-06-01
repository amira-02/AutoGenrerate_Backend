using AutoGenerate.Shared.Data;
using AutoGenerate.Shared.Models;
using AutoGenerate.SheetSync;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

[ApiController]
[Route("api/trello/briefs")]
[Authorize]
[Tags("Trello")]
public class TrelloBriefController : ControllerBase
{
    private readonly AppDbContext    _db;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _http;

    public TrelloBriefController(AppDbContext db, IConfiguration config, IHttpClientFactory http)
    {
        _db     = db;
        _config = config;
        _http   = http;
    }

    // GET /api/trello/briefs?clientId=X  — list assigned briefs for a client
    [HttpGet]
    public async Task<IActionResult> GetBriefs([FromQuery] int clientId, CancellationToken ct)
    {
        var briefs = await _db.TrelloBriefs
            .Where(b => b.ClientId == clientId)
            .OrderByDescending(b => b.AssignedAt)
            .Select(b => new
            {
                b.Id, b.CardId, b.Title, b.SheetUrl, b.Due,
                b.LabelsJson, b.CardUrl, b.AssignedAt,
                rowCount = _db.SheetRows.Count(sr => sr.ClientId == clientId),
            })
            .ToListAsync(ct);

        return Ok(briefs);
    }

    // GET /api/trello/briefs/{id}  — full detail: card info + sheet rows + Trello comments
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetBrief(int id, CancellationToken ct)
    {
        var brief = await _db.TrelloBriefs
            .Include(b => b.Client)
            .FirstOrDefaultAsync(b => b.Id == id, ct);

        if (brief == null) return NotFound();

        // Sheet rows with post + caption data
        var rows = await _db.SheetRows
            .Where(sr => sr.ClientId == brief.ClientId)
            .Include(sr => sr.Post)
                .ThenInclude(p => p!.Captions)
            .OrderBy(sr => sr.RowKey)
            .ToListAsync(ct);

        var rowDtos = rows.Select(r =>
        {
            var selectedCaption = r.Post?.Captions?.FirstOrDefault(c => c.IsSelected)
                               ?? r.Post?.Captions?.FirstOrDefault();
            BriefInfo? briefData = null;
            if (!string.IsNullOrEmpty(r.Post?.BriefData))
            {
                try { briefData = JsonSerializer.Deserialize<BriefInfo>(r.Post.BriefData); }
                catch { /* ignore */ }
            }

            return new
            {
                r.RowKey,
                r.PostId,
                status       = r.Post?.Status.ToString(),
                scheduledAt  = r.Post?.ScheduledAt,
                caption      = selectedCaption?.Content,
                hashtags     = selectedCaption?.Hashtags,
                platforms    = selectedCaption?.Platforms,
                brief        = briefData?.Brief,
                charte       = briefData?.Charte,
                format       = briefData?.Format,
                texteVisuel  = briefData?.TexteVisuel,
                imagesPhotos = briefData?.ImagesPhotos,
                infosPost    = briefData?.InfosPost,
                budget       = briefData?.Budget,
                objectif     = briefData?.Objectif,
                duplicateIG  = briefData?.DuplicateIG,
                sponsoEnd    = briefData?.SponsoEndDate,
                audience     = briefData?.Audience,
                pages        = briefData?.Pages,
                commentaires = briefData?.Commentaires,
            };
        }).ToList();

        // Trello comments
        var comments = await FetchCommentsAsync(brief.CardId, ct);

        return Ok(new
        {
            brief.Id, brief.CardId, brief.Title, brief.Description,
            brief.SheetUrl, brief.Due, brief.LabelsJson, brief.CardUrl,
            brief.AssignedAt, brief.CreatedAt,
            clientName = brief.Client?.Name,
            rows       = rowDtos,
            comments,
        });
    }

    // PATCH /api/trello/briefs/{id}/assign  — assign brief to a client
    [HttpPatch("{id:int}/assign")]
    public async Task<IActionResult> Assign(int id, [FromBody] AssignBriefDto dto, CancellationToken ct)
    {
        var brief = await _db.TrelloBriefs.FindAsync(new object[] { id }, ct);
        if (brief == null) return NotFound();

        brief.ClientId   = dto.ClientId;
        brief.AssignedAt = DateTime.UtcNow;

        // Save sheet URL on the client if the brief has one
        if (!string.IsNullOrEmpty(brief.SheetUrl))
        {
            var client = await _db.Clients.FindAsync(new object[] { dto.ClientId }, ct);
            if (client != null) client.SheetUrl = brief.SheetUrl;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { brief.Id, brief.ClientId, brief.AssignedAt });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<List<object>> FetchCommentsAsync(string cardId, CancellationToken ct)
    {
        var apiKey = _config["Trello:ApiKey"];
        var token  = _config["Trello:Token"];
        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(cardId))
            return new();

        try
        {
            var client   = _http.CreateClient();
            var url      = $"https://api.trello.com/1/cards/{cardId}/actions" +
                           $"?filter=commentCard&key={apiKey}&token={token}";
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return new();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var result = new List<object>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var author = "";
                if (item.TryGetProperty("memberCreator", out var mc))
                {
                    if (mc.TryGetProperty("fullName", out var fn) && !string.IsNullOrEmpty(fn.GetString()))
                        author = fn.GetString()!;
                    else if (mc.TryGetProperty("username", out var un))
                        author = un.GetString() ?? "";
                }

                var text = "";
                if (item.TryGetProperty("data", out var d) && d.TryGetProperty("text", out var t))
                    text = t.GetString() ?? "";

                var date = item.TryGetProperty("date", out var dt) ? dt.GetString() : null;

                result.Add(new { author, text, date });
            }
            return result;
        }
        catch { return new(); }
    }
}

public class AssignBriefDto
{
    public int ClientId { get; set; }
}
