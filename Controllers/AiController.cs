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

        // ✅ JSON FILE OPTIONNEL
        if (dto.JsonFile != null && dto.JsonFile.Length > 0)
        {
            using var reader = new StreamReader(dto.JsonFile.OpenReadStream());
            jsonContent = await reader.ReadToEndAsync();
        }

        // ✅ Appel fonctionne même si jsonContent == null
        var result = await _groq.GeneratePostAsync(dto.Prompt, jsonContent);

        return Ok(new
        {
            success = true,
            post = result
        });
    }



    [HttpPost("generate-image")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> GenerateImage([FromForm] GeneratePostDto dto,
                                                  [FromServices] ImageService imageService)
    {
        if (string.IsNullOrWhiteSpace(dto.Prompt))
            return BadRequest("Prompt is required.");

        string? jsonContent = null;
        if (dto.JsonFile != null && dto.JsonFile.Length > 0)
        {
            using var reader = new StreamReader(dto.JsonFile.OpenReadStream());
            jsonContent = await reader.ReadToEndAsync();
        }

        // Générer le caption avec ton GroqService (facultatif)
        var caption = await _groq.GeneratePostAsync(dto.Prompt, jsonContent);

        // Générer l'image
        var imageBytes = await imageService.GenerateImageAsync(caption);

        // Retourne l'image directement
        return File(imageBytes, "image/png", "generated.png");
    }
}