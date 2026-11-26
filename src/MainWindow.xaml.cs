using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;

namespace BeeHealth;

public partial class MainWindow : Window
{
    private readonly Database database = new();
    private readonly BeeService service;
    private readonly MediaPlayer welcomeMusic = new() { Volume = 0.15 };
    private Section? section;
    private bool ready;

    public MainWindow()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        Language = System.Windows.Markup.XmlLanguage.GetLanguage("ru-RU");
        service = new BeeService(database);
        InitializeComponent();
        Navigation.ItemsSource = Catalog.Sections.Select(item => item.Title).Append("Сводка").ToArray();
        ContentRendered += StartWelcomeMusic;
        welcomeMusic.MediaEnded += (_, _) =>
        {
            if (ready) return;
            welcomeMusic.Position = TimeSpan.Zero;
            welcomeMusic.Play();
        };
        Closed += (_, _) => welcomeMusic.Close();
    }

    private void StartWelcomeMusic(object? sender, EventArgs e)
    {
        ContentRendered -= StartWelcomeMusic;
        if (ready) return;
        string audioPath = Path.Combine(AppContext.BaseDirectory, "Assets", "kto-pchelok-uvazhaet.mp3");
        if (!File.Exists(audioPath)) return;
        welcomeMusic.Open(new Uri(audioPath, UriKind.Absolute));
        welcomeMusic.Play();
    }

    private void AcceptBees(object sender, RoutedEventArgs e)
    {
        Welcome.Visibility = Visibility.Collapsed;
        Workspace.Visibility = Visibility.Visible;
        ready = true;
        welcomeMusic.Close();
        Navigation.SelectedIndex = 0;
        Navigation.Focus();
    }

    private void DeclineBees(object sender, RoutedEventArgs e)
    {
        LocationText.Text = $"Регион Windows — {ReadWindowsRegion()}\nЧасовой пояс — {TimeZoneInfo.Local.DisplayName}";
        LocationPanel.Visibility = Visibility.Visible;
    }

    private static string ReadWindowsRegion()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\International\Geo");
            return key?.GetValue("Name") is string code
                ? new RegionInfo(code).DisplayName
                : "не указан";
        }
        catch (ArgumentException)
        {
            return "не указан";
        }
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
        Records.ItemsSource = null;
        Report.Items.Clear();
        Records.Visibility = report ? Visibility.Collapsed : Visibility.Visible;
        Report.Visibility = report ? Visibility.Visible : Visibility.Collapsed;
        ResetFilters();
        LoadRecords();
    }

    private void LoadRecords(string? message = null) => Try(() =>
    {
        DateTime? from = ReadDate(FromDate), to = ReadDate(ToDate);
        if (from > to) throw new InvalidOperationException("Начало периода должно быть не позже окончания.");
        if (section is null)
        {
            var active = database.ActiveColonies();
            var inspections = database.List(Catalog.Sections[3], "", from, to);
            var done = database.List(Catalog.Sections[4], "", from, to, true);
            var planned = database.List(Catalog.Sections[4], "", from, to, false);
            Report.Items.Clear();
            AddReport(Report, "Активные семьи", active);
            AddReport(Report, "Осмотры", inspections);
            AddReport(Report, "Выполнено", done);
            AddReport(Report, "Запланировано", planned);
            Report.SelectedIndex = 0;
            Status.Text = "Выберите вкладку сводки.";
            return;
        }
        var table = database.List(section, SearchBox.Text.Trim(), from, to);
        Records.ItemsSource = table.DefaultView;
        Status.Text = message ?? (table.Rows.Count == 0 ? "Записи не найдены." : $"Записей: {table.Rows.Count}");
    });

    private void AddReport(TabControl tabs, string title, DataTable data)
    {
        var grid = new DataGrid();
        grid.AutoGeneratingColumn += FormatColumn;
        grid.ItemsSource = data.DefaultView;
        tabs.Items.Add(new TabItem { Header = $"{title} ({data.Rows.Count})",
            Content = data.Rows.Count > 0 ? grid : new TextBlock
            { Text = "Записи не найдены.", Margin = new Thickness(16) } });
    }

    private void FormatColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
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
        var editor = new RecordWindow(section, database, service, row, id) { Owner = this, Resources = Resources, Background = Background };
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
