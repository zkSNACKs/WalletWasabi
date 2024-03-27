# Contributing to Wasabi Wallet

## How to be useful for the project

- Any issue labelled as [good first issue](https://github.com/zkSNACKs/WalletWasabi/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22) is good to start contributing to Wasabi.
- Always focus on a specific issue in your pull request and avoid unrelated/unnecessary changes.
- Avoid working on complex problems (fees, amount decomposition, coin selection...) without extensive research on the context, either on Github or asking to contributors.
- Avoid working on a UI or UX feature without first seeing a conclusion from a UX meeting.
- Consider filing a new issue or explaining in an opened issue the change that you want to make, and wait for concept ACKs to work on the implementation.
- For backend, the [Relevance Realization Buffet](https://github.com/orgs/zkSNACKs/projects/18/views/48) view is a list of tasks that has to be investigated or tackled. You can assign yourself to an issue or just make the pull request.
- Feel free to join the [zkSNACKs Slack Server](https://join.slack.com/t/tumblebit/shared_invite/enQtNjQ1MTQ2NzQ1ODI0LWIzOTg5YTM3YmNkOTg1NjZmZTQ3NmM1OTAzYmQyYzk1M2M0MTdlZDk2OTQwNzFiNTg1ZmExNzM0NjgzY2M0Yzg) to discuss with other contributors. 
- [Status calls](meet.zksnacks.com/research) are held on Mondays at 15:00 UTC to discuss what we did, and [peer programming calls](https://meet.zksnacks.com/code) on Thursdays at 13:30 UTC for coding together.

## Automatic code clean up

**Visual Studio IDE:**

**DO** use [CodeMaid](https://www.codemaid.net/), a Visual Studio extension to automatically clean up your code on saving the file.
CodeMaid is a non-intrusive code cleanup tool.

Wasabi's CodeMaid settings [can be found in the root of the repository](https://github.com/zkSNACKs/WalletWasabi/blob/master/CodeMaid.config). They are automatically picked up by Visual Studio when you open the project, assuming the CodeMaid extension is installed. Unfortunately CodeMaid has no Visual Studio Code extension yet. You can check out the progress on this [under this GitHub issue](https://github.com/codecadwallader/codemaid/issues/273).

**Rider IDE:**

In Rider, you can achieve similar functionality by going to `Settings -> Tools -> Action on Save` and enabling `Reformat and Cleanup Code` and changing `Profile` to `Reformat Code`.

And also enable `Enable EditorConfig support` in `Settings -> Editor -> Code Style`.

![image](https://user-images.githubusercontent.com/16364053/159900227-627f4b67-e793-421b-836a-09660971c807.png)
![image](https://user-images.githubusercontent.com/16364053/159900956-539868b7-9fd2-44ed-9ec6-d58569bd9dbb.png)

## .editorconfig

Not only CodeMaid, but Visual Studio also enforces consistent coding style through [`.editorconfig`](https://github.com/zkSNACKs/WalletWasabi/blob/master/.editorconfig) file.

If you are using Visual Studio Code make sure to install "C# Dev Kit" extension and add the following settings to your settings file:

```json
"editor.formatOnSave": true
```

## Technologies and scope

- [.NET SDK](https://dotnet.microsoft.com/en-us/): free, open-source, cross-platform framework for building apps. SDK version path: [WalletWasabi/global.json](https://github.com/zkSNACKs/WalletWasabi/blob/master/global.json).
- [C#](https://dotnet.microsoft.com/en-us/languages/csharp): open-source programming language.
- Model-View-ViewModel architecture (MVVM).
- [Avalonia UI](https://www.avaloniaui.net/): framework to create cross-platform UI.
- [xUnit](https://xunit.net/): create unit tests.
- Dependencies path: [WalletWasabi/Directory.Packages.props](https://github.com/zkSNACKs/WalletWasabi/blob/master/Directory.Packages.props).
- Developer's documentation path: [WalletWasabi/WalletWasabi.Documentation/](https://github.com/zkSNACKs/WalletWasabi/tree/master/WalletWasabi.Documentation).

# Code conventions

## Refactoring

If you are a new contributor **DO** keep refactoring pull requests short, uncomplex and easy to verify. It requires a certain level of experience to know where the code belongs to and to understand the full ramification (including rebase effort of open pull requests) - [source](https://github.com/bitcoin/bitcoin/blob/master/CONTRIBUTING.md#refactoring).

## Comments

**DO** follow [Microsoft's C# commenting conventions](https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/inside-a-program/coding-conventions#commenting-conventions).

- Place the comment on a separate line, not at the end of a line of code.
- Begin comment text with an uppercase letter.
- End comment text with a period.
- Insert one space between the comment delimiter (`//`) and the comment text, as shown in the following example.
- Do not create formatted blocks of asterisks around comments.

```cs
// The following declaration creates a query. It does not run
// the query.
```

## Asynchronous Locking

**DO NOT** mix awaitable and non-awaitable locks.

```cs
// GOOD
private AsyncLock AsyncLock { get; } = new();
using (await AsyncLock.LockAsync())
{
	...
}

// GOOD
private object Lock { get; } = new();
lock (Lock)
{
	...
}

// BAD
using (AsyncLock.Lock())
{
	...
}
```

## Null Check

**DO** use `is null` instead of `== null`. It was a performance consideration in the past but from C# 7.0 it does not matter anymore, today we use this convention to keep our code consistent.

```cs
if (foo is null)
{
	return;
}
```

## Empty quotes

**DO** use `""` instead of `string.Empty` for consistency.

```cs
if (foo is null)
{
	return "";
}
```

## Blocking

**DO NOT** block with `.Result, .Wait(), .GetAwaiter().GetResult()`. Never.

```cs
// BAD
IoHelpers.DeleteRecursivelyWithMagicDustAsync(Folder).GetAwaiter().GetResult();
```

## `async void`

**DO NOT** `async void`, except for event subscriptions. `async Task` instead.
**DO** `try catch` in `async void`, otherwise the software can crash.

```cs
{
	MyClass.SomethingHappened += MyClass_OnSomethingHappenedAsync;
}

// GOOD
private async void Synchronizer_ResponseArrivedAsync(object? sender, EventArgs e)
{
	try
	{
		await FooAsync();
	}
	catch (Exception ex)
	{
		Logger.LogError<MyClass2>(ex);
	}
}
```
- [Async/Await - Best Practices](https://docs.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming)

## `ConfigureAwait(false)`

Basically every async library method should use `ConfigureAwait(false)` except:
- Methods that touch objects on the UI Thread, like modifying UI controls.
- Methods that are unit tests, xUnit [Fact].

**Usage:**
```cs
await MyMethodAsync().ConfigureAwait(false);
```

**Top level synchronization**
```cs
// Later we need to modify UI control so we need to sync back to this thread, thus don't use .ConfigureAwait(false);.
// Note: inside MyMethodAsync() you can still use .ConfigureAwait(false);.
var result = await MyMethodAsync();

// At this point we are still on the UI thread, so you can safely touch UI elements.
myUiControl.Text = result;
```

- [ConfigureAwait FAQ](https://devblogs.microsoft.com/dotnet/configureawait-faq/)

## Never throw AggregateException and Exception in a mixed way
It causes confusion and awkward catch clauses.
[Example](https://github.com/zkSNACKs/WalletWasabi/pull/10353/files)

## Unused return value

- Good: `using IDisposable _ = BenchmarkLogger.Measure();`
- Bad: `_ = PrevOutsIndex.Remove(txInput.PrevOut);`
- Bad: `_ = Directory.CreateDirectory(dir);`
- Good: `_ = WaitAsync();` - disables warning message. Remark: you should always `await` or store the reference of the task.
- Good: `_ = new HwiClient(network);`

In general
- If the return value is not used, write nothing.
- In cases when the object needs to be disposed, but you do not need the object, `_ =` should be used.
- In case you want to create an object but do not need the reference, `_ =` should be used.
- If it generates a compiler warning, investigate, and if you are sure you can suppress the warning with `_ =` but elaborate on it with a comment.
- In special cases `_ =` can be used but a reasonable elaboration is required by adding a comment above. 

---

# UI Coding Conventions

The following is a list of UI specific coding conventions. Follow these any time you are contributing code in the following projects:
 - `WalletWasabi.Fluent`
 - `WalletWasabi.Fluent.Desktop`
 - `WalletWasabi.Fluent.Generators`

 ## Disposing Subscriptions in ReactiveObjects

**DO** follow [ReactiveUI's Subscription Disposing Conventions](https://reactiveui.net/docs/guidelines/framework/dispose-your-subscriptions).

**DO** dispose your subscription if you are referencing another object. **DO** use the `.DisposeWith()` method.

```cs
Observable.FromEventPattern(...)
	.ObserveOn(RxApp.MainThreadScheduler)
	.Subscribe(...)
	.DisposeWith(Disposables);
```

**DO NOT** dispose your subscription if a component exposes an event and also subscribes to it itself. That's because the subscription is manifested as the component having a reference to itself. Same is true with Rx. If you're a VM and you e.g. WhenAnyValue against your own property, there's no need to clean that up because that is manifested as the VM having a reference to itself.

```cs
this.WhenAnyValue(...)
	.ObserveOn(RxApp.MainThreadScheduler)
	.Subscribe(...);
```

## Subscribe triggered once on initialization

When you subscribe with the usage of `.WhenAnyValue()` right after the creation one call of Subcription will be triggered. This is by design and most of the cases it is fine. Still you can supress this behaviour by adding `Skip(1)`.

```cs
this.WhenAnyValue(x => x.PreferPsbtWorkflow)
	.Skip(1)
	.Subscribe(value =>
	{
		// Expensive operation, that should not run unnecessary.
	});
```

- [Example](https://stackoverflow.com/questions/36705139/why-does-whenanyvalue-observable-trigger-on-subscription)

## ObservableAsPropertyHelpers Over Properties

**DO** follow [ReactiveUI's Oaph Over Properties Principle](https://reactiveui.net/docs/guidelines/framework/prefer-oaph-over-properties).

**DO** use  `ObservableAsPropertyHelper` with `WhenAny` when a property's value depends on another property, a set of properties, or an observable stream, rather than set the value explicitly.

```cs
public class RepositoryViewModel : ReactiveObject
{
  private ObservableAsPropertyHelper<bool> _canDoIt;

  public RepositoryViewModel()
  {
    _canDoIt = this.WhenAnyValue(...)
		.ToProperty(this, x => x.CanDoIt, scheduler: RxApp.MainThreadScheduler);
  }

  public bool CanDoIt => _canDoIt?.Value ?? false;
}
```

**DO** always subscribe to these `ObservableAsPropertyHelper`s after their initialization is done.

## No code in Code-behind files (.xaml.cs)

All the logic should go into `ViewModels` or `Behaviors`.

## Main MUST be Synchronous

For Avalonia applications the Main method must be synchronous. No async-await here! If you await inside Main before Avalonia has initialised its renderloop / UIThread, then OSX will stop working. Why? Because OSX applications (and some of Unixes) assume that the very first thread created in a program is the UIThread. Cocoa apis check this and crash if they are not called from Thread 0. Awaiting means that Avalonia may not be able to capture Thread 0 for the UIThread.

## Avoid Binding expressions with SubProperties
If you have a `Binding` expression i.e. `{Binding MyProperty.ChildProperty}` then most likely the UI design is flawed and you have broken the MVVM pattern.

This kind of Binding demonstrates that your View is dependent not on just 1 ViewModel, but multiple Viewmodels and a very specific relationship between them.

If you find yourself having to do this, please re-think the UI design. To follow the MVVM pattern correctly to ensure the UI remains maintainable and testable then we should have a 1-1 view, viewmodel relationship. That means every view should only depend on a single viewmodel.

## Familiarise yourself with MVVM Pattern
It is very important for us to follow the MVVM pattern in UI code. Whenever difficulties arise in refactoring the UI or adding new UI features its usually where we have ventured from this path.

Some pointers on how to recognise if we are breaking MVVM:

* Putting code in .xaml.cs (code-behind files)
* Putting business logic inside control code
* Views that depend on more than 1 viewmodel class.

If it seems not possible to implement something without breaking some of this advice please consult with @danwalmsley.

## Avoid using Grid as much as possible, Use Panel instead
If you don't need any row or column splitting for your child controls, just use `Panel` as your default container control instead of `Grid` since it is a moderately memory and CPU intensive control.

## ViewModel Hierarchy

The ViewModel structure should reflect the UI structure as much as possible. This means that ViewModels can have *child* ViewModels directly referenced in their code, just like Views have direct reference to *child* views.

❌ **DO NOT** write ViewModel code that depends on *parent* or *sibling* ViewModels in the logical UI structure. This harms both testability and maintainability.

Examples:

 - ✔️ `MainViewModel` represents the Main Wasabi UI and references `NavBarViewModel`.
 - ✔️ `NavBarViewModel` represents the left-side navigation bar and references `WalletListViewModel`.
 - ❌ `NavBarViewModel` code must NOT reference `MainViewModel` (its logical parent).
 - ❌ `WalletListViewModel` code must NOT reference `NavBarViewModel` (its logical parent).
 - ❌ `WalletListViewModel` code must NOT reference other ViewModels that are logical children of `NavBarViewModel` (its logical siblings).

## UI Models

The UI Model classes (which comprise the *Model* part of the MVVM pattern) sit as an abstraction layer between the UI and the larger Wasabi Object Model (which lives in the `WalletWasabi` project). This layer is responsible for:

 - Exposing Wasabi data and functionality in a UI-friendly manner. Usually in the form of Observables.

 - Avoiding tight coupling between UI code and business logic. This is critical for testability of UI code, mainly ViewModels.

❌ **DO NOT** write ViewModel code that depends directly on `WalletWasabi` objects such as `Wallet`, `KeyManager`, `HdPubKey`, etc.

✔️ **DO** write ViewModel code that depends on `IWalletModel`, `IWalletRepository`, `IAddress`, etc.

❌ **DO NOT** convert regular .NET properties from `WalletWasabi` objects into observables or INPC properties in ViewModel code.

❌ **DO NOT** convert regular .NET events from `WalletWasabi` objects into observables in ViewModel code.

✔️ If such conversions are required, **DO** write them into the UI Model layer.

## UiContext

ViewModels that depend on external components (such as Navigation, Clipboard, QR Reader, etc) can access these via the `ViewModelBase.UIContext` property. For instance:

 - Get text from clipboard: `var text = await UIContext.Clipboard.GetTextAsync();`

 - Generate QR Code: `await UIContext.QrGenerator.Generate(data);`

 - Open a popup or navigate to another Viewmodel: `UIContext.Navigate().To(....)`

This is done to facilitate unit testing of viewmodels, since all dependencies that live inside the `UiContext` are designed to be mock-friendly.

❌ **DO NOT** write Viewmodel code that directly depends on external device-specific components or code that might otherwise not work in the context of a unit test.

## Source-Generated ViewModel Constructors

Whenever a ViewModel references its `UiContext` property, the `UiContext` object becomes an actual **dependency** of said ViewModel. It must therefore be initialized, ideally as a constructor parameter.

In order to minimize the amount of boilerplate required for such initialization, several things occur in this case:
 - A new constructor is generated for that ViewModel, including all parameters of any existing constructor plus the UiContext.
 - This generated constructor initializes the `UiContext` *after* running the code of the manually written constructor (if any).
 - A Roslyn Analyzer inspects any manually written constructors in the ViewModel to prevent references to `UiContext` in the constructor body, before the above mentioned initialization can take place, resulting in `NullReferenceException`s.
 - The Analyzer demands the manually written constructor to be declared `private`, so that external instatiation of the ViewModel is done by calling the source-generated constructor.

❌ Writing code that directly references `UiContext` in a ViewModel's constructor body will result in a compile-time error.

❌ Writing code that indirectly references `UiContext` in ViewModel's constructor body will result in a run-time `NullReferenceException`.

✔️ Writing code that directly or indirectly references `UiContext` inside a lambda expression in a ViewModel's constructor body is okay, since this code is deferred to a later time at run-time when the `UiContext` property has already been properly initialized.

Example:

```csharp
    // ❌ BAD, constructor should be private
    public AddressViewModel(IAddress address)
	{
		if (condition)
		{
			//❌ BAD, UiContext is null at this point.
			UiContext.Navigate().To(someOtherViewModel);
		}
	}

    // ✔️ GOOD, constructor is private
    private AddressViewModel(IAddress address)
	{
		//✔️ GOOD, UiContext is already initialized when the Command runs
		NextCommand = ReactiveCommand.Create(() => UiContext.Navigate().To(someOtherViewModel)));
	}
```

If you absolutely must reference `UiContext` in the constructor, you can create a public constructor explicitly taking `UiContext` as a parameter:

```csharp
    // ✔️ GOOD,
    public AddressViewModel(UiContext uiContext, IAddress address)
	{
		UiContext = uiContext;

		// ✔️Other code here can safely use the UiContext since it's explicitly initialized above.
	}
```

In this case, no additional constructors will be generated, and the analyzer will be satisfied.

# Pull Requests

### Committing Patches

In general, [commits should be atomic](https://en.wikipedia.org/wiki/Atomic_commit#Atomic_commit_convention)
and diffs should be easy to read. For this reason, do not mix any formatting
fixes or code moves with actual code changes.

Make sure each individual commit is hygienic: that it builds successfully on its
own without warnings, errors, regressions, or test failures.

Commit messages should be verbose by default consisting of a short subject line
(50 chars max), a blank line and detailed explanatory text as separate
paragraph(s), unless the title alone is self-explanatory (like "Correct typo
in readme.md") in which case a single title line is sufficient. Commit messages should be
helpful to people reading your code in the future, so explain the reasoning for
your decisions. Further explanation [here](https://chris.beams.io/posts/git-commit/).

If a particular commit references another issue, please add the reference. For
example: `refs #1234` or `fixes #4321`. Using the `fixes` or `closes` keywords
will cause the corresponding issue to be closed when the pull request is merged.

Commit messages should never contain any `@` mentions (usernames prefixed with "@").

Please refer to the [Git manual](https://git-scm.com/doc) for more information
about Git.

  - Push changes to your fork
  - Create pull request

### Features

When adding a new feature, thought must be given to the long term technical debt
and maintenance that feature may require after inclusion. Before proposing a new
feature that will require maintenance, please consider if you are willing to
maintain it (including bug fixing). If features get orphaned with no maintainer
in the future, they may be removed by the Repository Maintainer.

### Refactoring

Refactoring is a necessary part of any software project's evolution. The
following guidelines cover refactoring pull requests for the project.

There are three categories of refactoring: code-only moves, code style fixes, and
code refactoring. In general, refactoring pull requests should not mix these
three kinds of activities in order to make refactoring pull requests easy to
review and uncontroversial. In all cases, refactoring PRs must not change the
behaviour of code within the pull request (bugs must be preserved as is).

Project maintainers aim for a quick turnaround on refactoring pull requests, so
where possible keep them short, uncomplex and easy to verify.

Pull requests that refactor the code should not be made by new contributors. It
requires a certain level of experience to know where the code belongs to and to
understand the full ramification (including rebase effort of open pull requests).

Trivial pull requests or pull requests that refactor the code with no clear
benefits may be immediately closed by the maintainers to reduce unnecessary
workload on reviewing.

### Squashing Commits

If your pull request contains fixup commits (commits that change the same line of code repeatedly) or too fine-grained
commits, you may be asked to [squash](https://git-scm.com/docs/git-rebase#_interactive_mode) your commits
before it will be reviewed.

Please refrain from creating several pull requests for the same change.
Use the pull request that is already open (or was created earlier) to amend
changes. This preserves the discussion and review that happened earlier for
the respective change set.

The length of time required for peer review is unpredictable and will vary from
pull request to pull request.

### Merging Pull Requests

There are different ways to merge commits on GitHub. By default, the "Create merge commit" should be used. If there are several commits addressing the same change, the author can be asked to squash commits. For example:

- Fix code format
- Fix code format again
- More code format fix

Avoid squashing excessively. The objective is not to achieve a brief commit history but rather a sequential one, where each commit encapsulates a single logical change in the code, as detailed by its commit message.

