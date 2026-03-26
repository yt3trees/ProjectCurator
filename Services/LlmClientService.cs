using System.Globalization;
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

    public string LastSystemPrompt { get; private set; } = "";
    public string LastUserPrompt   { get; private set; } = "";
    public string LastResponse     { get; private set; } = "";

    public LlmClientService(ConfigService configService)
    {
        _configService = configService;
    }

    /// <summary>シングルターン (system + user 1往復)</summary>
    public async Task<string> ChatCompletionAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default)
    {
        var messages = new List<(string role, string content)>
        {
            ("user", userPrompt)
        };
        return await ChatWithHistoryAsync(systemPrompt, messages, ct);
    }

    /// <summary>マルチターン (会話履歴付き)</summary>
    public async Task<string> ChatWithHistoryAsync(
        string systemPrompt,
        IReadOnlyList<(string role, string content)> messages,
        CancellationToken ct = default,
        bool injectProfile = true)
    {
        var settings = _configService.LoadSettings();

        if (string.IsNullOrWhiteSpace(settings.LlmApiKey))
            throw new InvalidOperationException(
                "LLM API key is not configured. Please set it in Settings > LLM API.");

        var effectiveSystemPrompt = systemPrompt;
        if (injectProfile && !string.IsNullOrWhiteSpace(settings.LlmUserProfile))
            effectiveSystemPrompt = $"## User Profile\n{settings.LlmUserProfile.Trim()}\n\n{systemPrompt}";

        var response = settings.LlmProvider.Equals("azure_openai", StringComparison.OrdinalIgnoreCase)
            ? await SendAsync(settings, effectiveSystemPrompt, messages, isAzure: true,  ct)
            : await SendAsync(settings, effectiveSystemPrompt, messages, isAzure: false, ct);

        // デバッグログ: 最後のユーザーメッセージを記録
        LastSystemPrompt = effectiveSystemPrompt;
        LastUserPrompt   = messages.LastOrDefault(m => m.role == "user").content ?? "";
        LastResponse     = response;
        return response;
    }

    public async Task<string> TestConnectionAsync(CancellationToken ct = default)
    {
        var messages = new List<(string role, string content)> { ("user", "Reply with exactly 'OK'.") };
        return await ChatWithHistoryAsync("You are a test assistant.", messages, ct, injectProfile: false);
    }

    // -----------------------------------------------------------------------
    private async Task<string> SendAsync(
        AppSettings settings,
        string systemPrompt,
        IReadOnlyList<(string role, string content)> messages,
        bool isAzure,
        CancellationToken ct)
    {
        string url;
        if (isAzure)
        {
            if (string.IsNullOrWhiteSpace(settings.LlmEndpoint))
                throw new InvalidOperationException(
                    "Azure OpenAI endpoint is not configured. Please set LlmEndpoint in Settings.");
            var endpoint   = settings.LlmEndpoint.TrimEnd('/');
            var model      = string.IsNullOrWhiteSpace(settings.LlmModel)      ? "gpt-4o"             : settings.LlmModel;
            var apiVersion = string.IsNullOrWhiteSpace(settings.LlmApiVersion) ? "2024-12-01-preview" : settings.LlmApiVersion;
            url = $"{endpoint}/openai/deployments/{model}/chat/completions?api-version={apiVersion}";
        }
        else
        {
            url = "https://api.openai.com/v1/chat/completions";
        }

        var model2 = string.IsNullOrWhiteSpace(settings.LlmModel) ? "gpt-4o" : settings.LlmModel;

        // messages 配列を構築 (system → 履歴)
        var allMessages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };
        foreach (var (role, content) in messages)
            allMessages.Add(new { role, content });

        // ペイロードを動的に構築
        var payload = new Dictionary<string, object>
        {
            ["messages"] = allMessages
        };
        if (!isAzure)
            payload["model"] = model2;

        // ユーザー設定パラメータをマージ
        foreach (var (key, rawValue) in settings.LlmParameters)
            payload[key] = ParseParamValue(rawValue);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (isAzure)
            request.Headers.Add("api-key", settings.LlmApiKey);
        else
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.LlmApiKey);

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var provider = isAzure ? "Azure OpenAI" : "OpenAI";
            throw new HttpRequestException($"{provider} API error {(int)response.StatusCode}: {json}");
        }

        return ExtractContent(json);
    }

    private static object ParseParamValue(string value)
    {
        var v = value.Trim();
        if (bool.TryParse(v, out var b)) return b;
        if (long.TryParse(v, out var l)) return l;
        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        return v;
    }

    private static string ExtractContent(string json)
    {
        var node = JsonNode.Parse(json);
        return node?["choices"]?[0]?["message"]?["content"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Failed to parse LLM response.");
    }
}
