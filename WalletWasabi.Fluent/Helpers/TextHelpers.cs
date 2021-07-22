using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NBitcoin;

namespace WalletWasabi.Fluent.Helpers
{
	public static class TextHelpers
	{
		public static string AddSIfPlural(int n) => n > 1 ? "s" : "";

		public static string CloseSentenceIfZero(params int[] counts) => counts.All(x => x == 0) ? "." : " ";

		private static string ConcatNumberAndUnit(int n, string unit) => n > 0 ? $"{n} {unit}{AddSIfPlural(n)}" : "";

		private static void AddIfNotEmpty(List<string> list, string item)
		{
			if (!string.IsNullOrEmpty(item))
			{
				list.Add(item);
			}
		}

		public static string TimeSpanToFriendlyString(TimeSpan time)
		{
			var textMembers = new List<string>();
			string result = "";

			AddIfNotEmpty(textMembers, ConcatNumberAndUnit(time.Days, "day"));
			AddIfNotEmpty(textMembers, ConcatNumberAndUnit(time.Hours, "hour"));
			AddIfNotEmpty(textMembers, ConcatNumberAndUnit(time.Minutes, "minute"));
			AddIfNotEmpty(textMembers, ConcatNumberAndUnit(time.Seconds, "second"));

			for (int i = 0; i < textMembers.Count; i++)
			{
				result += textMembers[i];

				if (textMembers.Count > 1 && i < textMembers.Count - 2)
				{
					result += ", ";
				}
				else if (textMembers.Count > 1 && i == textMembers.Count - 2)
				{
					result += " and ";
				}
			}

			return result;
		}

		public static string GenerateFiatText(this decimal amountBtc, decimal exchangeRate, string fiatCode)
		{
			return GenerateFiatText(amountBtc * exchangeRate, fiatCode);
		}

		public static string GenerateFiatText(this decimal amountFiat, string fiatCode)
		{
			return $"(≈{(amountFiat).FormattedFiat()} {fiatCode}) ";
		}

		public static string ToFormattedString(this Money money)
		{
			const int WholeGroupSize = 3;

			var moneyString = money.ToString();

			moneyString = moneyString.Insert(moneyString.Length - 4, " ");

			var startIndex = moneyString.IndexOf(".", StringComparison.Ordinal) - WholeGroupSize;

			if (startIndex > 0)
			{
				for (var i = startIndex; i > 0; i -= WholeGroupSize)
				{
					moneyString = moneyString.Insert(i, " ");
				}
			}

			return moneyString;
		}

		public static string ParseLabel(this string text) => Regex.Replace(text, @"\s+", " ").Trim();
	}
}
