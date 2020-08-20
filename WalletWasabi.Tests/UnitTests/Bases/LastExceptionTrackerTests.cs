using System;
using System.IO;
using System.Text;
using WalletWasabi.Bases;
using WalletWasabi.Logging;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.Bases
{
	public class LastExceptionTrackerTests
	{
		[Fact]
		public void ProcessTest()
		{
			var consoleOutput = Console.Out;
			try
			{
				Logger.SetModes(LogMode.Console);
				Logger.SetMinimumLevel(LogLevel.Info);

				var log = new StringBuilder();
				using var writer = new StringWriter(log);
				Console.SetOut(writer);

				var let = new LastExceptionTracker();

				// Process first exception to process.
				let.Process(new ArgumentOutOfRangeException());
				Assert.Empty(log.ToString());

				// Same exception encountered.
				let.Process(new ArgumentOutOfRangeException());
				Assert.Empty(log.ToString());

				// No more exceptions are comming
				let.FinalizeExceptionsProcessing();
				Assert.Matches("It came for [^ ]+ seconds, 2 times: ArgumentOutOfRangeException", log.ToString());

				// Different exception encountered.
				let.Process(new NotImplementedException());
				let.FinalizeExceptionsProcessing();
				Assert.Matches("It came for [^ ]+ seconds, 1 times: NotImplementedException", log.ToString());
			}
			finally
			{
				Logger.SetModes();
				Logger.SetMinimumLevel(LogLevel.Critical);
				Console.SetOut(consoleOutput);
			}
/*
			// Different exception encountered.
			{
				let.Process(new NotImplementedException());

				Assert.IsType<NotImplementedException>(lastException.Exception);
				Assert.Equal(1, lastException.ExceptionCount);
			}
			*/
		}
	}
}
