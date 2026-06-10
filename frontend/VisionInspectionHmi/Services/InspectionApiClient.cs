using System.Text.Json;
using VisionInspectionHmi.Models;

namespace VisionInspectionHmi.Services;

public class InspectionApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public InspectionApiClient(string baseUrl = "http://localhost:8000", int timeoutSeconds = 30)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http    = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 120)) };
    }

    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            var res = await _http.GetAsync($"{_baseUrl}/health");
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<InspectionResult> InspectAsync(string imagePath)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException($"画像ファイルが見つかりません: {imagePath}");

        using var content = new MultipartFormDataContent();
        var fileBytes = await File.ReadAllBytesAsync(imagePath);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(GetMimeType(imagePath));
        content.Add(fileContent, "file", Path.GetFileName(imagePath));

        HttpResponseMessage res;
        try
        {
            res = await _http.PostAsync($"{_baseUrl}/inspect", content);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"APIへの接続に失敗しました。APIが起動しているか確認してください。\n詳細: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            throw new InvalidOperationException("APIリクエストがタイムアウトしました。");
        }

        var body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"APIエラー ({(int)res.StatusCode}): {body}");

        var result = JsonSerializer.Deserialize<InspectionResult>(body)
            ?? throw new InvalidOperationException("APIレスポンスの解析に失敗しました。");
        return result;
    }

    private static string GetMimeType(string path) => Path.GetExtension(path).ToLower() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png"            => "image/png",
        ".bmp"            => "image/bmp",
        _                 => "application/octet-stream",
    };

    public void Dispose() => _http.Dispose();
}
