using System.Linq;
using System.Collections.Generic;

namespace WalletWasabi.Services;
using SubscriptionRegistry = Dictionary<Type, List<EventBus.Subscription>>;

public class EventBus
{
	private readonly SubscriptionRegistry _subscriptions;
	private readonly object _syncObj = new();

	public EventBus()
	{
		_subscriptions = new SubscriptionRegistry();
	}

	public IDisposable Subscribe<TEvent>(Action<TEvent> action) where TEvent : notnull
	{
		lock (_syncObj)
		{
			if (!_subscriptions.ContainsKey(typeof(TEvent)))
			{
				_subscriptions.Add(typeof(TEvent), []);
			}

			var subscription = Subscription.Create(action, this);
			_subscriptions[typeof(TEvent)].Add(subscription);
			return subscription;
		}
	}

	private void Unsubscribe(Subscription subscription)
	{
		lock (_syncObj)
		{
			Type type = subscription.Type;
			if (_subscriptions.TryGetValue(type, out var allSubscriptions))
			{
				var subscriptionToRemove = allSubscriptions.FirstOrDefault(x => x == subscription);
				if (subscriptionToRemove != null)
				{
					allSubscriptions.Remove(subscriptionToRemove);
				}
			}
		}
	}

	public void Publish<TEvent>(TEvent eventItem) where TEvent : notnull
	{
		List<Subscription>? allSubscriptions;
		lock (_syncObj)
		{
			if (!_subscriptions.TryGetValue(typeof(TEvent), out allSubscriptions))
			{
				return;
			}
		}

		foreach (var subscription in allSubscriptions)
		{
			subscription.Invoke(eventItem);
		}
	}

	internal class Subscription : IDisposable
	{
		public Type Type { get; }
		private readonly Action<object> _action;
		private readonly EventBus _eventBus;

		public static Subscription Create<TEvent>(Action<TEvent> action, EventBus eventBus) =>
			new(o => action((TEvent)o), typeof(TEvent), eventBus);

		private Subscription(Action<object> action, Type type, EventBus eventBus)
		{
			Type = type;
			_action = action;
			_eventBus = eventBus;
		}

		public void Invoke<TEvent>(TEvent param) where TEvent : notnull
		{
			_action(param);
		}

		public void Dispose()
		{
			_eventBus.Unsubscribe(this);
		}
	}
}
