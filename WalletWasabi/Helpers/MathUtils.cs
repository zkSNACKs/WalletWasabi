using System.Collections.Generic;
using System.Linq;

namespace WalletWasabi.Helpers;

public static class MathUtils
{
	public static int Round(int n, int precision)
	{
		var fraction = n / (double)precision;
		var roundedFraction = Math.Round(fraction);
		var rounded = roundedFraction * precision;
		return (int)rounded;
	}

	public static decimal RoundToSignificantFigures(this decimal n, int precision)
	{
		if (n == 0)
		{
			return 0;
		}

		int d = (int)Math.Ceiling(Math.Log10((double)Math.Abs(n)));
		int power = precision - d;

		decimal magnitude = (decimal)Math.Pow(10, power);

		decimal shifted = Math.Round(n * magnitude, 0, MidpointRounding.AwayFromZero);
		decimal ret = shifted / magnitude;

		return ret;
	}

	public static (double, double) AverageStandardDeviation(IEnumerable<double> sequence)
	{
		var enumerable = sequence as double[] ?? sequence.ToArray();
		if (enumerable.Length > 0)
		{
			double average = enumerable.Average();
			double sum = enumerable.Sum(d => Math.Pow(d - average, 2));
			return (average, Math.Sqrt((sum) / enumerable.Length));
		}
		return (0, 0);
	}
}
