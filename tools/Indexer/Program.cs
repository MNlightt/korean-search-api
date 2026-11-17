using Dapper;
using Microsoft.Data.SqlClient;
using OpenSearch.Client;
using System.Diagnostics;

var connStr = Environment.GetEnvironmentVariable("SEARCH_CONN")
    ?? throw new InvalidOperationException("SEARCH_CONN 환경 변수가 없습니다.");

var batchSize = 2500;
var last = 0;

var client = new OpenSearchClient(new Uri("http://localhost:9200"));

using var db = new SqlConnection(connStr);
await db.OpenAsync();

var sw = Stopwatch.StartNew();
while (true)
{
    var sql = "SELECT TOP (@n) id, title, body, created_at AS CreatedAt FROM dbo.documents WHERE id > @last ORDER BY id";
    var batch = (await db.QueryAsync<Doc>(sql, new { n = batchSize, last })).ToList();

    if (batch.Count == 0)
    {
        await client.Indices.RefreshAsync("documents");
        var os = await client.CountAsync<object>(c => c.Index("documents"));
        var ms = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.documents");
        Console.WriteLine($"OpenSearch {os.Count} / MSSQL {ms}");
        break;
    }

    var bulkResponse = await client.BulkAsync(b =>
    {
        foreach (var d in batch)
            b.Index<object>(op => op.Index("documents").Id(d.Id)
                .Document(new { title = d.Title, body = d.Body, created_at = d.CreatedAt }));
        return b;
    });

    if (bulkResponse.Errors)
    {
        foreach (var itemWithError in bulkResponse.ItemsWithErrors)
        {
            Console.WriteLine(itemWithError.Error.Reason);
        }
        break;
    }

    last = batch[^1].Id;
}

Console.WriteLine(sw.ElapsedMilliseconds);

    


record Doc(int Id, string Title, string Body, DateTime CreatedAt);