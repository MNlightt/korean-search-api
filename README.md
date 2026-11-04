# korean-search-api

ASP.NET Core(.NET 10)와 MSSQL로 만드는 한국어 검색 API입니다.

## 로컬 실행

1. `db/init.sql`을 MSSQL에 실행합니다.
2. 연결 문자열을 user secrets에 넣습니다. (비밀번호는 커밋하지 않습니다.)

   ```bash
   dotnet user-secrets set "ConnectionStrings:Search" "<연결 문자열>" --project src/SearchApi
   ```

3. API를 실행합니다.

   ```bash
   dotnet run --project src/SearchApi
   ```
