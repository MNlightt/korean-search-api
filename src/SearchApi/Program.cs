var builder = WebApplication.CreateBuilder(args);

// 개발: user secrets, 운영: 환경 변수 ConnectionStrings__Search
var connectionString = builder.Configuration.GetConnectionString("Search")
    ?? throw new InvalidOperationException("ConnectionStrings:Search가 설정되지 않았습니다.");

var app = builder.Build();

// TODO: GET /api/search?q={검색어}&page={1..}&size={1..50}
// documents(id, title, body, created_at)를 조회해 JSON으로 반환한다.
app.MapGet("/api/search", () => TypedResults.StatusCode(StatusCodes.Status501NotImplemented));

app.Run();
