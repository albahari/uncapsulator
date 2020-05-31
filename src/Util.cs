using System;
using System.Collections.Generic;
using System.Text;

namespace Uncapsulator
{
	static class Util
	{
		public static T Try<T> (Func<T> func, Func<Exception, T> valueIfError)
		{
			try { return func (); }
			catch (Exception ex)
			{
				return valueIfError (ex);
			}
		}

		public static T Try<T> (Func<T> func, T valueIfError = default (T))
		{
			try { return func (); }
			catch
			{
				return valueIfError;
			}
		}

		public static Exception Try (Action action)
		{
			try { action (); }
			catch (Exception ex)
			{
				return ex;
			}
			return null;
		}

		public static bool IsNumeric (this Type t)
		{
			if (t == null) return false;

			if (t == typeof (decimal)) return true;
			if (!t.IsPrimitive) return false;
			return t != typeof (char) && t != typeof (bool);
		}
	}
}
