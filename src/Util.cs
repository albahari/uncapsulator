using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
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
	}

	static class TypeUtil
	{
		public static bool AreEquivalent (Type t1, Type t2)
		{
			if (t1 != null)
			{
				return t1.IsEquivalentTo (t2);
			}
			return false;
		}

		public static bool IsNumeric (this Type t)
		{
			if (t == null) return false;

			if (t == typeof (decimal)) return true;
			if (!t.IsPrimitive) return false;
			return t != typeof (char) && t != typeof (bool);
		}

		public static Func<object, object> GenDynamicField (FieldInfo fld)
		{
			DynamicMethod dynMeth;
			if (fld.DeclaringType.IsInterface)
				dynMeth = new DynamicMethod ("", typeof (object), new[] { typeof (object) });
			else
				dynMeth = new DynamicMethod ("", typeof (object), new[] { typeof (object) }, fld.DeclaringType);
			ILGenerator gen = dynMeth.GetILGenerator ();
			gen.Emit (OpCodes.Ldarg_0);

			if (fld.DeclaringType.IsValueType)
			{
				gen.DeclareLocal (fld.DeclaringType);
				gen.Emit (OpCodes.Unbox_Any, fld.DeclaringType);
				gen.Emit (OpCodes.Stloc_0);
				gen.Emit (OpCodes.Ldloca_S, 0);
			}
			else
				gen.Emit (OpCodes.Castclass, fld.DeclaringType);

			gen.Emit (OpCodes.Ldfld, fld);
			if (fld.FieldType.IsValueType) gen.Emit (OpCodes.Box, fld.FieldType);
			gen.Emit (OpCodes.Ret);
			return (Func<object, object>)dynMeth.CreateDelegate (typeof (Func<object, object>));
		}

		public static Func<object, object> GenDynamicProp (PropertyInfo prop)
		{
			//return x => prop.GetValue (x);
			DynamicMethod dynMeth;
			if (prop.DeclaringType.IsInterface)
				dynMeth = new DynamicMethod ("", typeof (object), new[] { typeof (object) });
			else
				dynMeth = new DynamicMethod ("", typeof (object), new[] { typeof (object) }, prop.DeclaringType);
			ILGenerator gen = dynMeth.GetILGenerator ();
			gen.Emit (OpCodes.Ldarg_0);

			if (prop.DeclaringType.IsValueType)
			{
				gen.DeclareLocal (prop.DeclaringType);
				gen.Emit (OpCodes.Unbox_Any, prop.DeclaringType);
				gen.Emit (OpCodes.Stloc_0);
				gen.Emit (OpCodes.Ldloca_S, 0);
				gen.Emit (OpCodes.Call, prop.GetGetMethod (true));
			}
			else
			{
				gen.Emit (OpCodes.Castclass, prop.DeclaringType);
				gen.Emit (OpCodes.Callvirt, prop.GetGetMethod (true));
			}
			if (prop.PropertyType.IsValueType) gen.Emit (OpCodes.Box, prop.PropertyType);
			gen.Emit (OpCodes.Ret);
			return (Func<object, object>)dynMeth.CreateDelegate (typeof (Func<object, object>));
		}
	}
}
