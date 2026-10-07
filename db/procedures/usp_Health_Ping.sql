CREATE OR ALTER PROCEDURE dbo.usp_Health_Ping
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CAST('OK' AS nvarchar(16)) AS [Status],
           SYSUTCDATETIME()           AS ServerTimeUtc;
END
GO
