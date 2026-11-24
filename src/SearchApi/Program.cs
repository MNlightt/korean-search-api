using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OpenSearch.Client;
using System.Diagnostics;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// 개발: user secrets, 운영: 환경 변수 ConnectionStrings__Search
var connectionString = builder.Configuration.GetConnectionString("Search")
    ?? throw new InvalidOperationException("ConnectionStrings:Search가 설정되지 않았습니다.");

builder.Services.AddDbContext<SearchDbContext>(o => o.UseSqlServer(connectionString));

var osUrl = builder.Configuration["OpenSearch:Url"] ?? "http://localhost:9200";
builder.Services.AddSingleton(new OpenSearchClient(
    new ConnectionSettings(new Uri(osUrl)).DefaultIndex("documents")));

var embedUrl = builder.Configuration["Embed:Url"] ?? "http://127.0.0.1:8001";
builder.Services.AddHttpClient("EmbedClient", client =>
{
    client.BaseAddress = new Uri(embedUrl);
});

var app = builder.Build();

app.MapDataAccessEndpoints(connectionString);

app.MapGet("/api/search", async (OpenSearchClient client, string? q, int page = 1, int size = 10) =>
{
    if (q == null)
        return Results.BadRequest("q 누락");
    else if (string.IsNullOrWhiteSpace(q))
        return Results.UnprocessableEntity("q는 빈 문자열일 수 없습니다.");
    else if (page < 1)
        return Results.UnprocessableEntity("page는 1미만일 수 없습니다.");
    else if (size < 1 || size > 50)
        return Results.UnprocessableEntity("size는 1~50 사이여야 합니다.");

    var documentResult = new List<DocumentDto>();

    var res = await client.SearchAsync<DocSource>(s => s
    .From((page - 1) * size)
    .Size(size)
    .Query(x => x.Match(m => m.Field("body").Query(q))));

    if (!res.IsValid)
        return res.ApiCall.HttpStatusCode is null
            ? Results.StatusCode(503)   // 연결 자체 실패
            : Results.StatusCode(502);  // OpenSearch가 오류 응답

    foreach (var hit in res.Hits)
    {
        documentResult.Add(new DocumentDto(int.Parse(hit.Id), hit.Source.title, hit.Source.body, hit.Source.created_at));
    }


    return Results.Ok(documentResult);
});

app.MapGet("/api/search/vector", async (OpenSearchClient osClient, IHttpClientFactory httpClientFactory, ILogger<Program> logger, string? q, int size = 10) =>
{
    if (q == null)
        return Results.BadRequest("q 누락");
    else if (string.IsNullOrWhiteSpace(q))
        return Results.UnprocessableEntity("q는 빈 문자열일 수 없습니다.");
    else if (size < 1 || size > 50)
        return Results.UnprocessableEntity("size는 1~50 사이여야 합니다.");

    var sw = Stopwatch.StartNew();

    var httpClient = httpClientFactory.CreateClient("EmbedClient");
    try
    {
        var httpResponseMessage = await httpClient.GetAsync($"embed?q={Uri.EscapeDataString(q)}");
        if (!httpResponseMessage.IsSuccessStatusCode)
        {
            var body = await httpResponseMessage.Content.ReadAsStringAsync();
            logger.LogError("임베딩 서버 오류 {Status} {Body}", (int)httpResponseMessage.StatusCode, body);
            return Results.StatusCode(502);
        }

        using var response = await httpResponseMessage.Content.ReadAsStreamAsync();

        logger.LogInformation("embedding ellapsed: {Elapsed} ms", sw.ElapsedMilliseconds);
        sw.Restart();

        var embedVector = await JsonSerializer.DeserializeAsync<EmbedModel>(response);

        if (embedVector == null)
            return Results.InternalServerError("임베딩 역직렬화에 실패하였습니다.");

        var documentResult = new List<DocumentVec>();

        var res = await osClient.SearchAsync<DocSource>(s => s
        .Index("documents_vec")
        .Query(x => x.Knn(k => k.Field("embedding").Vector(embedVector.embed).K(size)))
        .Source(src => src.Excludes(e => e.Field("embedding"))));

        logger.LogInformation("vector search ellapsed: {Elapsed} ms", sw.ElapsedMilliseconds);

        if (!res.IsValid)
        {
            if (res.ApiCall.HttpStatusCode is null)
            {
                logger.LogError(res.OriginalException, "OpenSearchClinet 연결 실패.");
                return Results.StatusCode(503);
            }
            logger.LogError("Open Search 오류 {Status} {Type} {Reason}", res.ServerError?.Status, res.ServerError?.Error?.Type, res.ServerError?.Error?.Reason);
            return Results.StatusCode(502);
        }

        foreach (var hit in res.Hits)
        {
            documentResult.Add(new DocumentVec(int.Parse(hit.Id), hit.Source.title, hit.Source.body, hit.Source.created_at, hit.Score));
        }

        return Results.Ok(documentResult);
    }
    catch (HttpRequestException ex)
    {
        logger.LogError(ex, "http exception.");
        return Results.StatusCode(503);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "server error");
        return Results.InternalServerError();
    }
});

app.Run();

record DocumentDto(int Id, string Title, string Body, DateTime CreatedAt);
record DocumentVec(int Id, string Title, string Body, DateTime CreatedAt, double? Score);
class DocSource { public string title { get; set; } = ""; public string body { get; set; } = ""; public DateTime created_at { get; set; } }
class EmbedModel { public required float[] embed { get; set; } }