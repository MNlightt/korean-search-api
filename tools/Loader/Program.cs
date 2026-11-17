using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;

var connStr = Environment.GetEnvironmentVariable("SEARCH_CONN")
    ?? throw new InvalidOperationException("SEARCH_CONN 환경 변수가 없습니다.");

const int Pages = 100, Size = 100;
var dir = Path.Combine("data", "wiki");
if (!Directory.Exists(dir))
    Directory.CreateDirectory(dir);

using var httpclient = new HttpClient();

await using var db = new SqlConnection(connStr);
await db.OpenAsync();
await db.ExecuteAsync("DELETE FROM dbo.documents WHERE id > 12");

for (int i = 0; i < Pages; i++)
{
    var file = Path.Combine(dir, $"page-{i:D4}.json");
    if (!File.Exists(file))
    {
        var offset = i * Size;
        var endpoint = $"https://datasets-server.huggingface.co/rows?dataset=wikimedia%2Fwikipedia&config=20231101.ko&split=train&offset={offset}&length={Size}";
        string? body = null;
        for (var attempt = 1; attempt <= 3 && body is null; attempt++)
        {
            var response = await httpclient.GetAsync(endpoint);
            if (response.IsSuccessStatusCode)
                body = await response.Content.ReadAsStringAsync();
            else
            {
                var err = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"page {i}: HTTP {(int)response.StatusCode} (시도 {attempt}) {err[..Math.Min(200, err.Length)]}");
                await Task.Delay(30000 * attempt);
            }
            await Task.Delay(3000);
        }
        if (body is null) break;
        await File.WriteAllTextAsync(file, body);
    }

    using var json = JsonDocument.Parse(await File.ReadAllTextAsync(file));
    var rows = json.RootElement.GetProperty("rows")
        .EnumerateArray()
        .Select(t => t.GetProperty("row"))
        .Select(t => new { title = t.GetProperty("title").GetString(), body = t.GetProperty("text").GetString() })
        .ToList();

    await db.ExecuteAsync("INSERT INTO dbo.documents (title, body) VALUES (@title, @body)", rows);
    Console.WriteLine($"page {i}: {rows.Count}");
}