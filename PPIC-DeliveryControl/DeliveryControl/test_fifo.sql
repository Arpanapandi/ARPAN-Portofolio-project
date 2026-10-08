-- Declare temporary tables to hold our data
DECLARE @Pulling TABLE (
    PullingId INT,
    Label NVARCHAR(MAX),
    CreatedDate DATETIME,
    Consumed BIT DEFAULT 0
);

DECLARE @Preparation TABLE (
    PreparationId INT,
    Label NVARCHAR(MAX),
    CreatedDate DATETIME
);

-- Insert pulling records for TA1700
INSERT INTO @Pulling (PullingId, Label, CreatedDate)
SELECT PullingId, Label, CreatedDate 
FROM PullingRecords 
WHERE Tag = 'TA1700' 
  AND Remark != 'Mismatch' 
  AND ISNULL(AdjustNote, '') != 'Opname Reduce'
  AND (Plant = 'Hose' OR Plant IS NULL OR Plant = '' OR Plant = 'Unknown' OR Plant = '-');

-- Insert preparation records for TA1700
INSERT INTO @Preparation (PreparationId, Label, CreatedDate)
SELECT PreparationId, Label, CreatedDate
FROM PreparationRecords
WHERE Tag = 'TA1700'
  AND Remark != 'Mismatch'
  AND (Plant = 'Hose' OR Plant = 'MADJUST' OR Plant IS NULL OR Plant = '' OR Plant = '-');

-- Iterate through Preparation records in order
DECLARE @PrepId INT, @PrepLabel NVARCHAR(MAX), @PrepDate DATETIME;
DECLARE prep_cursor CURSOR FOR
SELECT PreparationId, LTRIM(RTRIM(UPPER(ISNULL(Label, '')))), CreatedDate 
FROM @Preparation 
ORDER BY CreatedDate ASC, PreparationId ASC;

OPEN prep_cursor;
FETCH NEXT FROM prep_cursor INTO @PrepId, @PrepLabel, @PrepDate;

WHILE @@FETCH_STATUS = 0
BEGIN
    DECLARE @MatchedPullingId INT = NULL;

    -- Prioritas 1: Exact match
    SELECT TOP 1 @MatchedPullingId = PullingId
    FROM @Pulling
    WHERE Consumed = 0 
      AND LTRIM(RTRIM(UPPER(ISNULL(Label, '')))) = @PrepLabel
      AND CreatedDate <= DATEADD(second, 10, @PrepDate)
    ORDER BY PullingId ASC;

    IF @MatchedPullingId IS NULL
    BEGIN
        -- Check prefix match
        DECLARE @PrefixMatch BIT = 0;
        IF @PrepLabel != '' AND EXISTS (
            SELECT 1 FROM @Pulling
            WHERE Consumed = 0
              AND CreatedDate <= DATEADD(second, 10, @PrepDate)
              AND (LTRIM(RTRIM(UPPER(ISNULL(Label, '')))) LIKE @PrepLabel + '%' OR @PrepLabel LIKE LTRIM(RTRIM(UPPER(ISNULL(Label, '')))) + '%')
        )
        BEGIN
            SET @PrefixMatch = 1;
        END

        IF @PrepLabel = '' OR @PrefixMatch = 1
        BEGIN
            IF @PrefixMatch = 1
            BEGIN
                SELECT TOP 1 @MatchedPullingId = PullingId
                FROM @Pulling
                WHERE Consumed = 0
                  AND CreatedDate <= DATEADD(second, 10, @PrepDate)
                  AND (LTRIM(RTRIM(UPPER(ISNULL(Label, '')))) LIKE @PrepLabel + '%' OR @PrepLabel LIKE LTRIM(RTRIM(UPPER(ISNULL(Label, '')))) + '%')
                ORDER BY PullingId ASC;
            END
            ELSE
            BEGIN
                SELECT TOP 1 @MatchedPullingId = PullingId
                FROM @Pulling
                WHERE Consumed = 0
                  AND CreatedDate <= DATEADD(second, 10, @PrepDate)
                ORDER BY PullingId ASC;
            END
        END
    END

    IF @MatchedPullingId IS NOT NULL
    BEGIN
        UPDATE @Pulling SET Consumed = 1 WHERE PullingId = @MatchedPullingId;
        PRINT 'Prep ' + CAST(@PrepId AS VARCHAR) + ' consumed Pull ' + CAST(@MatchedPullingId AS VARCHAR);
    END

    FETCH NEXT FROM prep_cursor INTO @PrepId, @PrepLabel, @PrepDate;
END
CLOSE prep_cursor;
DEALLOCATE prep_cursor;

SELECT PullingId, Label, CreatedDate FROM @Pulling WHERE Consumed = 0;
