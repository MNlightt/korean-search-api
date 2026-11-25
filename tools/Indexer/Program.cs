using Dapper;
using Microsoft.Data.SqlClient;
using OpenSearch.Client;
using System.Diagnostics;
using System.Runtime.InteropServices;

if (args.Length != 1 || args[0] is not ("documents" or "documents_vec"))
{
    Console.WriteLine("사용법: dotnet run --project tools/Indexer -- documents|documents_vec");
    return;
}
var indexName = args[0];
var isVec = indexName == "documents_vec";

var connStr = Environment.GetEnvironmentVariable("SEARCH_CONN")
    ?? throw new InvalidOperationException("SEARCH_CONN 환경 변수가 없습니다.");

var batchSize = isVec ? 1000 : 2500;
var last = 0;

var settings = new ConnectionSettings(new Uri("http://localhost:9200")).DisableDirectStreaming();

var client = new OpenSearchClient(settings);

using var db = new SqlConnection(connStr);
await db.OpenAsync();

var dbDocumentIds = await db.QueryAsync<int>("SELECT id FROM dbo.documents;");

var idRowPairs = new Dictionary<int, int>();
float[]? vectors = null;
long[]? ids = null;
if (isVec)
{
    vectors = MemoryMarshal.Cast<byte, float>(File.ReadAllBytes("data/embeddings/vectors.bin")).ToArray();
    ids = MemoryMarshal.Cast<byte, long>(File.ReadAllBytes("data/embeddings/ids.bin")).ToArray();

    if (vectors.Length % 1024 != 0 || vectors.Length / 1024 != ids.Length)
    {
        Console.WriteLine($"vectors Length:{vectors.Length / 1024}, ids Length: {ids.Length}");
        throw new InvalidDataException("벡터 수와 id 수가 다릅니다.");
    }

    if (ids.Length != dbDocumentIds.Count())
        throw new InvalidDataException("id 수와 db 문서 수가 다릅니다.");

    for (int row = 0; row < ids.Length; row++)
    {
        idRowPairs.Add((int)ids[row], row);
    }
}
var sw = Stopwatch.StartNew();

var isFirst = true;

while (true)
{
    var sql = "SELECT TOP (@n) id, title, body, created_at AS CreatedAt FROM dbo.documents WHERE id > @last ORDER BY id";
    var batch = (await db.QueryAsync<Doc>(sql, new { n = batchSize, last })).ToList();

    if (batch.Count == 0)
    {
        await client.Indices.RefreshAsync(indexName);
        var os = await client.CountAsync<object>(c => c.Index(indexName));
        var ms = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.documents");
        Console.WriteLine($"OpenSearch {os.Count} / MSSQL {ms}");
        break;
    }

    var bulkResponse = await client.BulkAsync(b =>
    {
        foreach (var d in batch)
        {
            if (!isVec)
                b.Index<object>(op => op.Index(indexName).Id(d.Id)
                    .Document(new { title = d.Title, body = d.Body, created_at = d.CreatedAt }));
            else if (idRowPairs.TryGetValue(d.Id, out int row))
                b.Index<object>(op => op.Index(indexName).Id(d.Id)
                    .Document(new
                    {
                        title = d.Title,
                        body = d.Body,
                        created_at = d.CreatedAt,
                        embedding = vectors!.AsSpan(row * 1024, 1024).ToArray()
                    }));
            else
                Console.WriteLine($"ID {d.Id}에 해당하는 vector가 없습니다.");
        }


        return b;
    });

    if (isFirst)
    {
        Console.WriteLine($"첫 배치 요청 {bulkResponse.ApiCall.RequestBodyInBytes.Length:N0} bytes");
        isFirst = false;
    }

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