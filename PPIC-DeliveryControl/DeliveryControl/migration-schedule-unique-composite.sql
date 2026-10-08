-- Migration: ChangeScheduleNumberUniqueToComposite
-- Purpose: Change unique constraint on DeliverySchedules from ScheduleNumber-only
--          to composite (ScheduleNumber + ScheduledDate)
--          so same manifest number can exist on different dates
-- Run this script once on the production SQL Server database

-- Step 1: Drop the old unique index on ScheduleNumber only
IF EXISTS (
    SELECT 1 FROM sys.indexes i
    JOIN sys.tables t ON i.object_id = t.object_id
    WHERE t.name = 'DeliverySchedules'
      AND i.name = 'IX_DeliverySchedules_ScheduleNumber'
)
BEGIN
    DROP INDEX [IX_DeliverySchedules_ScheduleNumber] ON [dbo].[DeliverySchedules];
    PRINT 'Dropped old index IX_DeliverySchedules_ScheduleNumber';
END
ELSE
BEGIN
    PRINT 'Index IX_DeliverySchedules_ScheduleNumber does not exist, skipping drop';
END

-- Step 2: Create new composite unique index on (ScheduleNumber, ScheduledDate)
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes i
    JOIN sys.tables t ON i.object_id = t.object_id
    WHERE t.name = 'DeliverySchedules'
      AND i.name = 'IX_DeliverySchedules_ScheduleNumber_ScheduledDate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DeliverySchedules_ScheduleNumber_ScheduledDate]
        ON [dbo].[DeliverySchedules] ([ScheduleNumber], [ScheduledDate]);
    PRINT 'Created new composite unique index IX_DeliverySchedules_ScheduleNumber_ScheduledDate';
END
ELSE
BEGIN
    PRINT 'Composite index already exists, skipping';
END

PRINT 'Migration complete.';
