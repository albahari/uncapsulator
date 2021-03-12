using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
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
    /// All binding-related exceptions that Uncapsulator throws are wrapped in this exception. This makes it easy
    /// to catch errors related to a missing or unexpected member.
    /// </summary>
    [Serializable]
    public class UncapsulatorException : Exception
    {
        public UncapsulatorException (string message, Exception inner = null) : base (message, inner) { }
        protected UncapsulatorException (SerializationInfo info, StreamingContext context) : base (info, context) { }
    }

    /// <summary>
    /// Extension methods for Uncapsulator
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Returns a dynamic proxy that lets you access private members of the object.
        /// </summary>
        public static dynamic Uncapsulate (this object instance, bool useGlobalCache = false)
            => new Uncapsulator (null, new Uncapsulator.UncapsulatorOptions (useGlobalCache, false), instance);

        /// <summary>
        /// Clears any type data that was cached statically by virtue of calling Uncapsulate(true)
        /// </summary>
        public static void ClearCache () => Uncapsulator.ClearStaticCache ();

        internal static UncapsulatorException Wrap (this Exception ex) => new UncapsulatorException (ex.Message, ex);
    }

    /// <summary>
    /// Uncapsulator - by Joseph Albahari.
    /// </summary>
    internal partial class Uncapsulator : GreedyDynamicObject, IUncapsulated
    {
        internal class UncapsulatorOptions
        {
            public readonly bool PublicOnly;
            public readonly bool UseGlobalCache;

            internal UncapsulatorOptions (bool useGlobalCache, bool publicOnly) => (UseGlobalCache, PublicOnly) = (useGlobalCache, publicOnly);

            internal static UncapsulatorOptions Default = new UncapsulatorOptions (false, false);
        }

        static Dictionary<object, (MethodBase method, bool fieldOrProp)> _globalBindToMethodCache = new Dictionary<object, (MethodBase method, bool fieldOrProp)> ();
        Dictionary<object, (MethodBase method, bool fieldOrProp)> _bindToMethodCache;

        static Dictionary<object, FieldOrProperty> _globalFieldAndPropertyCache = new Dictionary<object, FieldOrProperty> ();
        Dictionary<object, FieldOrProperty> _fieldAndPropertyCache;

        static Func<Type, MemberInfo[]> _globalDefaultMemberCache;
        MemberInfo[] _defaultMembers;

        static internal void ClearStaticCache ()
        {
            lock (_globalBindToMethodCache) _globalBindToMethodCache.Clear ();
            lock (_globalFieldAndPropertyCache) _globalFieldAndPropertyCache.Clear ();
            _globalDefaultMemberCache = null;
        }

        readonly string _path;  // This helps to diagnose the source when throwing NullReferenceExceptions
        public object Value { get; private set; }    // This is null when we're uncapsulating a type rather than an instance.
        readonly Type _type;
        readonly UncapsulatorOptions _options;

        readonly BindingFlags _instanceFlags = BindingFlags.Instance | BindingFlags.Public;
        readonly BindingFlags _staticFlags = BindingFlags.Static | BindingFlags.Public;
        readonly BindingFlags _defaultBindingFlags;

        protected internal override Type WrappedType => _type;
        Type IUncapsulated.Type => _type;

        bool WrapsType => _type != null && Value == null;
        bool WrapsInstance => !WrapsType;
        bool WrapsNullInstance => _type == null;

        void ThrowNullException (string action)
        {
            throw new NullReferenceException ($"You attempted to {action} on a null object, which was returned from '{_path}'.").Wrap ();
        }

        void ThrowTargetException (string action)
        {
            throw new TargetException ($"You attempted to {action} on a type; this operation is valid only for instances.").Wrap ();
        }

        internal Uncapsulator (string path, UncapsulatorOptions options, object value)
            : this (path, options, value, value?.GetType ()) { }

        internal Uncapsulator (string path, UncapsulatorOptions options, object value, Type type)
        {
            (_path, _options, Value, _type) = (path, options ?? UncapsulatorOptions.Default, value, type);

            if (!_options.PublicOnly)
            {
                _instanceFlags |= BindingFlags.NonPublic;
                _staticFlags |= BindingFlags.NonPublic;
            }
            _defaultBindingFlags = Value == null ? _staticFlags : _instanceFlags;
        }

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
                throw new MethodAccessException ("Named arguments are not supported with dynamic method calls.").Wrap ();

            Lazy<Type[]> typeArgs = new Lazy<Type[]> (() => GetTypeArguments (binder));

            if (binder.Name == "ToObject" && args.Length == 0 && typeArgs.Value.Length == 0)
            {
                result = Value;
                return true;
            }

            if (binder.Name == "GetType" && args.Length == 0 && typeArgs.Value.Length == 0)
            {
                result = new Uncapsulator (_path + ".GetType()", _options, _type);
                return true;
            }

            if (WrapsNullInstance) ThrowNullException ($"call method '{binder.Name}'");

            if (TryCastTo (binder, args, typeArgs, out result) ||
                TryToDynamicSequence (binder, args, typeArgs, out result))
            {
                return true;
            }

            // If we're unable to find a matching member, we must return false rather than throwing, because the next thing
            // to happen is that the TryGetMember method will get invoked (which could match to a field or property that
            // returns a delegate that could subsequently be invoked).

            if (!InvokeCore (binder.Name, args, typeArgs.Value, paramModifier, out result))
                return false;

            result = new Uncapsulator ($"{_path}.{_type}.{binder.Name}(...)", _options, result);
            return true;
        }

        static bool IsDotNetFramework => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.StartsWith (".NET Framework", StringComparison.OrdinalIgnoreCase);

        Type[] GetTypeArguments (InvokeMemberBinder binder)
        {
            var uncap = binder.Uncapsulate (useGlobalCache: true);

            // We are looking at the private members of Microsoft.CSharp.RuntimeBinder.CSharpInvokeMemberBinder, but the implementation differs
            // between .NET Framwork and .NetCore. The former uses List<Type> m_typeArguments whereas the latter uses Type[] TypeArg.

            if (IsDotNetFramework)
            {
                // It is important to cast uncap.m_typeArguments to List<Type> before calling ToArray(), otherwise we will be calling ToArray()
                // on a dynamic object which means it will call TryInvokeMember recursively and we will end up with a stack overflow.
                var list = (List<Type>)uncap.m_typeArguments;                
                return list.ToArray ();
            }
            else
                return (Type[]) uncap.TypeArguments;
        }

        bool TryCastTo (InvokeMemberBinder binder, object[] args, Lazy<Type[]> typeArgs, out object result)
        {
            result = null;
            if (binder.Name != "CastTo" || WrapsType) return false;

            if (typeArgs.Value.Length == 0 && args.Length == 1 && args[0] is string typeName)    // CastTo(string typeName)
            {
                string newPath = _path + $".CastTo({typeName})";

                var newType =
                    Value.GetType ().GetInterface (typeName) ??      // Try first to find an interface
                    GetTypeHierarchy (Value?.GetType () ?? _type)
                        .Select (t => t.IsConstructedGenericType ? t.GetGenericTypeDefinition () : t)
                        .FirstOrDefault (t => t.Name == typeName || t.FullName == typeName);

                if (newType == null)
                    throw new InvalidCastException ($"Error calling CastTo: '{typeName}' is not a base class or interface of '{Value.GetType ()}'. Specify generic types with backticks, e.g., 'List`1' or 'IList`1'. A namespace is not required.")
                        .Wrap ();

                result = new Uncapsulator (newPath, _options, Value, newType);
                return true;
            }

            if (typeArgs.Value.Length == 1 && args.Length == 0 ||                         // CastTo<T>()
                typeArgs.Value.Length == 0 && args.Length == 1 && args[0] is Type)        // CastTo(Type typeName)   
            {
                Type newType = typeArgs.Value.Length == 1 ? typeArgs.Value[0] : (Type)args[0];

                if (!newType.IsAssignableFrom (Value.GetType ()))
                    throw new InvalidCastException ($"Cannot cast from type {Value.GetType ()} to {newType}.").Wrap ();

                result = new Uncapsulator (_path + $".CastTo({newType.Name})", _options, Value, newType);
                return true;
            }

            return false;
        }

        bool TryToDynamicSequence (InvokeMemberBinder binder, object[] args, Lazy<Type[]> typeArgs, out object result)
        {
            result = null;
            if (binder.Name != "ToDynamicSequence" || WrapsType || args.Length != 0 && typeArgs.Value.Length != 0) return false;

            if (Value is IEnumerable ie)
                result = Iterate ();
            else
                throw new InvalidCastException ($"Unable to call ToDynamicSequence() because type '{_type}' does not implement IEnumerable.").Wrap ();

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
                result = Activator.CreateInstance (_type, _instanceFlags, null, args, null);
                return true;
            }

            var originalArgs = args;

            InvocationCacheKey cacheKey = _options.UseGlobalCache
                ? new GlobalInvocationCacheKey { Type = _type, BindingFlags = _defaultBindingFlags }
                : new InvocationCacheKey ();

            cacheKey.Name = memberName;
            cacheKey.TypeArgs = typeArgs;
            cacheKey.ParamTypes = new Type[args.Length];
            cacheKey.ParamModifiers = parameterModifiers;
            for (int i = 0; i < args.Length; i++) cacheKey.ParamTypes[i] = args[i]?.GetType ();

            var cache = _options.UseGlobalCache
                ? _globalBindToMethodCache
                : (_bindToMethodCache ?? (_bindToMethodCache = new Dictionary<object, (MethodBase method, bool fieldOrProp)> ()));

            if (!TryGetValueWithLock (cache, cacheKey, out var bindingResult))
            {
                bindingResult = BindToMethod (_type, memberName, _defaultBindingFlags, typeArgs, parameterModifiers, ref args);
                // We can't cache the result with optional parameters because the binding applies the defaults.
                if (originalArgs.Length == args.Length)
                    lock (cache)
                        cache[cacheKey] = bindingResult;
            }

            if (bindingResult.method == null)
            {
                if (bindingResult.fieldOrProp)
                {
                    result = null;
                    return false;
                }
                if (GetTypeHierarchy (_type).SelectMany (t => t.GetMember (memberName, MemberTypes.Method, _defaultBindingFlags)).Any ())
                    throw new MissingMethodException ($"Unable to find a compatible overload for '{_type}.{memberName}'.").Wrap ();
                else
                    throw new MissingMemberException ($"{_type}' does not contain a method called {memberName}.").Wrap ();
            }

            result = bindingResult.method.Invoke (Value, args);

            // If the method has optional parameters which were applied, SelectMethod's call to BindToMethod will replace the
            // args array with a bigger one. In case some arguments were passed by reference, we need to copy the elements back.
            if (args != originalArgs)
                for (int i = 0; i < originalArgs.Length; i++)
                    originalArgs[i] = args[i];

            return true;
        }

        (MethodBase method, bool boundToFieldOrProp) BindToMethod (Type type, string memberName, BindingFlags bindingFlags, Type[] typeArgs, ParameterModifier parameterModifiers, ref object[] args)
        {
            int argCount = args.Length;
            var args2 = args;

            var matchingMember = GetTypeHierarchy (type).Select (SelectMethod).FirstOrDefault (x => x != null);
            if (matchingMember != null)
            {
                args = args2;
                return (matchingMember, true);
            }

            // If there's a field or property with that name, allow it to bind to that. It's possible that the field or
            // property could return a delegate type that can be invoked.
            return (null, GetFieldOrProperty (type, memberName, bindingFlags, false).MemberInfo != null);

            MethodBase SelectMethod (Type t)
            {
                MethodBase[] methods;

                // Find all the compatible methods on this type, i.e., the methods whose
                // (1) parameter count >= the argument count (the parameter count can be greater because we support optional parameters)
                // (2) type argument count matches the number of type args passed in by the caller
                // (3) parameter types are pass-by-reference if specified by the caller
                methods = t
                    .GetMember (memberName, MemberTypes.Method, bindingFlags)
                    .OfType<MethodInfo> ()
                    .Where (m => m.GetParameters ().Length >= argCount && m.GetGenericArguments ().Length == typeArgs.Length)
                    .Where (m => IsByRefCompatible (m.GetParameters ()))
                    .Select (m => m.IsGenericMethod ? m.MakeGenericMethod (typeArgs) : m)
                    .ToArray ();

                if (methods.Length == 0) return null;

                // Use DefaultBinder to pick the correct overload. Note that it might give us back a different args array if
                // it needed to apply optional paarmeters.
                try
                {
                    return Type.DefaultBinder.BindToMethod (bindingFlags, methods, ref args2, null, null, null, out object state);
                }
                catch (MissingMethodException ex)
                {
                    throw ex.Wrap ();
                }
            }

            bool IsByRefCompatible (ParameterInfo[] parameters)
            {
                for (int i = 0; i < argCount; i++)
                    if (parameters[i].ParameterType.IsByRef != parameterModifiers[i])
                        return false;

                // Any remaining parameters will be optional. Make sure that they're not pass-by-ref.
                for (int i = argCount; i < parameters.Length; i++)
                    if (parameters[i].ParameterType.IsByRef)
                        return false;

                return true;
            }
        }

        FieldOrProperty GetFieldOrProperty (Type type, string name, BindingFlags bindingFlags, bool throwIfNotFound)
        {
            var cache = _options.UseGlobalCache
                ? _globalFieldAndPropertyCache
                : (_fieldAndPropertyCache ?? (_fieldAndPropertyCache = new Dictionary<object, FieldOrProperty> ()));

            object cacheKey = _options.UseGlobalCache
                ? (object)new GlobalFieldPropertyCacheKey { Type = type, Name = name, BindingFlags = bindingFlags }
                : name;

            if (!TryGetValueWithLock (cache, cacheKey, out var result))
            {
                result = new FieldOrProperty (
                    GetTypeHierarchy (type)
                        .Select (t => (MemberInfo)t.GetProperty (name, bindingFlags) ?? t.GetField (name, bindingFlags))
                        .FirstOrDefault (x => x != null));

                lock (cache) cache[cacheKey] = result;
            }

            // If we can't match, it's better to throw than return null, so that we can report _type in the error message.
            if (result.MemberInfo == null && throwIfNotFound)
                throw new MissingMemberException ($"'{type}' does not contain a definition for '{name}'.").Wrap ();

            return result;
        }

        public override bool TryGetMember (GetMemberBinder binder, out object result)
        {
            if (binder.Name == "base")
            {
                result = _type.BaseType == null ? this : new Uncapsulator (_path + ".@base", _options, Value, _type.BaseType);
                return true;
            }

            bool bypass = false;
            ShouldBypassMethod (binder.Name, null, ref bypass);
            if (bypass)
            {
                result = null;
                return false;
            }

            if (WrapsType)
            {
                var nestedType = _type.GetNestedType (binder.Name, BindingFlags.Public | BindingFlags.NonPublic);
                if (nestedType != null)
                {
                    result = new Uncapsulator (_path + "." + binder.Name, _options, null, nestedType);
                    return true;
                }
            }

            if (WrapsNullInstance) ThrowNullException ($"get member '{binder.Name}'");

            var member = GetFieldOrProperty (_type, binder.Name, _defaultBindingFlags, true);
            var returnValue = member.GetValue (Value);

            result = new Uncapsulator (_path + "." + binder.Name, _options, returnValue);
            return true;
        }

        public override bool TrySetMember (SetMemberBinder binder, object value)
        {
            if (WrapsNullInstance) ThrowNullException ($"set member '{binder.Name}'");
            if (value is Uncapsulator uc) value = uc.Value;
            var member = GetFieldOrProperty (_type, binder.Name, _defaultBindingFlags, true);
            member.SetValue (Value, value);

            return true;
        }

        public override bool TryGetIndex (GetIndexBinder binder, object[] indexes, out object result)
        {
            if (WrapsNullInstance) ThrowNullException ($"invoke an indexer");
            if (WrapsType) ThrowTargetException ($"invoke an indexer");
            UnwrapArgs (indexes);

            string newParent = _path + "[" + string.Join (",", indexes) + "]";
            try
            {
                if (_type.IsArray && indexes.All (x => x is int))
                    result = new Uncapsulator (newParent, _options, ((Array)Value).GetValue (indexes.Select (x => (int)x).ToArray ()));
                else
                    result = new Uncapsulator (newParent, _options, SelectIndexer (indexes).GetValue (Value, _instanceFlags, null, indexes, null));
            }
            catch (Exception ex)
            {
                throw new MemberAccessException ($"Unable to invoke get-indexer on type '{_type}' - {ex.Message}", ex).Wrap ();
            }
            return true;
        }

        public override bool TrySetIndex (SetIndexBinder binder, object[] indexes, object value)
        {
            if (WrapsNullInstance) ThrowNullException ($"invoke an indexer");
            if (WrapsType) ThrowTargetException ($"invoke an indexer");
            UnwrapArgs (indexes);

            try
            {
                if (_type.IsArray && indexes.All (x => x is int))
                    ((Array)Value).SetValue (value, indexes.Select (x => (int)x).ToArray ());
                else
                    SelectIndexer (indexes, value?.GetType ()).SetValue (Value, value, _instanceFlags, null, indexes, null);
            }
            catch (Exception ex)
            {
                throw new MemberAccessException ($"Unable to invoke set-indexer on type '{_type}' - {ex.Message}", ex).Wrap ();
            }
            return true;
        }

        PropertyInfo SelectIndexer (object[] indexValues, Type returnType = null)
        {
            var props = GetDefaultMembers ()
                .OfType<PropertyInfo> ()
                .Where (p => p.GetIndexParameters ().Length == indexValues.Length)
                .ToArray ();

            if (props.Length == 0) throw new MissingMemberException ($"There are no indexers on type {_type}.").Wrap ();

            if (indexValues.Any (x => x == null))
            {
                var eligibleProps = props.Where (i => i.GetIndexParameters ().All (p => !p.ParameterType.IsValueType)).ToArray ();
                if (eligibleProps.Length == 1) return eligibleProps[0];
                if (eligibleProps.Length > 1)
                    throw new AmbiguousMatchException ($"Call to indexer on '{_path}' is ambiguous because one or more arguments is null.").Wrap ();
                else
                    throw new MissingMemberException ($"Cannot find a compatible indexer on type '{_type}'.").Wrap ();
            }

            var result = Type.DefaultBinder.SelectProperty (_defaultBindingFlags, props, returnType, indexValues.Select (i => i.GetType ()).ToArray (), null);
            if (result == null) throw new MissingMemberException ($"Cannot find a compatible indexer on type '{_type}'.").Wrap ();
            return result;
        }

        MemberInfo[] GetDefaultMembers ()
        {
            if (_options.UseGlobalCache)
            {
                var cache = _globalDefaultMemberCache ?? (_globalDefaultMemberCache = Memoizer.Memoize<Type, MemberInfo[]> (GetDefaultMembersCore));
                return cache (_type);
            }
            return _defaultMembers ?? (_defaultMembers = GetDefaultMembersCore (_type));
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
                throw new InvalidCastException ($"Cannot dynamically convert from type {type} to {binder.Type}.").Wrap ();
            }

            result = Value;
            return true;
        }

        public override bool TryInvoke (InvokeBinder binder, object[] args, out object result)
        {
            if (WrapsNullInstance) ThrowNullException ($"invoke");
            UnwrapArgs (args);

            if (!InvokeCore ("Invoke", args, new Type[0], new ParameterModifier (Math.Max (1, args.Length)), out result))
                throw new MissingMethodException ($"Unable to invoke '{_path}'.").Wrap ();

            result = new Uncapsulator (_path + "()", _options, result);
            return true;
        }

        public override string ToString () => Value?.ToString ();

        IEnumerable<Type> GetTypeHierarchy (Type type) =>
            type.IsInterface ? type.GetInterfaces ().Prepend (type) :
            _options.PublicOnly ? new[] { type } :
            Descend (type, (t => t.BaseType));

        static void UnwrapArgs (object[] args)
        {
            // We need to keep the array intact so pass-by-reference still works.
            for (int i = 0; i < args.Length; i++)
                if (args[i] is Uncapsulator dr)
                    args[i] = dr.Value;
        }

        static IEnumerable<T> Descend<T> (T item, Func<T, T> descendFunc) where T : class
        {
            while (item != null)
            {
                yield return item;
                item = descendFunc (item);
            }
        }

        static bool TryGetValueWithLock<TKey, TValue> (IDictionary<TKey, TValue> dictionary, TKey key, out TValue value)
        {
            lock (dictionary)
                return dictionary.TryGetValue (key, out value);
        }

        class InvocationCacheKey : IEquatable<InvocationCacheKey>
        {
            public string Name;
            public Type[] TypeArgs, ParamTypes;
            public ParameterModifier ParamModifiers;

            public override bool Equals (object obj) => Equals (obj as InvocationCacheKey);

            public bool Equals (InvocationCacheKey other)
            {
                if (other == null) return false;
                if (Name != other.Name) return false;
                if (ParamTypes.Length != other.ParamTypes.Length) return false;
                for (int i = 0; i < ParamTypes.Length; i++)
                    if (ParamTypes[i] != other.ParamTypes[i])
                        return false;
                return true;
            }

            public override int GetHashCode () => Name.GetHashCode () + ParamTypes.Length.GetHashCode ();
        }

        class GlobalInvocationCacheKey : InvocationCacheKey, IEquatable<GlobalInvocationCacheKey>
        {
            public Type Type;
            public BindingFlags BindingFlags;

            public override bool Equals (object obj) => Equals (obj as GlobalInvocationCacheKey);

            public bool Equals (GlobalInvocationCacheKey other)
            {
                if (other == null) return false;
                if (Type != other.Type) return false;
                if (BindingFlags != other.BindingFlags) return false;
                return base.Equals (other);
            }

            public override int GetHashCode () => Type.GetHashCode () + 37 * base.GetHashCode ();
        }

        class GlobalFieldPropertyCacheKey : IEquatable<GlobalFieldPropertyCacheKey>
        {
            public Type Type;
            public BindingFlags BindingFlags;
            public string Name;

            public override bool Equals (object obj) => Equals (obj as GlobalFieldPropertyCacheKey);

            public bool Equals (GlobalFieldPropertyCacheKey other)
            {
                if (other == null) return false;
                if (Type != other.Type) return false;
                if (BindingFlags != other.BindingFlags) return false;
                return Name == other.Name;
            }

            public override int GetHashCode () => Type.GetHashCode () + 37 * Name.GetHashCode ();
        }

        class FieldOrProperty
        {
            public readonly MemberInfo MemberInfo;

            public FieldOrProperty (MemberInfo memberInfo) => MemberInfo = memberInfo;

            Func<object, object> _fastGetter;
            public object GetValue (object instance)
            {
                if (_fastGetter == null)
                {
                    if (MemberInfo is FieldInfo fi && fi.IsStatic)
                        return fi.GetValue (null);
                    else if (MemberInfo is PropertyInfo pi && (pi.GetMethod?.IsStatic ?? true))
                        return pi.GetValue (instance, null);
                    else
                        // Optimize the common case of getting a field or property.
                        _fastGetter = MemberInfo is FieldInfo fi2
                            ? TypeUtil.GenDynamicField (fi2)
                            : TypeUtil.GenDynamicProp ((PropertyInfo)MemberInfo);
                }
                return _fastGetter (instance);
            }

            public void SetValue (object instance, object value)
            {
                if (MemberInfo is FieldInfo fi)
                    fi.SetValue (instance, value);
                else
                    ((PropertyInfo)MemberInfo).SetValue (instance, value);
            }
        }

        // This method is to allow the same code to compile in LINQPad and support native Dump calls:
        partial void ShouldBypassMethod (string methodName, object[] argsIfKnown, ref bool result);
    }
}
