using System;
using System.Collections.Generic;
using System.Text;

namespace Uncapsulator
{
	class Memoizer
	{
		public static Func<TKey, TValue> Memoize<TKey, TValue> (Func<TKey, TValue> getValueFunc)
		{
			var cache = new Dictionary<TKey, TValue> ();
			return (key =>
			{
				TValue result;
				lock (cache)
				{
					if (cache.TryGetValue (key, out result)) return result;
					return cache[key] = getValueFunc (key);
				}
			});
		}

		public static Func<TKey1, TKey2, TValue> Memoize<TKey1, TKey2, TValue> (Func<TKey1, TKey2, TValue> getValueFunc)
		{
			var memoizer = Memoize<(TKey1, TKey2), TValue> (key => getValueFunc (key.Item1, key.Item2));
			return (key1, key2) => memoizer ((key1, key2));
		}

		public static Func<TKey1, TKey2, TKey3, TValue> Memoize<TKey1, TKey2, TKey3, TValue> (Func<TKey1, TKey2, TKey3, TValue> getValueFunc)
		{
			var memoizer = Memoize<(TKey1, TKey2, TKey3), TValue> (key => getValueFunc (key.Item1, key.Item2, key.Item3));
			return (key1, key2, key3) => memoizer ((key1, key2, key3));
		}
	}
}
