using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using VehicleInspection.Application.Models;

namespace VehicleInspection.App.Localization;

public sealed class StatusDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is InspectionStatus status)
            return Loc.Get("Status" + status);
        return value ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s)
        {
            foreach (var status in Enum.GetValues<InspectionStatus>())
                if (Loc.Get("Status" + status) == s)
                    return status;
        }
        return value;
    }

    public static IReadOnlyList<string> GetDisplayOptions() =>
        Enum.GetValues<InspectionStatus>().Select(s => Loc.Get("Status" + s)).ToList();
}
