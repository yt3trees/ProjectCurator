using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using ProjectCurator.Models;

namespace ProjectCurator.Services;

public class LlmClientService
{
    private readonly ConfigService _configService;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };

    public LlmClientService(ConfigService configService)
    {
        _configService = configService;
    }

    public async Task<string> ChatCompletionAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default)
    {
        var settings = _configService.LoadSettings();

        if (string.IsNullOrWhiteSpace(settings.LlmApiKey))
            throw new InvalidOperationException(
                "LLM API key is not configured. Please set it in Settings > LLM API.");

        var response = settings.LlmProvider.Equals("azure_openai", StringComparison.OrdinalIgnoreCase)
            ? await AzureOpenAiCompletionAsync(settings, systemPrompt, userPrompt, ct)
            : await OpenAiCompletionAsync(settings, systemPrompt, userPrompt, ct);

        WriteDebugLog(systemPrompt, userPrompt, response);
        return response;
    }

    private static void WriteDebugLog(string systemPrompt, string userPrompt, string response)
    {
        try
        {
            var configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Projects", "_config");
            Directory.CreateDirectory(configDir);
            var logPath = Path.Combine(configDir, "llm_debug.log");

            var sb = new StringBuilder();
            sb.AppendLine($"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            sb.AppendLine();
            sb.AppendLine("--- SYSTEM PROMPT ---");
            sb.AppendLine(systemPrompt);
            sb.AppendLine();
            sb.AppendLine("--- USER PROMPT ---");
            sb.AppendLine(userPrompt);
            sb.AppendLine();
            sb.AppendLine("--- RESPONSE ---");
            sb.AppendLine(response);
            sb.AppendLine();

            File.WriteAllText(logPath, sb.ToString(), Encoding.UTF8);
        }
        catch { /* ログ失敗は握り潰す */ }
    }

    public async Task<string> TestConnectionAsync(CancellationToken ct = default)
    {
        return await ChatCompletionAsync("You are a test assistant.", "Reply with exactly 'OK'.", ct);
    }

    // -----------------------------------------------------------------------
    private async Task<string> OpenAiCompletionAsync(
        AppSettings settings, string systemPrompt, string userPrompt, CancellationToken ct)
    {
        const string endpoint = "https://api.openai.com/v1/chat/completions";
        var model = string.IsNullOrWhiteSpace(settings.LlmModel) ? "gpt-4o" : settings.LlmModel;

        var payload = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt   }
            },
            temperature = 0.3
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.LlmApiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenAI API error {(int)response.StatusCode}: {json}");

        return ExtractContent(json);
    }

    private async Task<string> AzureOpenAiCompletionAsync(
        AppSettings settings, string systemPrompt, string userPrompt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.LlmEndpoint))
            throw new InvalidOperationException(
                "Azure OpenAI endpoint is not configured. Please set LlmEndpoint in Settings.");

        var endpoint = settings.LlmEndpoint.TrimEnd('/');
        var model     = string.IsNullOrWhiteSpace(settings.LlmModel)      ? "gpt-4o"               : settings.LlmModel;
        var apiVersion = string.IsNullOrWhiteSpace(settings.LlmApiVersion) ? "2024-12-01-preview"   : settings.LlmApiVersion;
        var url = $"{endpoint}/openai/deployments/{model}/chat/completions?api-version={apiVersion}";

        var payload = new
        {
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt   }
            },
            temperature = 0.3
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("api-key", settings.LlmApiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Azure OpenAI API error {(int)response.StatusCode}: {json}");

        return ExtractContent(json);
    }

    private static string ExtractContent(string json)
    {
        var node = JsonNode.Parse(json);
        return node?["choices"]?[0]?["message"]?["content"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Failed to parse LLM response.");
    }
}
