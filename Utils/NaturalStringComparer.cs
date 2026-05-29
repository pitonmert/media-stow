using System.Text.RegularExpressions;

namespace MediaStow.Utils;

public partial class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    [GeneratedRegex(@"(\d+)", RegexOptions.Compiled)]
    private static partial Regex NumericRegex();

    public int Compare(string? x, string? y)
    {
        if (x == null || y == null)
            return string.Compare(x, y);

        var partsX = NumericRegex().Split(x);
        var partsY = NumericRegex().Split(y);

        for (int i = 0; i < Math.Min(partsX.Length, partsY.Length); i++)
        {
            int result;
            if (int.TryParse(partsX[i], out int numX) && int.TryParse(partsY[i], out int numY))
                result = numX.CompareTo(numY);
            else
                result = string.Compare(partsX[i], partsY[i], StringComparison.OrdinalIgnoreCase);

            if (result != 0)
                return result;
        }

        return partsX.Length.CompareTo(partsY.Length);
    }
}
