using System.Text;
using System.Text.Json;

namespace AutoPost.Api.Services;

public class GroqService
{
    private readonly IConfiguration _config;
    private readonly HttpClient _http;

    public GroqService(IConfiguration config, IHttpClientFactory factory)
    {
        _config = config;
        _http = factory.CreateClient();
    }

    public async Task<string> GeneratePostAsync(string prompt, string? jsonDocument = null)
    {
        var apiKey = _config["Groq:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new Exception("Groq API Key is missing in appsettings.json");

        string ragContext = "";

        if (!string.IsNullOrWhiteSpace(jsonDocument))
        {
            ragContext = $"Campaign JSON Context:\n{jsonDocument}\n\n";
        }

        var finalPrompt = $@"
You are a senior marketing AI.

{ragContext}

User request:
{prompt}

Generate:
- Instagram post
- LinkedIn post
Add CTA + hook + emojis.
";

        var payload = new
        {
            model = "llama-3.3-70b-versatile",
            messages = new[]
            {
                new { role = "system", content = "You are an expert marketing copywriter." },
                new { role = "user", content = finalPrompt }
            },
            temperature = 0.7
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");

        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json"
        );

        var response = await _http.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Groq API error: {response.StatusCode} - {responseBody}");

        using var doc = JsonDocument.Parse(responseBody);

        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "No content generated.";
    }
}