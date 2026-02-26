using AutoPost.Api.Services;
using AutoPost.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoPost.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PostsController : ControllerBase
{
    private readonly GroqService _gemini;

    public PostsController(GroqService gemini)
    {
        _gemini = gemini;
    }

    [Authorize]
    [HttpPost("generate")]
    [Consumes("multipart/form-data")] // pour accepter un fichier
    public async Task<IActionResult> GeneratePost([FromForm] GeneratePostDto dto)
    {
        string jsonContent = null;

        if (dto.JsonFile != null)
        {
            using var stream = new StreamReader(dto.JsonFile.OpenReadStream());
            jsonContent = await stream.ReadToEndAsync(); // c'est le JSON du fichier
        }

        var result = await _gemini.GeneratePostAsync(dto.Prompt, jsonContent);

        return Ok(new { post = result });
    }
}