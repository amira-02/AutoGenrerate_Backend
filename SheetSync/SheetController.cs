using AutoGenerate.SheetSync;
using AutoGenerate.Shared.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

[ApiController]
[Route("api/sheets")]
[Authorize]
[Tags("Sheets")]
public class SheetController : ControllerBase
{
    private readonly AppDbContext      _db;
    private readonly SheetSyncService  _sync;

    public SheetController(AppDbContext db, SheetSyncService sync)
    {
        _db   = db;
        _sync = sync;
    }

    // POST /api/sheets/sync/{clientId}  — manual trigger
    [HttpPost("sync/{clientId:int}")]
    public async Task<IActionResult> Sync(int clientId, CancellationToken ct)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == clientId, ct);
        if (client == null) return NotFound();
        if (string.IsNullOrEmpty(client.SheetUrl))
            return BadRequest(new { message = "No sheet URL linked to this client" });

        var result = await _sync.SyncClientAsync(clientId, ct);

        if (result.Error != null)
            return BadRequest(new { message = result.Error });

        return Ok(new
        {
            message     = "Sync complete",
            created     = result.Created,
            updated     = result.Updated,
            cancelled   = result.Cancelled,
            lastSyncAt  = client.SheetLastSyncAt,
        });
    }

    // PATCH /api/sheets/url/{clientId}  — save or update the sheet URL
    [HttpPatch("url/{clientId:int}")]
    public async Task<IActionResult> SetSheetUrl(int clientId, [FromBody] SetSheetUrlDto dto, CancellationToken ct)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == clientId, ct);
        if (client == null) return NotFound();

        client.SheetUrl = string.IsNullOrWhiteSpace(dto.SheetUrl) ? null : dto.SheetUrl.Trim();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "Sheet URL updated", sheetUrl = client.SheetUrl });
    }

    // GET /api/sheets/status/{clientId}
    [HttpGet("status/{clientId:int}")]
    public async Task<IActionResult> Status(int clientId, CancellationToken ct)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == clientId, ct);
        if (client == null) return NotFound();

        var rowCount = await _db.SheetRows.CountAsync(sr => sr.ClientId == clientId, ct);

        return Ok(new
        {
            sheetUrl     = client.SheetUrl,
            lastSyncAt   = client.SheetLastSyncAt,
            trackedRows  = rowCount,
        });
    }
}

public class SetSheetUrlDto
{
    public string? SheetUrl { get; set; }
}
