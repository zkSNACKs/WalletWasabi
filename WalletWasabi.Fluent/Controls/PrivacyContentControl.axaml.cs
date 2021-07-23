using Avalonia;
using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Controls;
using ReactiveUI;

namespace WalletWasabi.Fluent.Controls
{
	public class PrivacyContentControl : ContentControl
	{
		public static readonly StyledProperty<bool> PrivacyModeEnabledProperty =
			AvaloniaProperty.Register<PrivacyContentControl, bool>(nameof(PrivacyModeEnabled));

		private bool PrivacyModeEnabled
		{
			get => GetValue(PrivacyModeEnabledProperty);
			set => SetValue(PrivacyModeEnabledProperty, value);
		}

		private CompositeDisposable? _disposable;

		protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnAttachedToVisualTree(e);

			_disposable = new CompositeDisposable();

			Services.UiConfig
				.WhenAnyValue(x => x.PrivacyMode)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(value => PrivacyModeEnabled = value)
				.DisposeWith(_disposable);
		}

		protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnDetachedFromVisualTree(e);

			_disposable?.Dispose();
			_disposable = null;
		}
	}
}
