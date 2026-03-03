using System.Text;
using System.Text.Json;

namespace AutoPost.Api.Services;

public class ImageService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;

    public ImageService(IConfiguration config, IHttpClientFactory factory)
    {
        _config = config;
        _http = factory.CreateClient();
    }

    public async Task<byte[]> GenerateImageAsync(string prompt)
    {
        var apiKey = _config["HuggingFace:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
            throw new Exception("HuggingFace API Key not configured.");

        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var requestBody = new { inputs = prompt };
        var content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        var url = "https://router.huggingface.co/hf-inference/models/stabilityai/stable-diffusion-xl-base-1.0";

        var response = await _http.PostAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"HuggingFace API Error: {error}");
        }

        // Retourne directement les bytes de l'image (pas de JSON à parser)
        return await response.Content.ReadAsByteArrayAsync();
    }
}