namespace Griff.Data;

/// <summary>
/// The sums formulas and curves do that aren't plain adding, taking away, multiplying and dividing (which every machine
/// does the same, bit for bit): powers, e to a power and logarithms, worked out here from those alone rather than by the
/// machine's maths library, which may differ in the last digit from one machine, processor or browser to another. So a
/// formula gives the same answer in the editor, an exported game, every player of a network game and the web player.
/// </summary>
internal static class FormulaMath
{
    // ln 2 split in two, the first part with trailing zeros so k * Ln2Hi is exact for any k a double's exponent can be.
    private const double Ln2Hi = 6.93147180369123816490e-01;
    private const double Ln2Lo = 1.90821492927058770002e-10;
    private const double InvLn2 = 1.44269504088896338700e+00;
    private const double Sqrt2 = 1.41421356237309504880;

    /// <summary>e to the power <paramref name="x"/>.</summary>
    public static double Exp(double x)
    {
        if (double.IsNaN(x))
            return x;
        if (x > 709.78)
            return double.PositiveInfinity;
        if (x < -745.2)
            return 0;

        // x = k ln 2 + r, |r| <= ln 2 / 2, so e^x = 2^k e^r, and e^r is a short sum.
        var k = Math.Round(x * InvLn2);
        var r = x - k * Ln2Hi - k * Ln2Lo;
        var sum = 1.0;
        for (var n = 20; n >= 1; n--)
            sum = 1 + r * sum / n;
        return Math.ScaleB(sum, (int)k);
    }

    /// <summary>The natural logarithm of <paramref name="x"/>: NaN below 0, minus infinity at 0.</summary>
    public static double Log(double x)
    {
        if (double.IsNaN(x) || x < 0)
            return double.NaN;
        if (x == 0)
            return double.NegativeInfinity;
        if (double.IsPositiveInfinity(x))
            return x;

        // x = m 2^e with m between 1/sqrt 2 and sqrt 2, read from the number's bits.
        var e = 0;
        if (x < double.Epsilon * (1L << 53))
        {
            x *= 1L << 54;
            e = -54;
        }

        var bits = BitConverter.DoubleToInt64Bits(x);
        e += (int)((bits >> 52) & 0x7FF) - 1023;
        var m = BitConverter.Int64BitsToDouble((bits & 0x000FFFFFFFFFFFFFL) | 0x3FF0000000000000L);
        if (m > Sqrt2)
        {
            m /= 2;
            e++;
        }

        // ln m = 2 (s + s^3/3 + s^5/5 + ...) with s = (m - 1) / (m + 1), which is small.
        var s = (m - 1) / (m + 1);
        var s2 = s * s;
        var sum = 0.0;
        for (var n = 41; n >= 1; n -= 2)
            sum = 1.0 / n + s2 * sum;
        return e * Ln2Hi + (2 * s * sum + e * Ln2Lo);
    }

    /// <summary><paramref name="x"/> to the power <paramref name="y"/>; NaN for a negative number to a power that isn't whole.</summary>
    public static double Pow(double x, double y)
    {
        if (y == 0)
            return 1;
        if (double.IsNaN(x) || double.IsNaN(y))
            return double.NaN;
        if (y == Math.Floor(y) && Math.Abs(y) <= 1 << 30)
        {
            // A whole power: multiplied out by squaring.
            var n = (long)Math.Abs(y);
            var result = 1.0;
            var square = x;
            while (n > 0)
            {
                if ((n & 1) != 0)
                    result *= square;
                square *= square;
                n >>= 1;
            }

            return y < 0 ? 1 / result : result;
        }

        if (x < 0)
            return double.NaN;
        if (x == 0)
            return y > 0 ? 0 : double.PositiveInfinity;
        return Exp(y * Log(x));
    }
}
