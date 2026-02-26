using AutoPost.Api.Dtos;
using AutoPost.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoPost.Api.Controllers;

[ApiController]
[Route("api/ai")]
public class AiController : ControllerBase
{
    private readonly GroqService _groq;

    public AiController(GroqService groq)
    {
        _groq = groq;
    }

    [Authorize]
    [HttpPost("generate")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> GeneratePost([FromForm] GeneratePostDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Prompt))
            return BadRequest("Prompt is required.");

        string? jsonContent = null;

        if (dto.JsonFile != null && dto.JsonFile.Length > 0)
        {
            using var reader = new StreamReader(dto.JsonFile.OpenReadStream());
            jsonContent = await reader.ReadToEndAsync();
        }

        var result = await _groq.GeneratePostAsync(dto.Prompt, jsonContent);

        return Ok(new
        {
            success = true,
            post = result
        });
    }
}