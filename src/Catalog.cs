namespace BeeHealth;

internal enum FieldKind { Text, Date, Flag, Lookup }
internal record Choice(int Id, string Label);
internal record Field(string Key, string Label, FieldKind Kind = FieldKind.Text,
    int Length = 120, bool Required = true, string? Lookup = null);
internal record Section(string Title, string Table, string Query, string Search, Field[] Fields);

internal static class Catalog
{
    internal const string ColonyLabel = "c.Name + N' · ' + a.Name + N' / ' + h.Number";
    private const string ColonyJoins = "JOIN Hives h ON h.Id=c.HiveId JOIN Apiaries a ON a.Id=h.ApiaryId";
    internal const string ColonyChoices = "SELECT c.Id, " + ColonyLabel + " AS Label FROM Colonies c " + ColonyJoins;
    internal const string TypeChoices = "SELECT Id, Name AS Label FROM TreatmentTypes";

    internal static readonly Section[] Sections =
    [
        new("Пасеки", "Apiaries", "SELECT Id, Name AS [Пасека], Location AS [Местоположение] FROM Apiaries",
            "[Пасека] + [Местоположение]", [new("Name", "Название"), new("Location", "Местоположение", Length: 200)]),
        new("Ульи", "Hives", """
            SELECT h.Id, a.Name AS [Пасека], h.Number AS [Номер улья]
            FROM Hives h JOIN Apiaries a ON a.Id=h.ApiaryId
            """, "[Пасека] + [Номер улья]", [
                new("ApiaryId", "Пасека", FieldKind.Lookup, Lookup: "SELECT Id, Name AS Label FROM Apiaries"),
                new("Number", "Номер улья", Length: 40)]),
        new("Семьи", "Colonies", """
            SELECT c.Id, c.Name AS [Семья], a.Name AS [Пасека], h.Number AS [Улей],
                   c.SettledOn AS [Дата заселения], CASE c.Active WHEN 1 THEN N'Активна' ELSE N'Архив' END AS [Статус]
            FROM Colonies c
            """ + " " + ColonyJoins, "[Семья] + [Пасека] + [Улей] + [Статус]", [
                new("HiveId", "Улей", FieldKind.Lookup, Lookup: """
                    SELECT h.Id, a.Name + N' / ' + h.Number AS Label FROM Hives h JOIN Apiaries a ON a.Id=h.ApiaryId
                    """), new("Name", "Название семьи"), new("SettledOn", "Дата заселения", FieldKind.Date),
                new("Active", "Семья активна", FieldKind.Flag)]),
        new("Журнал осмотров", "Inspections", """
            SELECT i.Id, {{FAMILY}} AS [Семья], i.InspectedOn AS [Дата осмотра],
                   i.Condition AS [Состояние], i.Notes AS [Заметки]
            FROM Inspections i JOIN Colonies c ON c.Id=i.ColonyId
            """.Replace("{{FAMILY}}", ColonyLabel) + " " + ColonyJoins,
            "[Семья] + [Состояние] + COALESCE([Заметки], N'')", [
                new("ColonyId", "Семья", FieldKind.Lookup, Lookup: ColonyChoices),
                new("InspectedOn", "Дата осмотра", FieldKind.Date), new("Condition", "Состояние"),
                new("Notes", "Заметки", Length: 500, Required: false)]),
        new("Профилактика", "Treatments", """
            SELECT t.Id, {{FAMILY}} AS [Семья], tt.Name AS [Вид мероприятия], t.PlannedOn AS [План],
                   CASE t.Done WHEN 1 THEN N'Выполнено' ELSE N'Запланировано' END AS [Статус],
                   t.DoneOn AS [Дата выполнения], t.Notes AS [Заметки], t.InspectionId AS [Осмотр №]
            FROM Treatments t JOIN TreatmentTypes tt ON tt.Id=t.TypeId JOIN Colonies c ON c.Id=t.ColonyId
            """.Replace("{{FAMILY}}", ColonyLabel) + " " + ColonyJoins,
            "[Семья] + [Вид мероприятия] + [Статус]", [
                new("ColonyId", "Семья", FieldKind.Lookup, Lookup: ColonyChoices),
                new("TypeId", "Вид мероприятия", FieldKind.Lookup, Lookup: TypeChoices),
                new("PlannedOn", "Плановая дата", FieldKind.Date), new("Done", "Выполнено", FieldKind.Flag),
                new("DoneOn", "Дата выполнения", FieldKind.Date, Required: false),
                new("Notes", "Заметки", Length: 500, Required: false)]),
        new("Виды мероприятий", "TreatmentTypes", "SELECT Id, Name AS [Вид мероприятия] FROM TreatmentTypes",
            "[Вид мероприятия]", [new("Name", "Название")])
    ];

    internal static string? DateColumn(Section section) => section.Table switch
    {
        "Inspections" => "[Дата осмотра]",
        "Treatments" => "COALESCE([Дата выполнения], [План])",
        _ => null
    };
}
