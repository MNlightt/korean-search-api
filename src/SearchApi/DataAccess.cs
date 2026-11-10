using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Comparison endpoints for the same search query: Dapper vs EF Core.
static class DataAccessEndpoints
{
    public static void MapDataAccessEndpoints(this IEndpointRouteBuilder app, string connectionString)
    {
        app.MapGet("/api/compare/dapper", async (string q, int page = 1, int size = 10) =>
        {
            if (Invalid(page, size) is { } invalid) return invalid;

            await using var conn = new SqlConnection(connectionString);

            var sql = """
            
            SELECT id, title, body, created_at as CreatedAt
            FROM dbo.documents
            WHERE title LIKE N'%' + @q + N'%' OR body LIKE N'%' + @q + N'%'
            ORDER BY id DESC
            OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY;
            
            """;
            var result = await conn.QueryAsync<DocumentDto>(sql, new { q = q, size = size, offset = (long)(page - 1) * size });
            return Results.Ok(result);
        });

        app.MapGet("/api/compare/ef", async (SearchDbContext db, string q, int page = 1, int size = 10) =>
        {
            if (Invalid(page, size) is { } invalid) return invalid;

            var result = await db.Documents.AsNoTracking().Where(t => t.Title.Contains(q) || t.Body.Contains(q)).OrderByDescending(d => d.Id).Skip((page - 1) * size).Take(size).Select(t => new DocumentDto(t.Id, t.Title, t.Body, t.CreatedAt)).ToListAsync();
            return Results.Ok(result);
        });

        app.MapPost("/api/compare/rank", async (DateOnly date, int top = 3) =>
        {
            await using var conn = new SqlConnection(connectionString);

            var result = await conn.ExecuteAsync("dbo.usp_build_keyword_rank", new { target_date = date, top_n = top }, commandType: System.Data.CommandType.StoredProcedure);
            return Results.Ok(result);
        });
    }

    static IResult? Invalid(int page, int size) =>
        page < 1 || size < 1 || size > 50
            ? Results.UnprocessableEntity("page는 1 이상, size는 1~50이어야 합니다.")
            : null;
}

class SearchDbContext(DbContextOptions<SearchDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();

    protected override void OnModelCreating(ModelBuilder model) =>
        model.Entity<Document>(e =>
        {
            e.ToTable("documents");
            e.Property(d => d.Id).HasColumnName("id");
            e.Property(d => d.Title).HasColumnName("title");
            e.Property(d => d.Body).HasColumnName("body");
            e.Property(d => d.CreatedAt).HasColumnName("created_at");
        });
}

class Document
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
