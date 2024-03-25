namespace WalletWasabi.Fluent.Infrastructure;

/// <summary>
/// Indicates that the decorated application instances do not need to implement IDisposable 
/// because they have application lifetime.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AppLifetimeAttribute : Attribute;
