-- 3주차 v0.1 개발용 스키마와 샘플 데이터. 다시 실행해도 같은 상태가 된다.
IF DB_ID(N'SearchDb') IS NULL
    CREATE DATABASE SearchDb;
GO

USE SearchDb;
GO

DROP TABLE IF EXISTS dbo.documents;
GO

CREATE TABLE dbo.documents (
    id         INT IDENTITY(1,1) PRIMARY KEY,
    title      NVARCHAR(200) NOT NULL,
    body       NVARCHAR(MAX) NOT NULL,
    created_at DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

INSERT INTO dbo.documents (title, body) VALUES
    (N'한국어 형태소 분석 개요', N'형태소 분석은 문장을 의미 단위로 나누는 검색 색인의 첫 단계다.'),
    (N'역색인의 구조', N'역색인은 단어에서 문서 목록으로 가는 지도이며 검색 속도의 핵심이다.'),
    (N'BM25 점수 계산', N'BM25는 단어 빈도와 문서 길이를 함께 고려해 검색 결과의 순위를 매긴다.'),
    (N'벡터 검색 입문', N'임베딩으로 문장을 벡터로 바꾸고 코사인 유사도로 가까운 문서를 찾는다.'),
    (N'하이브리드 검색', N'키워드 검색과 벡터 검색의 결과를 합쳐 순위를 다시 매긴다.'),
    (N'트랜잭션 격리 수준', N'격리 수준은 동시에 실행되는 트랜잭션이 서로를 얼마나 볼 수 있는지 정한다.'),
    (N'데드락 대응', N'데드락은 자원 접근 순서를 통일하고 희생자 트랜잭션을 재시도해 대응한다.'),
    (N'커넥션 풀', N'커넥션 풀은 물리 연결을 재사용해 연결 생성 비용을 줄인다.'),
    (N'HTTP 메서드의 성질', N'GET은 안전하고 멱등하며 응답을 캐시할 수 있다.'),
    (N'검색 캐시 전략', N'자주 찾는 검색어의 결과를 캐시하면 원본 저장소의 부하가 줄어든다.'),
    (N'ASP.NET Core 호스팅', N'Kestrel은 기본 웹 서버이며 리버스 프록시 뒤에 둘 수 있다.'),
    (N'동시성 카운터 비교', N'lock, Interlocked, ConcurrentDictionary는 경합 비용이 서로 다르다.');
GO

SELECT COUNT(*) AS document_count FROM dbo.documents;
GO
