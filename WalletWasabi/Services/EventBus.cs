using System.Linq;
using System.Collections.Generic;

namespace WalletWasabi.Services;

public class EventBus
{
	private readonly Dictionary<Type, List<Action<object>>> _subscriptions;
	private readonly Dictionary<object, Type> _typeMap;
	private readonly object _syncObj = new();

	public EventBus()
	{
		_subscriptions = new Dictionary<Type, List<Action<object>>>();
		_typeMap = new Dictionary<object, Type>();
	}

	public void Subscribe<TEvent>(Action<TEvent> action) where TEvent : notnull
	{
		lock (_syncObj)
		{
			if (!_subscriptions.ContainsKey(typeof(TEvent)))
			{
				_subscriptions.Add(typeof(TEvent), []);
			}

			_typeMap.Add(action, typeof(TEvent));
			_subscriptions[typeof(TEvent)].Add(o => action((TEvent)o));
		}
	}

	public void Unsubscribe<TEvent>(Action<TEvent> action) where TEvent : notnull
	{
		lock (_syncObj)
		{
			Type type = _typeMap[action];
			if (_subscriptions.TryGetValue(type, out var allSubscriptions))
			{
				var subscriptionToRemove = allSubscriptions.FirstOrDefault(x => true /*x == action*/);
				if (subscriptionToRemove != null)
				{
					allSubscriptions.Remove(subscriptionToRemove);
				}
			}
		}
	}

	public void Publish<TEvent>(TEvent eventItem) where TEvent : notnull
	{
		List<Action<object>>? allSubscriptions;
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
}
