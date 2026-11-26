using System.Data;
using System.Text.RegularExpressions;
using BeeHealth;
using Microsoft.Data.SqlClient;

if (args is ["--setup"])
{
    var builder = new SqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("BEEHEALTH_CONNECTION") ?? Database.DefaultConnection)
    { InitialCatalog = "master" };
    using var connection = new SqlConnection(builder.ConnectionString);
    connection.Open();
    foreach (string file in new[] { "schema.sql", "seed.sql" })
    {
        foreach (string batch in Regex.Split(File.ReadAllText(Path.Combine("database", file)), @"(?im)^\s*GO\s*$"))
        {
            if (string.IsNullOrWhiteSpace(batch)) continue;
            using var command = new SqlCommand(batch, connection);
            command.ExecuteNonQuery();
        }
    }
    Console.WriteLine("База создана, демонстрационные данные загружены.");
    return;
}

var database = new Database();
var service = new BeeService(database);
var inspections = Catalog.Sections[3];
var treatments = Catalog.Sections[4];
int colonyId = database.Choices(Catalog.ColonyChoices).First().Id;
int validType = database.Choices(Catalog.TypeChoices).First().Id;
DateTime settled = database.Get(Catalog.Sections[2], colonyId).Field<DateTime>("SettledOn");
string token = $"CHECK-{Guid.NewGuid():N}";
Dictionary<string, object?> inspection = new()
{
    ["ColonyId"] = colonyId, ["InspectedOn"] = settled,
    ["Condition"] = "Проверка", ["Notes"] = token
};

int InspectionCount(string note) => database.Read("SELECT COUNT(*) FROM Inspections WHERE Notes=@note",
    new() { ["note"] = note }).Rows[0].Field<int>(0);
int TreatmentCount() => database.Read("SELECT COUNT(*) FROM Treatments").Rows[0].Field<int>(0);
int? created = null;
try
{
    int before = TreatmentCount();
    inspection["InspectedOn"] = settled.AddDays(-1);
    try { service.Save(inspections, null, inspection, [validType]); throw new Exception("Ожидался отказ по дате."); }
    catch (InvalidOperationException error) when (error.Message.Contains("раньше даты заселения")) { }
    if (InspectionCount(token) != 0 || TreatmentCount() != before) throw new Exception("Остались записи после отказа.");
    Console.WriteLine("BH-04 PASS · Осмотр раньше заселения отклонён.");

    try
    {
        service.Save(treatments, null, new() { ["Done"] = true, ["DoneOn"] = null }, []);
        throw new Exception("Ожидался отказ при отсутствии даты выполнения.");
    }
    catch (InvalidOperationException error) when (error.Message == "Укажите дату выполнения мероприятия.") { }
    if (TreatmentCount() != before) throw new Exception("Появилось мероприятие без даты.");
    Console.WriteLine("BH-05 PASS · Выполнение без даты отклонено.");

    inspection["InspectedOn"] = settled;
    service.Save(inspections, null, inspection, [validType]);
    created = database.Read("SELECT Id FROM Inspections WHERE Notes=@note", new() { ["note"] = token })
        .Rows[0].Field<int>("Id");
    var linked = database.Read("""
        SELECT TypeId, Done, DoneOn FROM Treatments WHERE InspectionId=@id
        """, new() { ["id"] = created.Value });
    if (linked.Rows.Count != 1 || linked.Rows[0].Field<int>("TypeId") != validType ||
        !linked.Rows[0].Field<bool>("Done") || linked.Rows[0].Field<DateTime>("DoneOn") != settled)
        throw new Exception("Не найдена ожидаемая связанная запись.");
    Console.WriteLine("BH-03 PASS · Осмотр и выполненное мероприятие сохранены.");

    inspection["Notes"] = token + "-ROLLBACK";
    try
    {
        service.Save(inspections, null, inspection, [validType, -1]);
        throw new Exception("Ожидалось нарушение внешнего ключа.");
    }
    catch (SqlException ex) when (ex.Number == 547) { }
    if (InspectionCount(token + "-ROLLBACK") != 0 || TreatmentCount() != before + 1)
        throw new Exception("Откат неполный.");
    Console.WriteLine("BH-06 PASS · Ошибка второй записи откатила осмотр и первое мероприятие.");
}
finally
{
    if (created.HasValue)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        using var cleanup = Database.Command(connection,
            "DELETE FROM Treatments WHERE InspectionId=@id; DELETE FROM Inspections WHERE Id=@id;",
            new Dictionary<string, object?> { ["id"] = created.Value }, transaction);
        cleanup.ExecuteNonQuery();
        transaction.Commit();
    }
}
