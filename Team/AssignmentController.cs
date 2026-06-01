using AutoGenerate.Shared.Data;
using AutoGenerate.Shared.Models;
using AutoGenerate.SheetSync;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

[ApiController]
[Route("api/assignments")]
[Authorize]
[Tags("Team")]
public class AssignmentController : ControllerBase
{
    private readonly AppDbContext _db;

    public AssignmentController(AppDbContext db) => _db = db;

    private async Task<User?> CurrentUserAsync(CancellationToken ct)
    {
        var email = User.FindFirstValue(ClaimTypes.Name);
        return await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
    }

    // POST /api/assignments — CM assigns a task to a team member
    [HttpPost]
    public async Task<IActionResult> Assign([FromBody] AssignDto dto, CancellationToken ct)
    {
        var me = await CurrentUserAsync(ct);
        if (me == null) return Unauthorized();

        var assignee = await _db.Users.FindAsync(new object[] { dto.AssignedToUserId }, ct);
        if (assignee == null) return NotFound(new { message = "Membre introuvable" });

        if (dto.TaskType != "image" && dto.TaskType != "caption" && dto.TaskType != "full")
            return BadRequest(new { message = "TaskType invalide" });

        var assignment = new PostAssignment
        {
            PostId           = dto.PostId,
            ClientId         = dto.ClientId,
            RowKey           = dto.RowKey,
            BriefTitle       = dto.BriefTitle,
            AssignedToUserId = dto.AssignedToUserId,
            AssignedByUserId = me.Id,
            TaskType         = dto.TaskType,
            Notes            = dto.Notes,
            Status           = "pending",
            AssignedAt       = DateTime.UtcNow,
        };

        _db.PostAssignments.Add(assignment);

        // Notification to the assignee
        var taskLabel = dto.TaskType == "image"   ? "créer le visuel"
                      : dto.TaskType == "caption" ? "rédiger le caption"
                      : "créer le visuel + rédiger le caption";

        _db.Notifications.Add(new Notification
        {
            Title       = $"Nouvelle tâche : {dto.BriefTitle}",
            Message     = $"📌 N°{dto.RowKey} — Tu as été assigné(e) à {taskLabel}."
                        + (string.IsNullOrEmpty(dto.Notes) ? "" : $"\n💬 {dto.Notes}"),
            Type        = "assignment",
            ReferenceId = assignment.ClientId.ToString(),
            IsRead      = false,
            CreatedAt   = DateTime.UtcNow,
        });

        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            assignment.Id,
            assignment.TaskType,
            assignment.Status,
            assignment.AssignedAt,
            assigneeName = assignee.Name ?? assignee.Email,
        });
    }

    // POST /api/assignments/{id}/delegate — ChefEquipe sub-assigns to a member
    [HttpPost("{id:int}/delegate")]
    public async Task<IActionResult> Delegate(int id, [FromBody] DelegateDto dto, CancellationToken ct)
    {
        var me = await CurrentUserAsync(ct);
        if (me == null || (me.Role != "ChefVisuel" && me.Role != "ChefRedac")) return Forbid();

        var parent = await _db.PostAssignments.FindAsync(new object[] { id }, ct);
        if (parent == null) return NotFound();
        if (parent.AssignedToUserId != me.Id) return Forbid();

        var assignee = await _db.Users.FindAsync(new object[] { dto.AssignedToUserId }, ct);
        if (assignee == null) return NotFound(new { message = "Membre introuvable" });

        // ChefVisuel delegates only to Graphistes for image tasks
        // ChefRedac delegates only to Rédacteurs for caption tasks
        var expectedRole = me.Role == "ChefVisuel" ? "Graphiste" : "Redacteur";
        if (assignee.Role != expectedRole)
            return BadRequest(new { message = $"Tu peux déléguer uniquement à un(e) {expectedRole}" });

        // Remove any previous delegation for the same parent + taskType
        var existing = await _db.PostAssignments
            .Where(a => a.ParentAssignmentId == id && a.TaskType == parent.TaskType)
            .ToListAsync(ct);
        _db.PostAssignments.RemoveRange(existing);

        var sub = new PostAssignment
        {
            PostId             = parent.PostId,
            ClientId           = parent.ClientId,
            RowKey             = parent.RowKey,
            BriefTitle         = parent.BriefTitle,
            AssignedToUserId   = dto.AssignedToUserId,
            AssignedByUserId   = me.Id,
            TaskType           = parent.TaskType,
            Notes              = dto.Notes ?? parent.Notes,
            Status             = "pending",
            ParentAssignmentId = id,
            AssignedAt         = DateTime.UtcNow,
        };
        _db.PostAssignments.Add(sub);

        _db.Notifications.Add(new Notification
        {
            Title       = $"Nouvelle tâche : {parent.BriefTitle}",
            Message     = $"📌 N°{parent.RowKey} — Délégué par {me.Name ?? me.Email}."
                        + (string.IsNullOrEmpty(dto.Notes) ? "" : $"\n💬 {dto.Notes}"),
            Type        = "assignment",
            ReferenceId = parent.ClientId.ToString(),
            IsRead      = false,
            CreatedAt   = DateTime.UtcNow,
        });

        await _db.SaveChangesAsync(ct);
        return Ok(new { sub.Id, sub.TaskType, sub.Status, assigneeName = assignee.Name ?? assignee.Email });
    }

    // GET /api/assignments/my — current user's assigned tasks
    [HttpGet("my")]
    public async Task<IActionResult> MyAssignments(CancellationToken ct)
    {
        var me = await CurrentUserAsync(ct);
        if (me == null) return Unauthorized();

        // Use .Select() projection to avoid EF Core generating CTEs (WITH clause) that break SQL Server
        var list = await _db.PostAssignments
            .Where(a => a.AssignedToUserId == me.Id)
            .OrderByDescending(a => a.AssignedAt)
            .Select(a => new
            {
                a.Id, a.RowKey, a.BriefTitle, a.TaskType, a.Status,
                a.Notes, a.SubmittedContent, a.AssignedAt, a.SubmittedAt,
                a.PostId, a.ClientId, a.AssignedByUserId, a.ParentAssignmentId,
                clientName = a.Client != null ? a.Client.Name : "",
                assignedByName = a.AssignedBy != null ? (a.AssignedBy.Name ?? a.AssignedBy.Email) : "",
            })
            .ToListAsync(ct);

        var sorted = list
            .OrderBy(a => a.Status != "pending")
            .ThenByDescending(a => a.AssignedAt)
            .ToList();

        // For chef roles: also fetch sub-assignments delegated BY this chef
        if (me.Role == "ChefVisuel" || me.Role == "ChefRedac" || me.Role == "ChefEquipe")
        {
            var subs = await _db.PostAssignments
                .Where(a => a.ParentAssignmentId != null && a.AssignedByUserId == me.Id)
                .Select(a => new
                {
                    a.Id, a.ParentAssignmentId, a.Status,
                    assigneeName = a.AssignedTo != null ? (a.AssignedTo.Name ?? a.AssignedTo.Email) : "",
                })
                .ToListAsync(ct);

            var subsByParent = subs
                .Where(s => s.ParentAssignmentId.HasValue)
                .GroupBy(s => s.ParentAssignmentId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            return Ok(sorted.Select(a =>
            {
                subsByParent.TryGetValue(a.Id, out var sub);
                return new
                {
                    a.Id, a.RowKey, a.BriefTitle, a.TaskType, a.Status,
                    a.Notes, a.SubmittedContent, a.AssignedAt, a.SubmittedAt,
                    clientName      = a.clientName,
                    assignedBy      = a.assignedByName,
                    a.PostId, a.ClientId,
                    delegatedTo     = sub?.assigneeName,
                    delegatedStatus = sub?.Status,
                    subAssignmentId = sub != null ? (int?)sub.Id : null,
                };
            }));
        }

        return Ok(sorted.Select(a => new
        {
            a.Id, a.RowKey, a.BriefTitle, a.TaskType, a.Status,
            a.Notes, a.SubmittedContent, a.AssignedAt, a.SubmittedAt,
            clientName      = a.clientName,
            assignedBy      = a.assignedByName,
            a.PostId, a.ClientId,
            delegatedTo     = (string?)null,
            delegatedStatus = (string?)null,
            subAssignmentId = (int?)null,
        }));
    }

    // GET /api/assignments/{id}/context — return the full brief context for this task
    [HttpGet("{id:int}/context")]
    public async Task<IActionResult> GetContext(int id, CancellationToken ct)
    {
        var me = await CurrentUserAsync(ct);
        if (me == null) return Unauthorized();

        var assignment = await _db.PostAssignments.FindAsync(new object[] { id }, ct);
        if (assignment == null) return NotFound();
        // Allow access to the assignee OR the chef who delegated it (ParentAssignmentId chain)
        if (assignment.AssignedToUserId != me.Id && assignment.AssignedByUserId != me.Id)
            return Forbid();

        string?    topicName       = null;
        string?    existingCaption = null;
        BriefInfo? brief           = null;

        if (assignment.PostId.HasValue)
        {
            var post = await _db.Posts
                .Include(p => p.Topic)
                .Include(p => p.Captions)
                .FirstOrDefaultAsync(p => p.Id == assignment.PostId.Value, ct);

            if (post != null)
            {
                topicName       = post.Topic?.Name;
                existingCaption = post.Captions?
                    .FirstOrDefault(c => c.IsSelected)?.Content
                    ?? post.Captions?.FirstOrDefault()?.Content;

                if (!string.IsNullOrEmpty(post.BriefData))
                    try { brief = System.Text.Json.JsonSerializer.Deserialize<BriefInfo>(post.BriefData); }
                    catch { /* ignore */ }
            }
        }

        return Ok(new
        {
            topicName,
            existingCaption,
            brief        = brief?.Brief,
            charte       = brief?.Charte,
            format       = brief?.Format,
            texteVisuel  = brief?.TexteVisuel,
            imagesPhotos = brief?.ImagesPhotos,
            infosPost    = brief?.InfosPost,
            objectif     = brief?.Objectif,
            audience     = brief?.Audience,
            commentaires = brief?.Commentaires,
        });
    }

    // POST /api/assignments/{id}/chat — Rédacteur chat with AI for caption generation
    [HttpPost("{id:int}/chat")]
    public async Task<IActionResult> TeamChat(int id, [FromBody] TeamCaptionDto dto, CancellationToken ct)
    {
        var me = await CurrentUserAsync(ct);
        if (me == null) return Unauthorized();

        var assignment = await _db.PostAssignments.FindAsync(new object[] { id }, ct);
        if (assignment == null) return NotFound();
        if (assignment.AssignedToUserId != me.Id) return Forbid();

        // Resolve topic name: from post → topic, or fall back to briefTitle
        string topicName = assignment.BriefTitle;
        if (assignment.PostId.HasValue)
        {
            var post = await _db.Posts
                .Include(p => p.Topic)
                .FirstOrDefaultAsync(p => p.Id == assignment.PostId.Value, ct);
            if (post?.Topic != null) topicName = post.Topic.Name;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        try
        {
            var res = await http.PostAsJsonAsync(
                "http://localhost:5678/webhook/chatbot",
                new
                {
                    message       = !string.IsNullOrWhiteSpace(dto.Message)
                                        ? dto.Message
                                        : !string.IsNullOrWhiteSpace(dto.Instructions)
                                            ? dto.Instructions
                                            : "Génère un caption professionnel pour ce post",
                    topic         = topicName,
                    tone          = dto.Tone ?? "professional",
                    captionLength = dto.CaptionLength ?? "medium",
                    hashtags      = dto.Hashtags ?? "",
                    platforms     = new[] { "Instagram" },
                    sessionId     = !string.IsNullOrWhiteSpace(dto.SessionId)
                                        ? dto.SessionId
                                        : $"team-{me.Id}-{id}",
                });

            var body = await res.Content.ReadAsStringAsync();
            return Content(body, "application/json");
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { message = "Chatbot indisponible", detail = ex.Message });
        }
    }

    // GET /api/assignments/row?clientId=X&rowKey=Y — assignments for a specific post row
    [HttpGet("row")]
    public async Task<IActionResult> ForRow([FromQuery] int clientId, [FromQuery] string rowKey, CancellationToken ct)
    {
        var list = await _db.PostAssignments
            .Where(a => a.ClientId == clientId && a.RowKey == rowKey)
            .Include(a => a.AssignedTo)
            .Select(a => new
            {
                a.Id, a.TaskType, a.Status,
                assigneeName = a.AssignedTo != null ? (a.AssignedTo.Name ?? a.AssignedTo.Email) : "",
                assigneeRole = a.AssignedTo != null ? a.AssignedTo.Role : "",
                a.AssignedAt,
            })
            .ToListAsync(ct);

        return Ok(list);
    }

    // PATCH /api/assignments/{id}/submit — team member submits their work
    [HttpPatch("{id:int}/submit")]
    public async Task<IActionResult> Submit(int id, [FromBody] SubmitDto dto, CancellationToken ct)
    {
        var me = await CurrentUserAsync(ct);
        if (me == null) return Unauthorized();

        var assignment = await _db.PostAssignments
            .Include(a => a.AssignedBy)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (assignment == null) return NotFound();
        if (assignment.AssignedToUserId != me.Id) return Forbid();

        assignment.SubmittedContent = dto.Content;
        assignment.Status           = "done";
        assignment.SubmittedAt      = DateTime.UtcNow;

        // If it's an image task and we have a PostId, update the post's media
        if ((assignment.TaskType == "image" || assignment.TaskType == "full")
            && assignment.PostId.HasValue
            && !string.IsNullOrEmpty(dto.ImageUrl))
        {
            var existing = await _db.PostImages
                .FirstOrDefaultAsync(pi => pi.PostId == assignment.PostId.Value, ct);
            if (existing != null)
                existing.SetUrls(new List<string> { dto.ImageUrl });
            else
            {
                var img = new PostImage { PostId = assignment.PostId.Value };
                img.SetUrls(new List<string> { dto.ImageUrl });
                _db.PostImages.Add(img);
            }
        }

        // If it's a caption task, update the post's caption
        if ((assignment.TaskType == "caption" || assignment.TaskType == "full")
            && assignment.PostId.HasValue
            && !string.IsNullOrEmpty(dto.Caption))
        {
            var cap = await _db.Captions
                .Where(c => c.PostId == assignment.PostId.Value && c.IsSelected)
                .FirstOrDefaultAsync(ct)
              ?? await _db.Captions
                .Where(c => c.PostId == assignment.PostId.Value)
                .FirstOrDefaultAsync(ct);

            if (cap != null)
                cap.Content = dto.Caption;
        }

        // Notify the CM who assigned the task
        _db.Notifications.Add(new Notification
        {
            Title       = $"Tâche complétée : {assignment.BriefTitle}",
            Message     = $"✅ {me.Name ?? me.Email} a soumis sa tâche pour le post N°{assignment.RowKey}.",
            Type        = "assignment_done",
            ReferenceId = assignment.ClientId.ToString(),
            IsRead      = false,
            CreatedAt   = DateTime.UtcNow,
        });

        await _db.SaveChangesAsync(ct);
        return Ok(new { assignment.Id, assignment.Status, assignment.SubmittedAt });
    }
}

public class AssignDto
{
    public int?   PostId          { get; set; }
    public int    ClientId        { get; set; }
    public string RowKey          { get; set; } = "";
    public string BriefTitle      { get; set; } = "";
    public int    AssignedToUserId { get; set; }
    public string TaskType        { get; set; } = ""; // "image" | "caption" | "full"
    public string? Notes          { get; set; }
}

public class SubmitDto
{
    public string? Content  { get; set; }
    public string? ImageUrl { get; set; }
    public string? Caption  { get; set; }
}

public class TeamCaptionDto
{
    public string? Message       { get; set; }
    public string? Instructions  { get; set; }
    public string? Tone          { get; set; }
    public string? CaptionLength { get; set; }
    public string? Hashtags      { get; set; }
    public string? SessionId     { get; set; }
}

public class DelegateDto
{
    public int     AssignedToUserId { get; set; }
    public string? Notes            { get; set; }
}
