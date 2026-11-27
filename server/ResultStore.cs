using Microsoft.Data.SqlClient;

namespace LabAlert;

public sealed class ResultStore(IConfiguration configuration)
{
    private string ConnectionString => configuration.GetConnectionString("LabAlert")
        ?? throw new InvalidOperationException("Set ConnectionStrings__LabAlert.");

    public async Task SaveAsync(IReadOnlyList<LabResult> results, CancellationToken ct)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        foreach (var result in results)
        {
            await using var command = new SqlCommand("""
                INSERT INTO dbo.LabResults
                (PatientId, PatientName, Unit, OrderId, OrderName, TestCode, TestName, Value,
                 Units, ReferenceRange, AbnormalFlag, Severity)
                VALUES (@patientId, @patientName, @unit, @orderId, @orderName, @testCode, @testName,
                        @value, @units, @range, @flag, @severity)
                """, connection, transaction);
            command.Parameters.AddWithValue("@patientId", result.PatientId);
            command.Parameters.AddWithValue("@patientName", result.PatientName);
            command.Parameters.AddWithValue("@unit", result.Unit);
            command.Parameters.AddWithValue("@orderId", result.OrderId);
            command.Parameters.AddWithValue("@orderName", result.OrderName);
            command.Parameters.AddWithValue("@testCode", result.TestCode);
            command.Parameters.AddWithValue("@testName", result.TestName);
            command.Parameters.AddWithValue("@value", result.Value);
            command.Parameters.AddWithValue("@units", result.Units);
            command.Parameters.AddWithValue("@range", result.ReferenceRange);
            command.Parameters.AddWithValue("@flag", result.AbnormalFlag);
            command.Parameters.AddWithValue("@severity", result.Severity);
            await command.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    public async Task<List<object>> GetFlaggedAsync(string? patient, string? unit, CancellationToken ct)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT Id, PatientId, PatientName, Unit, OrderId, OrderName, TestCode, TestName, Value,
                   Units, ReferenceRange, AbnormalFlag, Severity, ReceivedAt
            FROM dbo.LabResults
            WHERE Severity <> 'Normal' AND AcknowledgedAt IS NULL
              AND (@patient = '' OR CHARINDEX(@patient, PatientId) > 0 OR CHARINDEX(@patient, PatientName) > 0)
              AND (@unit = '' OR CHARINDEX(@unit, Unit) > 0)
            ORDER BY CASE WHEN Severity = 'Critical' THEN 0 ELSE 1 END, ReceivedAt DESC, Id DESC
            """, connection);
        command.Parameters.AddWithValue("@patient", patient ?? "");
        command.Parameters.AddWithValue("@unit", unit ?? "");
        var results = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(new
            {
                id = reader.GetInt64(0), patientId = reader.GetString(1), patientName = reader.GetString(2),
                unit = reader.GetString(3), orderId = reader.GetString(4), orderName = reader.GetString(5),
                testCode = reader.GetString(6), testName = reader.GetString(7), value = reader.GetString(8),
                units = reader.GetString(9), referenceRange = reader.GetString(10), abnormalFlag = reader.GetString(11),
                severity = reader.GetString(12), receivedAt = reader.GetDateTime(13)
            });
        return results;
    }

    public async Task<bool> AcknowledgeAsync(long id, CancellationToken ct)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(
            "UPDATE dbo.LabResults SET AcknowledgedAt = SYSUTCDATETIME() WHERE Id = @id AND AcknowledgedAt IS NULL AND Severity <> 'Normal'", connection);
        command.Parameters.AddWithValue("@id", id);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }
}
