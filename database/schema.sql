IF DB_ID(N'BeeHealth') IS NULL CREATE DATABASE BeeHealth;
GO
USE BeeHealth;
GO
CREATE TABLE dbo.Apiaries (
 Id int IDENTITY PRIMARY KEY,
 Name nvarchar(120) NOT NULL UNIQUE,
 Location nvarchar(200) NOT NULL
);
CREATE TABLE dbo.Hives (
 Id int IDENTITY PRIMARY KEY,
 ApiaryId int NOT NULL REFERENCES dbo.Apiaries(Id),
 Number nvarchar(40) NOT NULL,
 UNIQUE (ApiaryId, Number)
);
CREATE TABLE dbo.Colonies (
 Id int IDENTITY PRIMARY KEY,
 HiveId int NOT NULL REFERENCES dbo.Hives(Id),
 Name nvarchar(120) NOT NULL,
 SettledOn date NOT NULL,
 Active bit NOT NULL
);
CREATE UNIQUE INDEX UX_Colonies_ActiveHive ON dbo.Colonies(HiveId) WHERE Active = 1;
CREATE TABLE dbo.Inspections (
 Id int IDENTITY PRIMARY KEY,
 ColonyId int NOT NULL REFERENCES dbo.Colonies(Id),
 InspectedOn date NOT NULL,
 Condition nvarchar(120) NOT NULL,
 Notes nvarchar(500) NULL,
 UNIQUE (Id, ColonyId)
);
CREATE INDEX IX_Inspections_ColonyDate ON dbo.Inspections(ColonyId, InspectedOn);
CREATE TABLE dbo.TreatmentTypes (
 Id int IDENTITY PRIMARY KEY,
 Name nvarchar(120) NOT NULL UNIQUE
);
CREATE TABLE dbo.Treatments (
 Id int IDENTITY PRIMARY KEY,
 ColonyId int NOT NULL REFERENCES dbo.Colonies(Id),
 TypeId int NOT NULL REFERENCES dbo.TreatmentTypes(Id),
 InspectionId int NULL,
 PlannedOn date NOT NULL,
 Done bit NOT NULL,
 DoneOn date NULL,
 Notes nvarchar(500) NULL,
 FOREIGN KEY (InspectionId, ColonyId) REFERENCES dbo.Inspections(Id, ColonyId),
 CONSTRAINT CK_Treatments_DoneDate CHECK (
     (Done = 1 AND DoneOn IS NOT NULL) OR (Done = 0 AND DoneOn IS NULL)
 ),
 CONSTRAINT CK_Treatments_InspectionDone CHECK (InspectionId IS NULL OR Done = 1)
);
GO
CREATE TRIGGER dbo.Inspections_CheckDate ON dbo.Inspections AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS (
   SELECT 1 FROM inserted i JOIN dbo.Colonies c WITH (HOLDLOCK) ON c.Id = i.ColonyId
   WHERE i.InspectedOn < c.SettledOn
 )
 THROW 51001, N'Дата осмотра раньше даты заселения семьи.', 1;
END;
GO
CREATE TRIGGER dbo.Colonies_CheckDate ON dbo.Colonies AFTER UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF UPDATE(SettledOn) AND EXISTS (
   SELECT 1 FROM inserted c JOIN dbo.Inspections i WITH (HOLDLOCK) ON i.ColonyId = c.Id
   WHERE i.InspectedOn < c.SettledOn
 )
 THROW 51001, N'Дата заселения позже уже зарегистрированного осмотра.', 1;
END;
GO
