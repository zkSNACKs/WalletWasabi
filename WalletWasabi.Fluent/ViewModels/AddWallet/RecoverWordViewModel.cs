using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Fluent.Validation;
using WalletWasabi.Models;

namespace WalletWasabi.Fluent.ViewModels.AddWallet;

public partial class RecoverWordViewModel : ViewModelBase
{
	[AutoNotify] private bool _isSelected;
	[AutoNotify] private string? _word;
	[AutoNotify] private IReadOnlyCollection<string> _suggestions;

	[AutoNotify(SetterModifier = AccessModifier.Private)]
	private bool _isValid;

	public RecoverWordViewModel(int index, string word, IReadOnlyCollection<string> suggestions)
	{
		Index = index;
		_word = word;
		_suggestions = suggestions;

		this.ValidateProperty(x => x.Word, ValidateWord);

		this.WhenAnyValue(x => x.Word)
			.Subscribe(_ =>
			{
				IsValid = !Validations.Any && !string.IsNullOrEmpty(Word);
			});
	}

	public int Index { get; }

	private void ValidateWord(IValidationErrors errors)
	{
		if (Word is null)
		{
			ClearValidations();
			return;
		}

		if (Suggestions.All(w => string.Compare(Word, w, StringComparison.OrdinalIgnoreCase) != 0))
		{
			errors.Add(ErrorSeverity.Error, "Invalid word.");
		}
	}

	public override string ToString()
	{
		return $"{Index}. {Word}";
	}
}
