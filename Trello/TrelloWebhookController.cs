using AutoGenerate.SheetSync;
using AutoGenerate.Shared.Data;
using AutoGenerate.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;

[ApiController]
[Route("api/trello")]
[AllowAnonymous]
[Tags("Trello")]
public class TrelloWebhookController : ControllerBase
{
    private readonly AppDbContext       _db;
    private readonly IConfiguration    _config;
    private readonly IHttpClientFactory _http;
    private readonly SheetSyncService  _sheetSync;

    private const string TARGET_LIST_KEYWORD = "brief";

    public TrelloWebhookController(
        AppDbContext db, IConfiguration config,
        IHttpClientFactory http, SheetSyncService sheetSync)
    {
        _db = db; _config = config; _http = http; _sheetSync = sheetSync;
    }

    [HttpHead("webhook")]
    public IActionResult ValidateWebhook() => Ok();

    [HttpPost("webhook")]
    public async Task<IActionResult> ReceiveWebhook([FromBody] JsonElement payload)
    {
        if (!payload.TryGetProperty("action", out var action)) return Ok();
        if (!action.TryGetProperty("type", out var typeProp)) return Ok();

        var actionType = typeProp.GetString();
        bool isCreate = actionType == "createCard";
        bool isUpdate = actionType == "updateCard";
        if (!isCreate && !isUpdate) return Ok();

        // For updateCard, only proceed if the description field changed
        string? descFromPayload = null;
        if (isUpdate)
        {
            if (!action.TryGetProperty("data", out var ud)) return Ok();
            if (!ud.TryGetProperty("old", out var old))    return Ok();
            if (!old.TryGetProperty("desc", out _))        return Ok(); // desc didn't change

            // New description is directly in the payload — no extra API call needed
            if (ud.TryGetProperty("card", out var uc) && uc.TryGetProperty("desc", out var nd))
                descFromPayload = nd.GetString();
        }

        var memberName = "Someone";
        if (action.TryGetProperty("memberCreator", out var mc))
        {
            if (mc.TryGetProperty("fullName", out var fn) && !string.IsNullOrEmpty(fn.GetString()))
                memberName = fn.GetString()!;
            else if (mc.TryGetProperty("username", out var un))
                memberName = un.GetString() ?? "Someone";
        }

        var cardId = ""; var cardName = ""; var listName = "";

        if (action.TryGetProperty("data", out var data))
        {
            if (data.TryGetProperty("card", out var card))
            {
                if (card.TryGetProperty("name", out var cn)) cardName = cn.GetString() ?? "";
                if (card.TryGetProperty("id",   out var ci)) cardId   = ci.GetString() ?? "";
            }
            if (data.TryGetProperty("list", out var list) && list.TryGetProperty("name", out var ln))
                listName = ln.GetString() ?? "";
        }

        // For updateCard, list may be missing — fetch it via API to confirm this card is still in "brief"
        if (isUpdate && string.IsNullOrEmpty(listName))
        {
            var details = await FetchCardDetailsAsync(cardId);
            // If we can't confirm the list, still proceed (the webhook is registered on the board)
        }

        if (!string.IsNullOrEmpty(listName) &&
            !listName.Contains(TARGET_LIST_KEYWORD, StringComparison.OrdinalIgnoreCase))
            return Ok();

        var allClients = await _db.Clients.ToListAsync();
        var dbClient   = MatchClientByTitle(allClients, cardName);

        // For createCard: try API call to get description
        // For updateCard: description comes directly from the payload
        CardDetails? cardDetails = null;
        if (isCreate)
            cardDetails = await FetchCardDetailsAsync(cardId);
        else
            cardDetails = new CardDetails { Description = descFromPayload };

        var description = cardDetails?.Description ?? descFromPayload ?? "";
        var sheetUrl    = ExtractSpreadsheetUrl(description);
        var syncResult  = new SyncResult();

        // ── Upsert TrelloBrief ──────────────────────────────────────────────
        var brief = await _db.TrelloBriefs.FirstOrDefaultAsync(b => b.CardId == cardId);
        if (brief == null)
        {
            brief = new TrelloBrief
            {
                CardId    = cardId,
                Title     = string.IsNullOrEmpty(cardName) ? "Brief Trello" : cardName,
                CardUrl   = cardDetails?.Url,
                CreatedAt = DateTime.UtcNow,
            };
            _db.TrelloBriefs.Add(brief);
        }

        // Always update mutable fields
        if (!string.IsNullOrEmpty(cardName))    brief.Title       = cardName;
        if (!string.IsNullOrEmpty(description)) brief.Description = description;
        if (!string.IsNullOrEmpty(sheetUrl))    brief.SheetUrl    = sheetUrl;
        if (!string.IsNullOrEmpty(cardDetails?.Due))
            brief.Due = cardDetails.Due;
        if (cardDetails?.Labels?.Count > 0)
            brief.LabelsJson = System.Text.Json.JsonSerializer.Serialize(cardDetails.Labels);
        if (!string.IsNullOrEmpty(cardDetails?.Url))
            brief.CardUrl = cardDetails.Url;

        // If client matched by name, auto-assign
        if (dbClient != null && brief.ClientId == null)
        {
            brief.ClientId   = dbClient.Id;
            brief.AssignedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();

        // ── Sheet sync if URL detected ──────────────────────────────────────
        if (!string.IsNullOrEmpty(sheetUrl) && dbClient != null)
        {
            dbClient.SheetUrl = sheetUrl;
            await _db.SaveChangesAsync();
            try { syncResult = await _sheetSync.SyncClientAsync(dbClient.Id); }
            catch { /* non-blocking */ }
        }

        // ── Build notification message ──────────────────────────────────────
        var message = BuildRichMessage(memberName, dbClient?.Name, listName, cardDetails);
        if (!string.IsNullOrEmpty(sheetUrl))
        {
            message += syncResult.Error != null
                ? "\n⚠️ Planning détecté mais inaccessible (vérifier le partage public du Sheet)"
                : $"\n✅ {syncResult.Created} post(s) importé(s) depuis le planning — en attente de validation";
        }

        // ── Upsert notification linked to this brief ────────────────────────
        var notif = await _db.Notifications
            .Where(n => n.Type == "trello_brief" && n.ReferenceId == brief.Id.ToString())
            .FirstOrDefaultAsync();

        if (notif != null)
        {
            notif.Message = message;
            notif.IsRead  = false;
        }
        else
        {
            _db.Notifications.Add(new Notification
            {
                Title       = brief.Title,
                Message     = message,
                Type        = "trello_brief",
                ReferenceId = brief.Id.ToString(),
                IsRead      = false,
                CreatedAt   = DateTime.UtcNow,
            });
        }

        await _db.SaveChangesAsync();
        return Ok();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string? ExtractSpreadsheetUrl(string text)
    {
        var patterns = new[]
        {
            @"https://docs\.google\.com/spreadsheets/d/[^\s\)\]\>]+",
            @"https://1drv\.ms/[^\s\)\]\>]+",
            @"https://onedrive\.live\.com/[^\s\)\]\>]+",
            @"https://[^\s]+\.sharepoint\.com/[^\s\)\]\>]+",
        };
        foreach (var p in patterns)
        {
            var m = Regex.Match(text, p);
            if (m.Success) return m.Value.TrimEnd('.');
        }
        return null;
    }

    private async Task<CardDetails?> FetchCardDetailsAsync(string cardId)
    {
        if (string.IsNullOrEmpty(cardId)) return null;
        var apiKey = _config["Trello:ApiKey"];
        var token  = _config["Trello:Token"];
        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(token)) return null;

        try
        {
            var client   = _http.CreateClient();
            var url      = $"https://api.trello.com/1/cards/{cardId}" +
                           $"?fields=name,desc,due,labels,url,shortUrl" +
                           $"&key={apiKey}&token={token}";
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var details = new CardDetails
            {
                Description = root.TryGetProperty("desc",     out var d)  ? d.GetString()  : null,
                Due         = root.TryGetProperty("due",      out var du) ? du.GetString() : null,
                Url         = root.TryGetProperty("shortUrl", out var u)  ? u.GetString()  : null,
            };

            if (root.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array)
            {
                details.Labels = labels.EnumerateArray()
                    .Select(l => l.TryGetProperty("name", out var n) ? n.GetString() : null)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Select(n => n!)
                    .ToList();
            }

            return details;
        }
        catch { return null; }
    }

    private static string BuildRichMessage(
        string creator, string? clientName, string listName, CardDetails? card)
    {
        var lines = new List<string>();
        lines.Add($"👤 Déposé par : {creator}");
        if (!string.IsNullOrEmpty(clientName))
            lines.Add($"🏢 Client : {clientName}");
        lines.Add($"📋 Liste : {listName}");
        if (!string.IsNullOrEmpty(card?.Description))
            lines.Add($"📝 Description : {card.Description}");
        if (card?.Labels?.Count > 0)
            lines.Add($"🏷️ Labels : {string.Join(", ", card.Labels)}");
        if (!string.IsNullOrEmpty(card?.Due) && DateTime.TryParse(card.Due, out var due))
            lines.Add($"📅 Deadline : {due:dd/MM/yyyy HH:mm}");
        if (!string.IsNullOrEmpty(card?.Url))
            lines.Add($"🔗 {card.Url}");
        return string.Join("\n", lines);
    }

    // Find the client whose name appears inside the Trello card title (case-insensitive, accent-insensitive)
    // The card title is typically "ClientName - N posts - Month"
    private static Client? MatchClientByTitle(List<Client> clients, string cardTitle)
    {
        var normTitle = NormMatch(cardTitle);

        // Prefer the longest name match (more specific = better)
        return clients
            .Where(c => normTitle.Contains(NormMatch(c.Name)))
            .OrderByDescending(c => c.Name.Length)
            .FirstOrDefault();
    }

    private static string NormMatch(string s)
        => System.Text.RegularExpressions.Regex.Replace(
            s.Trim().ToLowerInvariant()
             .Replace("é","e").Replace("è","e").Replace("ê","e")
             .Replace("à","a").Replace("â","a").Replace("ô","o")
             .Replace("û","u").Replace("î","i").Replace("ç","c"),
            @"\s+", " ");

    [HttpPost("register-webhook")]
    [Authorize]
    public async Task<IActionResult> RegisterWebhook([FromBody] RegisterWebhookDto dto)
    {
        var apiKey = _config["Trello:ApiKey"];
        var token  = _config["Trello:Token"];
        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(token))
            return BadRequest(new { message = "Trello:ApiKey and Trello:Token must be set in appsettings.json" });

        var client   = _http.CreateClient();
        var url      = $"https://api.trello.com/1/webhooks/?key={apiKey}&token={token}";
        var body     = new { callbackURL = dto.CallbackUrl, idModel = dto.BoardId, description = $"AutoGenerate webhook for board {dto.BoardId}" };
        var response = await client.PostAsJsonAsync(url, body);
        var content  = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            return BadRequest(new { message = "Trello rejected the webhook", detail = content });

        return Ok(new { message = "Webhook registered successfully", detail = content });
    }

    private class CardDetails
    {
        public string?       Description { get; set; }
        public string?       Due         { get; set; }
        public string?       Url         { get; set; }
        public List<string>? Labels      { get; set; }
    }
}

public class RegisterWebhookDto
{
    public string BoardId     { get; set; } = "";
    public string CallbackUrl { get; set; } = "";
}
