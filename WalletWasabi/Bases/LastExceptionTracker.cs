using System;
using WalletWasabi.Logging;

namespace WalletWasabi.Bases
{
	/// <summary>
	/// Tracker that stores the latest received exception, and increases a counter as long as the same exception type is received.
	/// </summary>
	public class LastExceptionTracker
	{
		private ExceptionInfo LastException { get; set; } = new ExceptionInfo();

		/// <summary>
		/// Process encountered exception and return the latest exception info.
		/// </summary>
		/// <returns>The latest exception.</returns>
		public void Process(Exception currentException) =>
			LastException = LastException switch
			{
				{ ExceptionCount: 0 } => LastException.Is(currentException),
				{ Exception: {} ex } when ex.GetType() == currentException.GetType() && ex.Message == currentException.Message => LastException.Again(),
				_ => LastException
			};

		public void FinalizeExceptionsProcessing()
		{
			var info = LastException;

			// Log previous exception if any.
			if (info.ExceptionCount > 0)
			{
				Logger.LogInfo($"Exception stopped coming. It came for " +
					$"{(DateTimeOffset.UtcNow - info.FirstAppeared).TotalSeconds} seconds, " +
					$"{info.ExceptionCount} times: {info.Exception.ToTypeMessageString()}");

				LastException = new ExceptionInfo();
			}
		}
	}
}
