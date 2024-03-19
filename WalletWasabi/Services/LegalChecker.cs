using Nito.AsyncEx;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Legal;
using WalletWasabi.Logging;
using WalletWasabi.Services.Events;

namespace WalletWasabi.Services;

public class LegalChecker : IDisposable
{
	public const string LegalFolderName = "Legal2";
	public const string ProvisionalLegalFolderName = "Provisional";

	private bool _disposedValue;

	public LegalChecker(string dataDir, EventBus eventBus)
	{
		LegalFolder = Path.Combine(dataDir, LegalFolderName);
		ProvisionalLegalFolder = Path.Combine(LegalFolder, ProvisionalLegalFolderName);
		LegalDocumentVersionSubscription = eventBus.Subscribe<LegalDocumentVersionChanged>(OnLegalDocumentVersionChanged);
	}

	public IDisposable LegalDocumentVersionSubscription { get; }

	/// <remarks>Lock object to guard <see cref="CurrentLegalDocument"/> and <see cref="ProvisionalLegalDocument"/> property.</remarks>
	private AsyncLock LegalDocumentLock { get; } = new();

	//private UpdateChecker UpdateChecker { get; }
	public string LegalFolder { get; }
	public string ProvisionalLegalFolder { get; }
	public LegalDocuments? CurrentLegalDocument { get; private set; }
	private LegalDocuments? ProvisionalLegalDocument { get; set; }
	private TaskCompletionSource<LegalDocuments> LatestDocumentTaskCompletion { get; } = new();

	public async Task InitializeAsync()
	{
		CurrentLegalDocument = await LegalDocuments.LoadAgreedAsync(LegalFolder).ConfigureAwait(false);
		ProvisionalLegalDocument = await LegalDocuments.LoadAgreedAsync(ProvisionalLegalFolder).ConfigureAwait(false);

		if (ProvisionalLegalDocument is { } provisional)
		{
			LatestDocumentTaskCompletion.TrySetResult(provisional);
		}
		else if (CurrentLegalDocument is { } current)
		{
			LatestDocumentTaskCompletion.TrySetResult(current);
		}
	}

	public async Task<LegalDocuments> WaitAndGetLatestDocumentAsync(CancellationToken cancellationToken)
	{
		if (TryGetNewLegalDocs(out var provisionalLegal))
		{
			return provisionalLegal;
		}
		if (CurrentLegalDocument is { } currentLegal)
		{
			return currentLegal;
		}

		return await LatestDocumentTaskCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
	}

	public bool TryGetNewLegalDocs([NotNullWhen(true)] out LegalDocuments? legalDocuments)
	{
		legalDocuments = null;

		if (ProvisionalLegalDocument is { } legal)
		{
			legalDocuments = legal;
			return true;
		}

		return false;
	}

	private async void OnLegalDocumentVersionChanged(LegalDocumentVersionChanged evnt)
	{
		var legalDocumentsVersion = evnt.Version;
		try
		{
			LegalDocuments? provisionalLegalDocument = null;

			using (await LegalDocumentLock.LockAsync().ConfigureAwait(false))
			{
				// If we don't have it or there is a new one.
				if (CurrentLegalDocument is null || CurrentLegalDocument.Version < legalDocumentsVersion)
				{
					// UpdateChecker cannot be null as the event called by it.
					var content = ""; //await WasabiClient.GetLegalDocumentsAsync(CancellationToken.None).ConfigureAwait(false);

					// Save it as a provisional legal document.
					provisionalLegalDocument = new(legalDocumentsVersion, content);
					await provisionalLegalDocument.ToFileAsync(ProvisionalLegalFolder).ConfigureAwait(false);

					ProvisionalLegalDocument = provisionalLegalDocument;
					LatestDocumentTaskCompletion.TrySetResult(ProvisionalLegalDocument);
				}
			}
		}
		catch (Exception ex)
		{
			Logger.LogError("Could not get legal documents.", ex);
		}
	}

	public async Task AgreeAsync()
	{
		using (await LegalDocumentLock.LockAsync().ConfigureAwait(false))
		{
			if (ProvisionalLegalDocument is not { } provisionalLegalDocument || string.IsNullOrEmpty(provisionalLegalDocument.Content))
			{
				throw new InvalidOperationException("Cannot agree the new legal document.");
			}

			await provisionalLegalDocument.ToFileAsync(LegalFolder).ConfigureAwait(false);
			LegalDocuments.RemoveCandidates(ProvisionalLegalFolder);

			CurrentLegalDocument = ProvisionalLegalDocument;
			ProvisionalLegalDocument = null;
		}
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_disposedValue)
		{
			if (disposing)
			{
				LegalDocumentVersionSubscription.Dispose();
				LatestDocumentTaskCompletion.TrySetCanceled();
			}

			_disposedValue = true;
		}
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
