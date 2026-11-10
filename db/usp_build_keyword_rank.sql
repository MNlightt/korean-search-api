-- Stored Procedure for daily keyword ranking. Run after init.sql; safe to re-run.
USE SearchDb;
GO

DROP TABLE IF EXISTS dbo.keyword_rank;
DROP TABLE IF EXISTS dbo.search_log;
CREATE TABLE dbo.search_log (
    id          INT IDENTITY PRIMARY KEY,
    keyword     NVARCHAR(50) NOT NULL,
    searched_at DATETIME2    NOT NULL
);
CREATE TABLE dbo.keyword_rank (
    rank_date DATE         NOT NULL,
    rank_no   INT          NOT NULL,
    keyword   NVARCHAR(50) NOT NULL,
    cnt       INT          NOT NULL
);
INSERT INTO dbo.search_log (keyword, searched_at) VALUES
    (N'search',  '2026-10-27 09:00'), (N'search',  '2026-10-27 10:00'), (N'search', '2026-10-27 11:00'),
    (N'news',    '2026-10-27 09:30'), (N'news',    '2026-10-27 12:00'),
    (N'weather', '2026-10-27 13:00'), (N'weather', '2026-10-27 14:00'),
    (N'stock',   '2026-10-27 15:00'),
    (N'search',  '2026-10-28 09:00');
GO

CREATE OR ALTER PROCEDURE dbo.usp_build_keyword_rank
    @target_date DATE,
    @top_n INT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRAN;

        DELETE FROM dbo.keyword_rank
        WHERE rank_date = @target_date;

        INSERT INTO dbo.keyword_rank (rank_date, rank_no, keyword, cnt)
        SELECT TOP (@top_n)
               @target_date,
               ROW_NUMBER() OVER (ORDER BY COUNT(*) DESC, keyword) AS rank_no,
               keyword,
               COUNT(*) AS cnt
        FROM dbo.search_log
        WHERE searched_at >= @target_date
          AND searched_at <  DATEADD(DAY, 1, @target_date)
        GROUP BY keyword
        ORDER BY rank_no;

        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO
