using AutoGenerate.Shared.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

[ApiController]
[Route("api/notifications")]
[Authorize]
[Tags("Notifications")]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public NotificationsController(AppDbContext db) => _db = db;

    // GET /api/notifications
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var notifs = await _db.Notifications
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new {
                n.Id,
                n.Title,
                n.Message,
                n.Type,
                n.ReferenceId,
                n.IsRead,
                n.CreatedAt
            })
            .ToListAsync();

        return Ok(notifs);
    }

    // PATCH /api/notifications/{id}/read
    [HttpPatch("{id}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var notif = await _db.Notifications.FindAsync(id);
        if (notif == null) return NotFound();
        notif.IsRead = true;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Marked as read" });
    }

    // GET /api/notifications/unread-count
    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount()
    {
        var count = await _db.Notifications.CountAsync(n => !n.IsRead);
        return Ok(new { count });
    }
}