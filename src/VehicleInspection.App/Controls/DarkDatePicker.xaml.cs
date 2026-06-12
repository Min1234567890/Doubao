using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace VehicleInspection.App.Controls;

public partial class DarkDatePicker : UserControl
{
    public static readonly DependencyProperty SelectedDateProperty =
        DependencyProperty.Register(nameof(SelectedDate), typeof(DateTime?), typeof(DarkDatePicker),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedDateChanged));

    public DateTime? SelectedDate
    {
        get => (DateTime?)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    private DateTime _displayMonth;

    public DarkDatePicker()
    {
        InitializeComponent();
        _displayMonth = SelectedDate ?? DateTime.Today;
        _displayMonth = new DateTime(_displayMonth.Year, _displayMonth.Month, 1);
        Loaded += (_, _) => DateText.Text = SelectedDate?.ToString("yyyy-MM-dd") ?? string.Empty;
    }

    private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (DarkDatePicker)d;
        var dt = (DateTime?)e.NewValue;
        picker.DateText.Text = dt?.ToString("yyyy-MM-dd") ?? string.Empty;
        if (dt.HasValue)
            picker._displayMonth = new DateTime(dt.Value.Year, dt.Value.Month, 1);
        picker.RefreshCalendar();
    }

    private void CalendarBtn_Click(object sender, RoutedEventArgs e)
    {
        CalendarPopup.IsOpen = !CalendarPopup.IsOpen;
        if (CalendarPopup.IsOpen)
        {
            _displayMonth = SelectedDate ?? DateTime.Today;
            _displayMonth = new DateTime(_displayMonth.Year, _displayMonth.Month, 1);
            RefreshCalendar();
        }
    }

    private void DateText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CalendarPopup.IsOpen = !CalendarPopup.IsOpen;
        if (CalendarPopup.IsOpen)
        {
            _displayMonth = SelectedDate ?? DateTime.Today;
            _displayMonth = new DateTime(_displayMonth.Year, _displayMonth.Month, 1);
            RefreshCalendar();
        }
    }

    private void PrevMonth_Click(object sender, RoutedEventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(-1);
        RefreshCalendar();
    }

    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(1);
        RefreshCalendar();
    }

    private void RefreshCalendar()
    {
        MonthYearHeader.Text = _displayMonth.ToString("MMMM yyyy");

        DayGrid.Children.Clear();

        var firstDay = new DateTime(_displayMonth.Year, _displayMonth.Month, 1);
        int startOffset = (int)firstDay.DayOfWeek; // 0=Sunday
        int daysInMonth = DateTime.DaysInMonth(_displayMonth.Year, _displayMonth.Month);
        var today = DateTime.Today;
        var selected = SelectedDate;

        for (int i = 0; i < startOffset; i++)
        {
            DayGrid.Children.Add(new Border()); // empty cell
        }

        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = new DateTime(_displayMonth.Year, _displayMonth.Month, day);
            var btn = new Button
            {
                Content = day.ToString(),
                Width = 28,
                Height = 28,
                FontSize = 12,
                FontWeight = FontWeights.Normal,
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Style = null, // kill implicit Button style entirely
                Cursor = Cursors.Hand,
                Tag = date
            };

            // Today: blue border
            if (date == today)
            {
                btn.BorderBrush = new SolidColorBrush(Color.FromRgb(78, 163, 230));
                btn.BorderThickness = new Thickness(1);
                btn.FontWeight = FontWeights.Bold;
                btn.Foreground = new SolidColorBrush(Color.FromRgb(78, 163, 230));
            }
            // Selected: solid blue
            else if (date == selected)
            {
                btn.Background = new SolidColorBrush(Color.FromRgb(13, 75, 131)); // #0D4B83
                btn.Foreground = Brushes.White;
                btn.FontWeight = FontWeights.Bold;
            }
            // Weekend
            else if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
            {
                btn.Foreground = new SolidColorBrush(Color.FromRgb(158, 172, 186));
            }
            else
            {
                btn.Foreground = Brushes.White;
            }

            btn.Click += DayButton_Click;
            DayGrid.Children.Add(btn);
        }
    }

    private void DayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is DateTime date)
        {
            SelectedDate = date;
            CalendarPopup.IsOpen = false;
        }
    }
}
