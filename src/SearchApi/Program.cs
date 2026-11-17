using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OpenSearch.Client;

var builder = WebApplication.CreateBuilder(args);

// 개발: user secrets, 운영: 환경 변수 ConnectionStrings__Search
var connectionString = builder.Configuration.GetConnectionString("Search")
    ?? throw new InvalidOperationException("ConnectionStrings:Search가 설정되지 않았습니다.");

builder.Services.AddDbContext<SearchDbContext>(o => o.UseSqlServer(connectionString));

var osUrl = builder.Configuration["OpenSearch:Url"] ?? "http://localhost:9200";
builder.Services.AddSingleton(new OpenSearchClient(
    new ConnectionSettings(new Uri(osUrl)).DefaultIndex("documents")));

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

app.Run();

record DocumentDto(int Id, string Title, string Body, DateTime CreatedAt);
class DocSource { public string title { get; set; } = ""; public string body { get; set; } = ""; public DateTime created_at { get; set; } }
