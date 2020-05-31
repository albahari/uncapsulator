using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Uncapsulator
{
	/// <summary>
	/// Casting to this interface provides another way to access the underlying object and type of an uncapsulated object.
	/// </summary>
	public interface IUncapsulated
	{
		/// <summary>The underlying value</summary>
		object Value { get; }

		/// <summary>The static type</summary>
		Type Type { get; }
	}

	/// <summary>
	/// Extension methods for Uncapsulator
	/// </summary>
	public static class Extensions
	{
		/// <summary>
		/// Returns a dynamic proxy that lets you access private members of the object.
		/// </summary>
		public static dynamic Uncapsulate (this object instance) => new Uncapsulator (null, instance);
	}

	/// <summary>
	/// Uncapsulator - by Joseph Albahari.
	/// </summary>
	partial class Uncapsulator : GreedyDynamicObject, IUncapsulated
	{
		readonly string _path;  // This helps to diagnose the source when throwing NullReferenceExceptions
		public object Value { get; private set; }    // This is null when we're uncapsulating a type rather than an instance.
		readonly Type _type;
		
		const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

		BindingFlags DefaultBindingFlags => Value == null ? StaticFlags : InstanceFlags;

		protected internal override Type WrappedType => _type;
		Type IUncapsulated.Type => _type;

		bool WrapsType => _type != null && Value == null;
		bool WrapsInstance => !WrapsType;
		bool WrapsNullInstance => _type == null;

		void ThrowIfNull (string action)
		{
			if (WrapsNullInstance)
				throw new NullReferenceException ($"You attempted to {action} on a null object, which was returned from '{_path}'.");
		}

		void ThrowIfStatic (string action)
		{
			if (WrapsType)
				throw new NullReferenceException ($"You attempted to {action} on a type; this operation is valid only for instances.");
		}

		internal Uncapsulator (string path, object value) : this (path, value, value?.GetType ()) { }
		internal Uncapsulator (string path, object value, Type type) => (_path, Value, _type) = (path, value, type);

		public override bool TryInvokeMember (InvokeMemberBinder binder, object[] args, out object result, ParameterModifier paramModifier)
		{
			UnwrapArgs (args);

			bool bypass = false;
			ShouldBypassMethod (binder.Name, args, ref bypass);
			if (bypass)
			{
				result = null;
				return false;
			}

			if (binder.CallInfo.ArgumentNames.Any ())
				throw new MethodAccessException ("Named arguments are not supported with dynamic method calls.");

			Type[] typeArgs = binder.Uncapsulate ().TypeArguments;

			if (binder.Name == "ToObject" && args.Length == 0 && typeArgs.Length == 0)
			{
				result = Value;
				return true;
			}

			if (binder.Name == "GetType" && args.Length == 0 && typeArgs.Length == 0)
			{
				result = new Uncapsulator (_path + ".GetType()", _type);
				return true;
			}

			ThrowIfNull ($"call method '{binder.Name}'");

			if (TryCastTo (binder, args, typeArgs, out result) ||
				TryToDynamicSequence (binder, args, typeArgs, out result))
			{
				return true;
			}

			// If we're unable to find a matching member, we must return false rather than throwing, because the next thing
			// to happen is that the TryGetMember method will get invoked (which could match to a field or property that
			// returns a delegate that could subsequently be invoked).

			if (!InvokeCore (binder.Name, args, typeArgs, paramModifier, out result))
				return false;

			result = new Uncapsulator ($"{_path}.{_type}.{binder.Name}(" + string.Join (",", Enumerable.Repeat ("...", args.Length)) + ")", result);
			return true;
		}

		bool TryCastTo (InvokeMemberBinder binder, object[] args, Type[] typeArgs, out object result)
		{
			result = null;
			if (binder.Name != "CastTo" || WrapsType) return false;

			if (typeArgs.Length == 0 && args.Length == 1 && args[0] is string typeName)    // CastTo(string typeName)
			{
				string newPath = _path + $".CastTo({typeName})";

				var newType =
					Value.GetType ().GetInterface (typeName) ??      // Try first to find an interface
					GetTypeHierarchy (Value?.GetType () ?? _type)
						.Select (t => t.IsConstructedGenericType ? t.GetGenericTypeDefinition () : t)
						.FirstOrDefault (t => t.Name == typeName || t.FullName == typeName);

				if (newType == null)
					throw new TypeLoadException ($"Error calling CastTo: '{typeName}' is not a base clas or interface of '{Value.GetType ()}'. Specify generic types with backticks, e.g., 'List`1' or 'IList`1'. A namespace is not required.");

				result = new Uncapsulator (newPath, Value, newType);
				return true;
			}

			if (typeArgs.Length == 1 && args.Length == 0 ||                         // CastTo<T>()
				typeArgs.Length == 0 && args.Length == 1 && args[0] is Type)        // CastTo(Type typeName)   
			{
				Type newType = typeArgs.Length == 1 ? typeArgs[0] : (Type)args[0];

				if (!newType.IsAssignableFrom (Value.GetType ()))
					throw new InvalidCastException ($"Cannot cast from type {Value.GetType ()} to {newType}.");

				result = new Uncapsulator (_path + $".CastTo({newType.Name})", Value, newType);
				return true;
			}

			return false;
		}

		bool TryToDynamicSequence (InvokeMemberBinder binder, object[] args, Type[] typeArgs, out object result)
		{
			result = null;
			if (binder.Name != "ToDynamicSequence" || WrapsType || args.Length != 0 && typeArgs.Length != 0) return false;

			if (Value is IEnumerable ie)
				result = Iterate ();
			else
				throw new InvalidCastException ($"Unable to call ToDynamicSequence() because type '{_type}' does not implement IEnumerable.");

			return true;

			IEnumerable<dynamic> Iterate ()
			{
				// Each item in the sequence needs to be uncapsulated.
				foreach (var item in ie) yield return item?.Uncapsulate ();
			}
		}

		bool InvokeCore (string memberName, object[] args, Type[] typeArgs, ParameterModifier parameterModifiers, out object result)
		{
			if (memberName == "new")
			{
				result = Activator.CreateInstance (_type, InstanceFlags, null, args, null);
				return true;
			}

			var originalArgs = args;
			var matchingMember = GetTypeHierarchy (_type).Select (SelectMethod).FirstOrDefault (x => x != null);
			if (matchingMember == null)
			{
				// If there's a field or property with that name, allow it to bind to that. It's possible that the field or
				// property could return a delegate type that can be invoked.
				if (GetFieldOrProperty (memberName, false) != null)
				{
					result = null;
					return false;
				}
				if (GetTypeHierarchy (_type).SelectMany (t => t.GetMember (memberName, MemberTypes.Method, DefaultBindingFlags)).Any ())
					throw new MissingMethodException ($"Unable to find a compatible overload for '{_type}.{memberName}'.");
				else
					throw new MissingMemberException ($"{_type}' does not contain a method called {memberName}.");
			}

			result = matchingMember.Invoke (Value, args);

			// If the method has optional parameters which were applied, SelectMethod's call to BindToMethod will replace the
			// args array with a bigger one. In case some arguments were passed by reference, we need to copy the elements back.
			if (args != originalArgs)  
				for (int i = 0; i < originalArgs.Length; i++)
					originalArgs[i] = args[i];

			return true;

			MethodBase SelectMethod (Type type)
			{
				Type[] argTypes = args.Select (a => a?.GetType ()).ToArray ();
				MethodBase[] methods;

				// Find all the compatible methods on this type, i.e., the methods whose
				// (1) parameter count >= the argument count (the parameter count can be greater because we support optional parameters)
				// (2) type argument count matches the number of type args passed in by the caller
				// (3) parameter types are pass-by-reference if specified by the caller
				methods = type
					.GetMember (memberName, MemberTypes.Method, DefaultBindingFlags)
					.OfType<MethodInfo> ()
					.Where (m => m.GetParameters ().Length >= args.Length && m.GetGenericArguments ().Length == typeArgs.Length)
					.Where (m => IsByRefCompatible (m.GetParameters ()))
					.Select (m => m.IsGenericMethod ? m.MakeGenericMethod (typeArgs) : m)
					.ToArray ();

				if (methods.Length == 0) return null;

				// Use DefaultBinder to pick the correct overload. Note that it might give us back a different args array if
				// it needed to apply optional paarmeters.
				return Type.DefaultBinder.BindToMethod (DefaultBindingFlags, methods, ref args, null, null, null, out object state);
			}

			bool IsByRefCompatible (ParameterInfo[] parameters)
			{
				for (int i = 0; i < args.Length; i++)
					if (parameters[i].ParameterType.IsByRef != parameterModifiers[i])
						return false;

				// Any remaining parameters will be optional. Make sure that they're not pass-by-ref.
				for (int i = args.Length; i < parameters.Length; i++)
					if (parameters[i].ParameterType.IsByRef)
						return false;

				return true;
			}
		}

		MemberInfo GetFieldOrProperty (string name, bool throwIfNotFound)
		{
			var result = GetTypeHierarchy (_type).Select (t => GetFieldOrProperty (t, name)).FirstOrDefault (x => x != null);

			// If we can't match, it's better to throw than return null, so that we can report _type in the error message.
			if (result == null && throwIfNotFound) throw new MissingMemberException ($"'{_type}' does not contain a definition for '{name}'.");

			return result;
		}

		MemberInfo GetFieldOrProperty (Type type, string name) =>
			(MemberInfo)type.GetProperty (name, DefaultBindingFlags) ?? type.GetField (name, DefaultBindingFlags);

		public override bool TryGetMember (GetMemberBinder binder, out object result)
		{
			if (binder.Name == "base")
			{
				result = _type.BaseType == null ? this : new Uncapsulator (_path + ".@base", Value, _type.BaseType);
				return true;
			}

			bool bypass = false;
			ShouldBypassMethod (binder.Name, null, ref bypass);
			if (bypass)
			{
				result = null;
				return false;
			}

			ThrowIfNull ($"get member '{binder.Name}'");

			var member = GetFieldOrProperty (binder.Name, true);

			var returnValue = member is FieldInfo fi ? fi.GetValue (Value) : ((PropertyInfo)member).GetValue (Value);

			result = new Uncapsulator (_path + "." + binder.Name, returnValue);
			return true;
		}

		public override bool TrySetMember (SetMemberBinder binder, object value)
		{
			ThrowIfNull ($"set member '{binder.Name}'");
			if (value is Uncapsulator uc) value = uc.Value;
			var member = GetFieldOrProperty (binder.Name, true);

			if (member is FieldInfo fi)
				fi.SetValue (Value, value);
			else
				((PropertyInfo)member).SetValue (Value, value);

			return true;
		}

		public override bool TryGetIndex (GetIndexBinder binder, object[] indexes, out object result)
		{
			ThrowIfNull ($"invoke an indexer");
			ThrowIfStatic ($"invoke an indexer");
			UnwrapArgs (indexes);
			string newParent = _path + "[" + string.Join (",", indexes) + "]";
			try
			{
				if (_type.IsArray && indexes.All (x => x is int))
					result = new Uncapsulator (newParent, ((Array)Value).GetValue (indexes.Select (x => (int)x).ToArray ()));
				else
					result = new Uncapsulator (newParent, SelectIndexer (indexes).GetValue (Value, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, indexes, null));
			}
			catch (Exception ex)
			{
				throw new MemberAccessException ($"Unable to invoke get-indexer on type '{_type}' - {ex.Message}", ex);
			}
			return true;
		}

		public override bool TrySetIndex (SetIndexBinder binder, object[] indexes, object value)
		{
			ThrowIfNull ($"invoke an indexer");
			ThrowIfStatic ($"invoke an indexer");
			UnwrapArgs (indexes);
			try
			{
				if (_type.IsArray && indexes.All (x => x is int))
					((Array)Value).SetValue (value, indexes.Select (x => (int)x).ToArray ());
				else
					SelectIndexer (indexes, value?.GetType ()).SetValue (Value, value, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, indexes, null);
			}
			catch (Exception ex)
			{
				throw new MemberAccessException ($"Unable to invoke set-indexer on type '{_type}' - {ex.Message}", ex);
			}
			return true;
		}

		PropertyInfo SelectIndexer (object[] indexValues, Type returnType = null)
		{
			var props = GetDefaultMembers (_type)
				.OfType<PropertyInfo> ()
				.Where (p => p.GetIndexParameters ().Length == indexValues.Length)
				.ToArray ();

			if (props.Length == 0) throw new MissingMemberException ($"There are no indexers on type {_type}.");

			if (indexValues.Any (x => x == null))
			{
				var eligibleProps = props.Where (i => i.GetIndexParameters ().All (p => !p.ParameterType.IsValueType)).ToArray ();
				if (eligibleProps.Length == 1) return eligibleProps[0];
				if (eligibleProps.Length > 1)
					throw new AmbiguousMatchException ($"Call to indexer on '{_path}' is ambiguous because one or more arguments is null.");
				else
					throw new MissingMemberException ($"Cannot find a compatible indexer on type '{_type}'.");
			}

			var result = Type.DefaultBinder.SelectProperty (DefaultBindingFlags, props, returnType, indexValues.Select (i => i.GetType ()).ToArray (), null);
			if (result == null) throw new MissingMemberException ($"Cannot find a compatible indexer on type '{_type}'.");
			return result;
		}

		static Func<Type, MemberInfo[]> _getDefaultMembers;

		static MemberInfo[] GetDefaultMembers (Type type)
		{
			ValidateDefaultMemberCache ();

			if (_getDefaultMembers == null)
				_getDefaultMembers = Memoizer.Memoize<Type, MemberInfo[]> (GetDefaultMembersCore);

			return _getDefaultMembers (type);
		}

		static MemberInfo[] GetDefaultMembersCore (Type type)
		{
			Type typeFromHandle = typeof (DefaultMemberAttribute);
			while (type != null)
			{
				var customAttributeData = CustomAttributeData
					.GetCustomAttributes (type)
					.FirstOrDefault (c => (object)c.Constructor.DeclaringType == typeFromHandle);

				if (customAttributeData != null)
				{
					string name = customAttributeData.ConstructorArguments[0].Value as string;
					if (name == null) return new MemberInfo[0];
					return type.GetMember (name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				}
				type = type.BaseType;
			}
			return new MemberInfo[0];
		}

		public override bool TryConvert (ConvertBinder binder, out object result)
		{
			if (binder.Type == typeof (IUncapsulated))
			{
				result = this;
				return true;
			}

			if (Value == null)
			{
				result = null;
				return true;
			}

			var type = Value.GetType ();
			if (!binder.Type.IsAssignableFrom (type))
			{
				if (binder.Type.IsNumeric () && type.IsNumeric ())
				{
					result = Convert.ChangeType (Value, binder.Type);
					return true;
				}
				throw new InvalidCastException ($"Cannot dynamically convert from type {type} to {binder.Type}.");
			}

			result = Value;
			return true;
		}

		public override bool TryInvoke (InvokeBinder binder, object[] args, out object result)
		{
			ThrowIfNull ($"invoke");
			UnwrapArgs (args);

			if (!InvokeCore ("Invoke", args, new Type[0], new ParameterModifier (Math.Max (1, args.Length)), out result))
				throw new MissingMethodException ($"Unable to invoke '{_path}'.");

			result = new Uncapsulator (_path + "()", result);
			return true;
		}

		public override string ToString () => Value?.ToString ();

		static void UnwrapArgs (object[] args)
		{
			// We need to keep the array intact so pass-by-reference still works.
			for (int i = 0; i < args.Length; i++)
				if (args[i] is Uncapsulator dr)
					args[i] = dr.Value;
		}

		IEnumerable<Type> GetTypeHierarchy (Type type) => type.IsInterface
			? type.GetInterfaces ().Prepend (type)
			: Descend (type, (t => t.BaseType));

		static IEnumerable<T> Descend<T> (T item, Func<T, T> descendFunc) where T : class
		{
			while (item != null)
			{
				yield return item;
				item = descendFunc (item);
			}
		}

		// These method are to allow the same code to compile in LINQPad and support native Dump calls:
		partial void ShouldBypassMethod (string methodName, object[] argsIfKnown, ref bool result);
		static partial void ValidateDefaultMemberCache ();
	}
}
