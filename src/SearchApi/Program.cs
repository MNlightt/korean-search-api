using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);

// 개발: user secrets, 운영: 환경 변수 ConnectionStrings__Search
var connectionString = builder.Configuration.GetConnectionString("Search")
    ?? throw new InvalidOperationException("ConnectionStrings:Search가 설정되지 않았습니다.");

var app = builder.Build();

app.MapGet("/api/search", async (string? q, int page = 1, int size = 10) =>
{
    if (string.IsNullOrWhiteSpace(q))
        return Results.BadRequest("q 누락/빈 문자열");
    else if (page < 1)
        return Results.UnprocessableEntity("page는 1미만일 수 없습니다.");
    else if (size < 1 || size > 50)
        return Results.UnprocessableEntity("size는 1~50 사이여야 합니다.");

    var documentResult = new List<DocumentDto>();

    await using var conn = new SqlConnection(connectionString);
    try
    {
        await conn.OpenAsync();
    }
    catch (SqlException)
    {
        return Results.StatusCode(503);
    }
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        SELECT id, title, body, created_at
    FROM dbo.documents
    WHERE title LIKE N'%' + @q + N'%' OR body LIKE N'%' + @q + N'%'
    ORDER BY id DESC
    OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY;
    """;
    cmd.Parameters.AddWithValue("@q", q);
    cmd.Parameters.AddWithValue("@offset", (long)(page - 1) * size);
    cmd.Parameters.AddWithValue("@size", size);

    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        documentResult.Add(new DocumentDto(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetDateTime(3)));
    }


    return Results.Ok(documentResult);
});

app.Run();

record DocumentDto(int Id, string Title, string Body, DateTime CreatedAt);
