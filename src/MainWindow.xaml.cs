using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Data.SqlClient;

namespace BeeHealth;

public partial class MainWindow : Window
{
    private readonly Database database = new();
    private readonly BeeService service;
    private Section? section;
    private bool ready;

    public MainWindow()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        Language = System.Windows.Markup.XmlLanguage.GetLanguage("ru-RU");
        service = new BeeService(database);
        InitializeComponent();
        Navigation.ItemsSource = Catalog.Sections.Select(item => item.Title).Append("Сводка").ToArray();
        Loaded += (_, _) => { ready = true; Navigation.SelectedIndex = 0; };
    }

    private void Navigate(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || Navigation.SelectedIndex < 0) return;
        section = Catalog.Sections.ElementAtOrDefault(Navigation.SelectedIndex);
        Heading.Text = section?.Title ?? "Аналитическая сводка";
        bool report = section is null;
        Hint.Text = report ? "Период применяется к осмотрам и мероприятиям. Счётчик семей показывает текущее состояние."
            : section?.Table == "Colonies" ? "Откройте карточку семьи двойным щелчком по строке."
            : "Введите текст для поиска или выберите запись для изменения.";
        Actions.Visibility = report ? Visibility.Collapsed : Visibility.Visible;
        SearchBox.Visibility = report ? Visibility.Collapsed : Visibility.Visible;
        Period.Visibility = report || Catalog.DateColumn(section!) != null ? Visibility.Visible : Visibility.Collapsed;
        EditButton.Content = section?.Table == "Colonies" ? "Карточка семьи" : "Изменить";
        ResetFilters();
        LoadRecords();
    }

    private void LoadRecords(string? message = null) => Try(() =>
    {
        DateTime? from = ReadDate(FromDate), to = ReadDate(ToDate);
        if (from > to) throw new InvalidOperationException("Начало периода должно быть не позже окончания.");
        if (section is null)
        {
            var report = new TabControl();
            AddReport(report, "Активные семьи", database.ActiveColonies());
            AddReport(report, "Осмотры", database.List(Catalog.Sections[3], "", from, to));
            AddReport(report, "Выполнено", database.List(Catalog.Sections[4], "Выполнено", from, to, true));
            AddReport(report, "Запланировано", database.List(Catalog.Sections[4], "Запланировано", from, to, false));
            Records.ItemsSource = null;
            if (Records.Parent is DockPanel host)
            {
                const string tag = "Report";
                foreach (var previous in host.Children.OfType<TabControl>().Where(t => Equals(t.Tag, tag)).ToArray())
                    host.Children.Remove(previous);
                Records.Visibility = Visibility.Collapsed;
                report.Tag = tag;
                host.Children.Add(report);
            }
            Status.Text = "Выберите вкладку сводки.";
            return;
        }
        if (Records.Parent is DockPanel panel)
            foreach (var report in panel.Children.OfType<TabControl>().ToArray()) panel.Children.Remove(report);
        Records.Visibility = Visibility.Visible;
        var table = database.List(section, SearchBox.Text.Trim(), from, to);
        Records.ItemsSource = table.DefaultView;
        Status.Text = message ?? (table.Rows.Count == 0 ? "Записи не найдены." : $"Записей: {table.Rows.Count}");
    });

    private static void AddReport(TabControl tabs, string title, DataTable data)
    {
        var grid = new DataGrid();
        grid.AutoGeneratingColumn += FormatColumn;
        grid.ItemsSource = data.DefaultView;
        tabs.Items.Add(new TabItem { Header = $"{title} ({data.Rows.Count})",
            Content = data.Rows.Count > 0 ? grid : new TextBlock
            { Text = "Записи не найдены.", Margin = new Thickness(16) } });
    }

    private static void FormatColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.PropertyName == "Id") e.Column.Header = "№";
        if (e.PropertyType == typeof(DateTime) && e.Column is DataGridBoundColumn column && column.Binding is Binding binding)
            binding.StringFormat = "dd.MM.yyyy";
    }

    private static DateTime? ReadDate(DatePicker picker)
    {
        if (string.IsNullOrWhiteSpace(picker.Text)) return null;
        if (DateTime.TryParse(picker.Text, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out DateTime date))
            return date.Date;
        throw new InvalidOperationException("Укажите дату в формате ДД.ММ.ГГГГ.");
    }

    private void ResetFilters() { SearchBox.Clear(); FromDate.SelectedDate = null; ToDate.SelectedDate = null; FromDate.Text = ""; ToDate.Text = ""; }
    private void Apply(object sender, RoutedEventArgs e) => LoadRecords();
    private void Reset(object sender, RoutedEventArgs e) { ResetFilters(); LoadRecords(); }
    private void SearchKey(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) LoadRecords(); }
    private void BadDate(object sender, DatePickerDateValidationErrorEventArgs e) => Status.Text = "Введите дату в формате ДД.ММ.ГГГГ.";
    private void Add(object sender, RoutedEventArgs e) => OpenEditor(null);
    private void Edit(object sender, RoutedEventArgs e) { if (SelectedId() is int id) OpenEditor(id); }
    private void OpenRecord(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(Records, source) is DataGridRow &&
            Records.SelectedItem is DataRowView row) OpenEditor((int)row["Id"]);
    }

    private void OpenEditor(int? id) => Try(() =>
    {
        if (section is null) return;
        var row = id.HasValue ? database.Get(section, id.Value) : null;
        var editor = new RecordWindow(section, database, service, row, id) { Owner = this };
        if (editor.ShowDialog() == true) LoadRecords("Изменения сохранены.");
    });

    private int? SelectedId()
    {
        if (Records.SelectedItem is DataRowView row) return (int)row["Id"];
        Status.Text = "Сначала выберите запись.";
        return null;
    }

    private void Delete(object sender, RoutedEventArgs e) => Try(() =>
    {
        if (section is null || SelectedId() is not int id) return;
        if (MessageBox.Show(this, $"Удалить запись №{id}? Действие необратимо.", "BeeHealth",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        database.Delete(section, id);
        LoadRecords("Запись удалена.");
    });

    private void Try(Action action)
    {
        try { action(); }
        catch (Exception ex) { Status.Text = ErrorText.For(ex); }
    }
}

internal static class ErrorText
{
    internal static string For(Exception ex) => ex switch
    {
        InvalidOperationException => ex.Message,
        SqlException sql when sql.Number == 51001 => "Проверьте даты заселения и осмотров семьи.",
        SqlException sql when sql.Number is 2627 or 2601 => "Такая запись уже существует или в улье уже есть активная семья.",
        SqlException sql when sql.Number == 547 => "Изменение запрещено из-за связанных записей или ограничения данных. Изменения не сохранены.",
        SqlException => "Операция SQL Server не выполнена. Проверьте подключение и наличие базы BeeHealth.",
        _ => "Операция не выполнена. Изменения не сохранены."
    };
}
