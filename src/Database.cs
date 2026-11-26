using System.Data;
using Microsoft.Data.SqlClient;
namespace BeeHealth;

internal sealed class Database
{
    internal const string DefaultConnection = @"Server=(localdb)\MSSQLLocalDB;Database=BeeHealth;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=5";
    private readonly string connectionString =
        Environment.GetEnvironmentVariable("BEEHEALTH_CONNECTION") ?? DefaultConnection;

    internal SqlConnection Open()
    {
        var connection = new SqlConnection(connectionString);
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    internal static SqlCommand Command(SqlConnection connection, string sql,
        IEnumerable<KeyValuePair<string, object?>> values, SqlTransaction? transaction = null)
    {
        var command = new SqlCommand(sql, connection, transaction);
        foreach (var (key, value) in values)
        {
            var type = value switch
            {
                int => SqlDbType.Int, bool => SqlDbType.Bit, DateTime => SqlDbType.Date, _ => SqlDbType.NVarChar
            };
            var parameter = command.Parameters.Add("@" + key, type);
            parameter.Value = value ?? DBNull.Value;
        }
        return command;
    }

    internal DataTable Read(string sql, Dictionary<string, object?>? values = null)
    {
        using var connection = Open();
        using var command = Command(connection, sql, values ?? []);
        using var reader = command.ExecuteReader();
        var table = new DataTable();
        table.Load(reader);
        return table;
    }

    internal DataTable List(Section section, string search, DateTime? from, DateTime? to, bool? done = null)
    {
        string sql = $"SELECT * FROM ({section.Query}) q WHERE (@search = N'' OR CHARINDEX(@search, {section.Search}) > 0)";
        var parameters = new Dictionary<string, object?> { ["search"] = search };
        if (Catalog.DateColumn(section) is string date)
        {
            if (from.HasValue) { sql += $" AND {date} >= @from"; parameters["from"] = from.Value; }
            if (to.HasValue) { sql += $" AND {date} <= @to"; parameters["to"] = to.Value; }
        }
        if (done.HasValue) { sql += " AND [Статус] = @status"; parameters["status"] = done.Value ? "Выполнено" : "Запланировано"; }
        return Read(sql + " ORDER BY Id DESC", parameters);
    }

    internal Choice[] Choices(string sql) => Read($"SELECT * FROM ({sql}) q ORDER BY Label")
        .AsEnumerable().Select(row => new Choice(row.Field<int>("Id"), row.Field<string>("Label")!)).ToArray();

    internal DataRow Get(Section section, int id) =>
        Read($"SELECT * FROM {section.Table} WHERE Id=@id", new() { ["id"] = id })
        .AsEnumerable().SingleOrDefault() ?? throw new InvalidOperationException("Запись больше не существует.");

    internal static int Insert(SqlConnection connection, SqlTransaction? transaction, Section section,
        Dictionary<string, object?> values)
    {
        string fields = string.Join(", ", values.Keys);
        string parameters = string.Join(", ", values.Keys.Select(key => "@" + key));
        using var command = Command(connection,
            $"INSERT INTO {section.Table} ({fields}) VALUES ({parameters}); SELECT CONVERT(int, SCOPE_IDENTITY());",
            values, transaction);
        return (int)command.ExecuteScalar()!;
    }

    internal static void Update(SqlConnection connection, SqlTransaction transaction, Section section,
        int id, Dictionary<string, object?> values)
    {
        string assignments = string.Join(", ", values.Keys.Select(key => $"{key}=@{key}"));
        var parameters = new Dictionary<string, object?>(values) { ["id"] = id };
        using var command = Command(connection, $"UPDATE {section.Table} SET {assignments} WHERE Id=@id",
            parameters, transaction);
        if (command.ExecuteNonQuery() == 0) throw new InvalidOperationException("Запись больше не существует.");
    }

    internal void Delete(Section section, int id)
    {
        using var connection = Open();
        using var command = Command(connection, $"DELETE FROM {section.Table} WHERE Id=@id",
            new Dictionary<string, object?> { ["id"] = id });
        if (command.ExecuteNonQuery() == 0) throw new InvalidOperationException("Запись больше не существует.");
    }

    internal DataTable ActiveColonies() => Read("""
        SELECT a.Name AS [Пасека], COUNT(c.Id) AS [Активные семьи]
        FROM Apiaries a LEFT JOIN Hives h ON h.ApiaryId=a.Id
        LEFT JOIN Colonies c ON c.HiveId=h.Id AND c.Active=1
        GROUP BY a.Id, a.Name ORDER BY a.Name
        """);
}
