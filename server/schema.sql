CREATE TABLE dbo.LabResults (
    Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    PatientId nvarchar(max) NOT NULL,
    PatientName nvarchar(max) NOT NULL,
    Unit nvarchar(max) NOT NULL,
    OrderId nvarchar(max) NOT NULL,
    OrderName nvarchar(max) NOT NULL,
    TestCode nvarchar(max) NOT NULL,
    TestName nvarchar(max) NOT NULL,
    Value nvarchar(max) NOT NULL,
    Units nvarchar(max) NOT NULL,
    ReferenceRange nvarchar(max) NOT NULL,
    AbnormalFlag nvarchar(max) NOT NULL,
    Severity nvarchar(16) NOT NULL,
    ReceivedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
    AcknowledgedAt datetime2 NULL
);
