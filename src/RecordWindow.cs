using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BeeHealth;

internal sealed class RecordWindow : Window
{
    private readonly Dictionary<Field, Control> inputs = new();
    private readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkRed };
    private readonly ListBox? treatments;

    public RecordWindow(Section section, Database database, BeeService service, DataRow? row, int? id)
    {
        Title = $"{section.Title} · {(id.HasValue ? $"Запись №{id}" : "Новая запись")}";
        Width = 530;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 30;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        FontSize = 14;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage("ru-RU");

        var root = new DockPanel { Margin = new Thickness(22) };
        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        footer.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0) };
        footer.Children.Add(buttons);
        var save = new Button { Content = "Сохранить", Padding = new Thickness(16, 6, 16, 6), IsDefault = true };
        var cancel = new Button { Content = "Отмена", Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);

        var form = new StackPanel();
        root.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;

        foreach (var field in section.Fields)
        {
            object? value = row == null || row.IsNull(field.Key) ? null : row[field.Key];
            Control control = field.Kind switch
            {
                FieldKind.Date => new DatePicker { SelectedDate = value as DateTime? },
                FieldKind.Flag => new CheckBox { IsChecked = value as bool? ?? field.Key == "Active" },
                FieldKind.Lookup => new ComboBox { ItemsSource = database.Choices(field.Lookup!),
                    DisplayMemberPath = nameof(Choice.Label), SelectedValuePath = nameof(Choice.Id), SelectedValue = value },
                _ => new TextBox { Text = value as string ?? "", MaxLength = field.Length,
                    TextWrapping = TextWrapping.Wrap, MinHeight = field.Key == "Notes" ? 50 : 28 }
            };
            if (control is DatePicker picker)
                picker.DateValidationError += (_, _) => error.Text = "Введите дату в формате ДД.ММ.ГГГГ.";

            form.Children.Add(new Label { Content = field.Label, Target = control, Padding = new Thickness(0, 7, 0, 3) });
            form.Children.Add(control);
            inputs.Add(field, control);
        }

        if (section.Table == "Inspections" && id == null)
        {
            treatments = new ListBox { ItemsSource = database.Choices(Catalog.TypeChoices),
                DisplayMemberPath = nameof(Choice.Label), SelectionMode = SelectionMode.Multiple, MaxHeight = 130,
                MinHeight = 32 };
            form.Children.Add(new Label { Content = "Мероприятия, выполненные в день осмотра", Target = treatments });
            form.Children.Add(treatments);
            form.Children.Add(new TextBlock { Text = "Щелчком можно выбрать несколько видов.", Margin = new Thickness(0, 4, 0, 0) });
        }

        save.Click += (_, _) =>
        {
            try
            {
                error.Text = "";
                var values = inputs.ToDictionary(pair => pair.Key.Key, pair => GetValue(pair.Key, pair.Value));
                int[] types = treatments?.SelectedItems.Cast<Choice>().Select(choice => choice.Id).ToArray() ?? [];
                service.Save(section, id, values, types);
                DialogResult = true;
            }
            catch (Exception ex) { error.Text = ErrorText.For(ex); }
        };
    }

    private static object? GetValue(Field field, Control control)
    {
        object? value = control switch
        {
            TextBox text when !string.IsNullOrWhiteSpace(text.Text) => text.Text.Trim(),
            ComboBox combo => combo.SelectedValue,
            CheckBox check => check.IsChecked == true,
            DatePicker date => ReadDate(date, field.Label),
            _ => null
        };
        if (field.Required && value == null)
            throw new InvalidOperationException($"Заполните поле «{field.Label}».");
        return value;
    }

    private static DateTime? ReadDate(DatePicker picker, string label)
    {
        if (string.IsNullOrWhiteSpace(picker.Text)) return null;
        if (DateTime.TryParse(picker.Text, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out DateTime date))
            return date.Date;
        throw new InvalidOperationException($"Укажите корректную дату в поле «{label}».");
    }
}
