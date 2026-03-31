//using System.Text;
//using System.Text.Json;

//namespace AutoPost.Api.Services;

//public class GroqService
//{
//    private readonly IConfiguration _config;
//    private readonly HttpClient _http;

//    public GroqService(IConfiguration config, IHttpClientFactory factory)
//    {
//        _config = config;
//        _http = factory.CreateClient();
//    }

//    public async Task<string> GeneratePostAsync(string prompt, string? jsonContent)
//    {
//        string finalPrompt = prompt;

//        // ✅ JSON optionnel
//        if (!string.IsNullOrWhiteSpace(jsonContent))
//        {
//            finalPrompt += $"\n\nAdditional Data:\n{jsonContent}";
//        }

//        var apiKey = _config["Groq:ApiKey"];
//        if (string.IsNullOrEmpty(apiKey))
//            throw new Exception("Groq API Key not configured.");

//        _http.DefaultRequestHeaders.Clear();
//        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

//        var requestBody = new
//        {
//            model = "llama-3.3-70b-versatile",
//            messages = new[]
//            {
//                new { role = "user", content = finalPrompt }
//            }
//        };

//        var json = JsonSerializer.Serialize(requestBody);
//        var content = new StringContent(json, Encoding.UTF8, "application/json");

//        var response = await _http.PostAsync("https://api.groq.com/openai/v1/chat/completions", content);

//        if (!response.IsSuccessStatusCode)
//        {
//            var error = await response.Content.ReadAsStringAsync();
//            throw new Exception($"Groq API Error: {error}");
//        }

//        var responseString = await response.Content.ReadAsStringAsync();

//        using var doc = JsonDocument.Parse(responseString);
//        var result = doc.RootElement
//                        .GetProperty("choices")[0]
//                        .GetProperty("message")
//                        .GetProperty("content")
//                        .GetString();

//        return result ?? "No content generated.";
//    }
//}