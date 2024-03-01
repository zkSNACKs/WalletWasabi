namespace WalletWasabi.Services.Terminate;

public interface ITerminateService
{
	/// <summary>
	/// Signals that a graceful shutdown should occur after a crash of a single service of the application.
	/// </summary>
	/// <param name="ex">Exception with which a service crashed with.</param>
	void SignalServiceCrash(Exception ex);
}
