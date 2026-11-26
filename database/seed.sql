USE BeeHealth;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.Apiaries)
BEGIN
 DECLARE @Today date = CAST(GETDATE() AS date);
 INSERT dbo.Apiaries(Name, Location) VALUES (N'Луговая', N'Берёзовая долина');
 DECLARE @Meadow int = CONVERT(int, SCOPE_IDENTITY());
 INSERT dbo.Apiaries(Name, Location) VALUES (N'Таёжная', N'Сосновый распадок');
 DECLARE @Forest int = CONVERT(int, SCOPE_IDENTITY());
 INSERT dbo.Hives(ApiaryId, Number) VALUES (@Meadow,N'01');
 DECLARE @Hive int = CONVERT(int, SCOPE_IDENTITY());
 INSERT dbo.Hives(ApiaryId, Number) VALUES (@Meadow,N'02'),(@Forest,N'01');
 INSERT dbo.Colonies(HiveId, Name, SettledOn, Active)
 VALUES (@Hive,N'Рябинка',DATEADD(day,-30,@Today),1);
 DECLARE @Colony int = CONVERT(int, SCOPE_IDENTITY());
 INSERT dbo.Colonies(HiveId, Name, SettledOn, Active)
 SELECT Id,N'Кедровая',DATEADD(day,-45,@Today),1 FROM dbo.Hives WHERE ApiaryId=@Forest;
 INSERT dbo.TreatmentTypes(Name) VALUES (N'Замена рамок');
 DECLARE @Type int = CONVERT(int, SCOPE_IDENTITY());
 INSERT dbo.TreatmentTypes(Name) VALUES (N'Очистка дна'),(N'Контроль состояния улья');
 INSERT dbo.Inspections(ColonyId, InspectedOn, Condition, Notes)
 VALUES (@Colony, DATEADD(day,-3,@Today), N'Стабильное', N'Расплод равномерный');
 DECLARE @Inspection int = CONVERT(int, SCOPE_IDENTITY());
 INSERT dbo.Treatments(ColonyId,TypeId,InspectionId,PlannedOn,Done,DoneOn)
 VALUES (@Colony,@Type,@Inspection,DATEADD(day,-3,@Today),1,DATEADD(day,-3,@Today)),
        (@Colony,@Type,NULL,DATEADD(day,7,@Today),0,NULL);
END;
COMMIT;
GO
