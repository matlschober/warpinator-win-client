using System;
using System.Globalization;

namespace WarpinatorWinClient.App.Interop;

internal static class ByteSizeFormatter
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    public static string Format(ulong bytes)
    {
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024d && unitIndex < Units.Length - 1)
        {
            value /= 1024d;
            unitIndex++;
        }

        var format = unitIndex == 0 ? "0" : "0.0";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{value.ToString(format, CultureInfo.InvariantCulture)} {Units[unitIndex]}");
    }
}
