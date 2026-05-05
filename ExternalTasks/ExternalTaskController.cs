using AutoGenerate.Shared.Data;
using AutoGenerate.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoGenerate.ExternalTasks
{
    [ApiController]
    [Route("api/external")]
    [Tags("External")]
    public class ExternalTaskController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly HttpClient _http;

        public ExternalTaskController(AppDbContext db, IHttpClientFactory factory)
        {
            _db = db;
            _http = factory.CreateClient();
        }

        // ── POST /api/external/tasks ──────────────────────────────────────
        // Appelé par le binôme — crée juste une ExternalTask + notification
        [HttpPost("tasks")]
        [AllowAnonymous]
        public async Task<IActionResult> ReceiveTask([FromBody] ExternalTaskDto dto)
        {
            if (string.IsNullOrEmpty(dto.TaskId) || string.IsNullOrEmpty(dto.Description))
                return BadRequest(new { message = "TaskId and Description are required" });

            var exists = await _db.ExternalTasks.AnyAsync(t => t.TaskId == dto.TaskId);
            if (exists)
                return Conflict(new { message = "Task already received" });

            // Crée la tâche externe
            var task = new ExternalTask
            {
                TaskId = dto.TaskId,
                Description = dto.Description,
                ScheduledAt = dto.ScheduledAt,
                CallbackUrl = dto.CallbackUrl.Trim(),
                Status = "pending",
                CreatedAt = DateTime.UtcNow,
            };
            _db.ExternalTasks.Add(task);

            // Crée la notification pour la CM
            var notif = new Notification
            {
                Title = "New external post request",
                Message = $"A post has been requested: {dto.Description.Substring(0, Math.Min(60, dto.Description.Length))}...",
                Type = "external_task",
                ReferenceId = task.Id.ToString(),
                CreatedAt = DateTime.UtcNow,
                IsRead = false,
            };
            _db.Notifications.Add(notif);

            await _db.SaveChangesAsync();

            // Met à jour ReferenceId avec le vrai ID
            notif.ReferenceId = task.Id.ToString();
            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "Task received — CM will be notified",
                taskId = task.Id,
                status = "pending"
            });
        }

        // ── GET /api/external/tasks ───────────────────────────────────────
        // Liste toutes les tâches externes
        [HttpGet("tasks")]
        public async Task<IActionResult> GetTasks()
        {
            var tasks = await _db.ExternalTasks
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new
                {
                    t.Id,
                    t.TaskId,
                    t.Description,
                    t.ScheduledAt,
                    t.Status,
                    t.PostId,
                    t.CreatedAt
                })
                .ToListAsync();
            return Ok(tasks);
        }

        // ── GET /api/external/tasks/{id} ──────────────────────────────────
        // Détail d'une tâche — utilisé par le frontend pour pré-remplir le modal
        [HttpGet("tasks/{id}")]
        public async Task<IActionResult> GetTask(int id)
        {
            var task = await _db.ExternalTasks.FindAsync(id);
            if (task == null) return NotFound(new { message = "Task not found" });

            return Ok(new
            {
                task.Id,
                task.TaskId,
                task.Description,
                task.ScheduledAt,
                task.Status,
                task.PostId,
                task.CreatedAt,
            });
        }

        // ── POST /api/external/tasks/{id}/assign ──────────────────────────
        // CM choisit un topic → crée le post → retourne les infos pour NewPostModal
        [HttpPost("tasks/{id}/assign")]
        public async Task<IActionResult> AssignTask(int id, [FromBody] AssignTaskDto dto)
        {
            var task = await _db.ExternalTasks.FindAsync(id);
            if (task == null) return NotFound(new { message = "Task not found" });
            if (task.Status != "pending")
                return BadRequest(new { message = "Task already assigned" });

            var email = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "";
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null) return Unauthorized();

            // Vérifie que le topic existe
            var topic = await _db.Topics.FindAsync(dto.TopicId);
            if (topic == null) return NotFound(new { message = "Topic not found" });

            // Crée le post pré-rempli
            var post = new Post
            {
                TopicId = dto.TopicId,
                UserId = user.Id,
                Status = PostStatus.Draft,
                ScheduledAt = task.ScheduledAt,
                CreatedAt = DateTime.UtcNow,
                ExternalTaskId = task.TaskId,
                ExternalCallback = task.CallbackUrl,
            };
            _db.Posts.Add(post);
            await _db.SaveChangesAsync();

            // Ajoute la description comme caption
            var caption = new AutoGenerate.CaptionService.Models.Caption
            {
                PostId = post.Id,
                Content = !string.IsNullOrEmpty(dto.Description) ? dto.Description : task.Description,
            };
            _db.Captions.Add(caption);

            // Met à jour la tâche externe
            task.Status = "assigned";
            task.PostId = post.Id;

            await _db.SaveChangesAsync();

            // Retourne les infos pour ouvrir NewPostModal pré-rempli
            return Ok(new
            {
                message = "Post created — open NewPostModal",
                postId = post.Id,
                topicId = dto.TopicId,
                description = task.Description,
                scheduledAt = task.ScheduledAt,
            });
        }

        // ── POST /api/external/tasks/{id}/approve ─────────────────────────
        // Post publié → envoie callback approved au binôme
        [HttpPost("tasks/{id}/approve")]
        public async Task<IActionResult> ApproveTask(int id)
        {
            var task = await _db.ExternalTasks.FindAsync(id);
            if (task == null) return NotFound(new { message = "Task not found" });

            if (string.IsNullOrEmpty(task.CallbackUrl))
                return BadRequest(new { message = "No callback URL" });

            var payload = new
            {
                taskId = task.TaskId,
                status = "approved",
                publishedAt = DateTime.UtcNow,
                message = "Post successfully published on all platforms"
            };

            try
            {
                var response = await _http.PostAsJsonAsync(task.CallbackUrl.Trim(), payload);
                if (!response.IsSuccessStatusCode)
                    return StatusCode(502, new { message = "Callback URL returned an error" });
            }
            catch (Exception ex)
            {
                return StatusCode(502, new { message = "Failed to reach callback URL", details = ex.Message });
            }

            // Met à jour le statut
            task.Status = "published";
            if (task.PostId.HasValue)
            {
                var post = await _db.Posts.FindAsync(task.PostId.Value);
                if (post != null)
                {
                    post.Status = PostStatus.Published;
                    post.PublishedAt = DateTime.UtcNow;
                }
            }
            await _db.SaveChangesAsync();

            return Ok(new { message = "Callback sent — workflow can continue" });
        }
    }
}