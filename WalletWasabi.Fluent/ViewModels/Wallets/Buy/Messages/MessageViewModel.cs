﻿using System.Windows.Input;
using ReactiveUI;
using WalletWasabi.BuyAnything;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Buy.Messages;

public abstract partial class MessageViewModel : ReactiveObject
{
	[AutoNotify] private string? _id;
	[AutoNotify] private string? _uiMessage;
	[AutoNotify] private bool _isUnread;
	[AutoNotify] private bool _isPaid; // TODO: Should only be in PayNowAssistantMessageViewModel

	protected MessageViewModel(
		ICommand? editCommand,
		IObservable<bool>? canEditObservable,
		ChatMessageMetaData metaData)
	{
		EditCommand = editCommand;
		CanEditObservable = canEditObservable;
		MetaData = metaData;
	}

	public string? OriginalMessage { get; set; }

	public ICommand? EditCommand { get; }

	public IObservable<bool>? CanEditObservable { get; }

	public ChatMessageMetaData MetaData { get; protected set; }
}
