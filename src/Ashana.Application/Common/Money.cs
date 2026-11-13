using System.Globalization;

namespace Ashana.Application.Common;

/// <summary>Formats tenge amounts the way they are written in Kazakhstan: "1 800 ₸".</summary>
public static class Money
{
    private static readonly NumberFormatInfo Format = new()
    {
        NumberGroupSeparator = " ", // narrow no-break space: the number never wraps across lines
        NumberDecimalSeparator = ",",
        NumberGroupSizes = [3],
    };

    public static string Tenge(decimal amount)
    {
        var format = decimal.Truncate(amount) == amount ? "#,0" : "#,0.00";
        return amount.ToString(format, Format) + " ₸";
    }
}
