using Microsoft.Data.SqlClient;
namespace BeeHealth;

internal sealed class BeeService(Database database)
{
    internal void Save(Section section, int? id, Dictionary<string, object?> values, IReadOnlyList<int> types)
    {
        if (section.Table == "Treatments" && (bool)values["Done"]! != (values["DoneOn"] != null))
            throw new InvalidOperationException((bool)values["Done"]!
                ? "Укажите дату выполнения мероприятия." : "Уберите дату выполнения у запланированного мероприятия.");

        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            if (section.Table == "Inspections")
                CheckInspectionDate(connection, transaction, values);

            if (id.HasValue)
                Database.Update(connection, transaction, section, id.Value, values);
            else
            {
                int newId = Database.Insert(connection, transaction, section, values);
                if (section.Table == "Inspections")
                {
                    foreach (int type in types)
                    {
                        Database.Insert(connection, transaction, Catalog.Sections[4], new()
                        {
                            ["ColonyId"] = values["ColonyId"], ["TypeId"] = type, ["InspectionId"] = newId,
                            ["PlannedOn"] = values["InspectedOn"], ["Done"] = true,
                            ["DoneOn"] = values["InspectedOn"]
                        });
                    }
                }
            }
            transaction.Commit();
        }
        catch
        {
            if (transaction.Connection != null) transaction.Rollback();
            throw;
        }
    }

    private static void CheckInspectionDate(SqlConnection connection, SqlTransaction transaction,
        Dictionary<string, object?> values)
    {
        using var check = Database.Command(connection, "SELECT SettledOn FROM Colonies WITH (UPDLOCK, HOLDLOCK) WHERE Id=@id",
            new Dictionary<string, object?> { ["id"] = values["ColonyId"] }, transaction);
        if (check.ExecuteScalar() is not DateTime settled)
            throw new InvalidOperationException("Выбранная семья больше не существует.");
        if ((DateTime)values["InspectedOn"]! < settled)
            throw new InvalidOperationException("Дата осмотра не может быть раньше даты заселения семьи.");
    }
}
