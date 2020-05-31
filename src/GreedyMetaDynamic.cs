using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using static Uncapsulator.CachedReflectionInfo;

namespace Uncapsulator
{
	partial class GreedyDynamicObject
	{
		internal sealed class TrueReadOnlyCollection<T> : ReadOnlyCollection<T>
		{
			public TrueReadOnlyCollection (params T[] list)
				: base ((IList<T>)list)
			{
			}
		}

		private sealed class GreedyMetaDynamic : DynamicMetaObject
		{
			internal GreedyMetaDynamic (Expression expression, GreedyDynamicObject value)
				: base (expression, BindingRestrictions.Empty, value)
			{
			}

			public override IEnumerable<string> GetDynamicMemberNames () => Value.GetDynamicMemberNames ();

			public override DynamicMetaObject BindGetMember (GetMemberBinder binder)
			{
				if (IsOverridden (DynamicObject_TryGetMember))
				{
					return CallMethodWithResult (
						DynamicObject_TryGetMember,
						binder,
						s_noArgs,
						(GreedyMetaDynamic @this, GetMemberBinder b, DynamicMetaObject e) => b.FallbackGetMember (@this, e)
					);
				}

				return base.BindGetMember (binder);
			}

			public override DynamicMetaObject BindSetMember (SetMemberBinder binder, DynamicMetaObject value)
			{
				if (IsOverridden (DynamicObject_TrySetMember))
				{
					DynamicMetaObject localValue = value;

					return CallMethodReturnLast (
						DynamicObject_TrySetMember,
						binder,
						s_noArgs,
						value.Expression,
						(GreedyMetaDynamic @this, SetMemberBinder b, DynamicMetaObject e) => b.FallbackSetMember (@this, localValue, e)
					);
				}

				return base.BindSetMember (binder, value);
			}

			public override DynamicMetaObject BindDeleteMember (DeleteMemberBinder binder)
			{
				if (IsOverridden (DynamicObject_TryDeleteMember))
				{
					return CallMethodNoResult (
						DynamicObject_TryDeleteMember,
						binder,
						s_noArgs,
						(GreedyMetaDynamic @this, DeleteMemberBinder b, DynamicMetaObject e) => b.FallbackDeleteMember (@this, e)
					);
				}

				return base.BindDeleteMember (binder);
			}

			public override DynamicMetaObject BindConvert (ConvertBinder binder)
			{
				if (IsOverridden (DynamicObject_TryConvert))
				{
					return CallMethodWithResult (
						DynamicObject_TryConvert,
						binder,
						s_noArgs,
						(GreedyMetaDynamic @this, ConvertBinder b, DynamicMetaObject e) => b.FallbackConvert (@this, e)
					);
				}

				return base.BindConvert (binder);
			}

			public override DynamicMetaObject BindInvokeMember (InvokeMemberBinder binder, DynamicMetaObject[] args)
			{
				// Generate a tree like:
				//
				// {
				//   object result;
				//   TryInvokeMember(payload, out result)
				//      ? result
				//      : TryGetMember(payload, out result)
				//          ? FallbackInvoke(result)
				//          : fallbackResult
				// }
				//
				// Then it calls FallbackInvokeMember with this tree as the
				// "error", giving the language the option of using this
				// tree or doing .NET binding.
				//
				DynamicMetaObject call = BuildCallMethodWithResult (
					DynamicObject_TryInvokeMember,
					binder,
					GetExpressions (args),
					BuildCallMethodWithResult<GetMemberBinder> (
						DynamicObject_TryGetMember,
						new GetBinderAdapter (binder),
						s_noArgs,
						binder.FallbackInvokeMember (this, args, null),
						(GreedyMetaDynamic @this, GetMemberBinder ignored, DynamicMetaObject e) => binder.FallbackInvoke (e, args, null)
					),
					null
				);

				// JJA - we should handle these methods, too, otherwise we can't wrap the result in another dynamic object.
				if (binder.Name == "ToString" || binder.Name == "GetHashCode" || binder.Name == "GetType" || binder.Name == "Equals") return call;

				return binder.FallbackInvokeMember (this, args, call);
			}

			public override DynamicMetaObject BindCreateInstance (CreateInstanceBinder binder, DynamicMetaObject[] args)
			{
				if (IsOverridden (DynamicObject_TryCreateInstance))
				{
					DynamicMetaObject[] localArgs = args;

					return CallMethodWithResult (
						DynamicObject_TryCreateInstance,
						binder,
						GetExpressions (args),
						(GreedyMetaDynamic @this, CreateInstanceBinder b, DynamicMetaObject e) => b.FallbackCreateInstance (@this, localArgs, e)
					);
				}

				return base.BindCreateInstance (binder, args);
			}

			public override DynamicMetaObject BindInvoke (InvokeBinder binder, DynamicMetaObject[] args)
			{
				if (IsOverridden (DynamicObject_TryInvoke))
				{
					DynamicMetaObject[] localArgs = args;

					return CallMethodWithResult (
						DynamicObject_TryInvoke,
						binder,
						GetExpressions (args),
						(GreedyMetaDynamic @this, InvokeBinder b, DynamicMetaObject e) => b.FallbackInvoke (@this, localArgs, e)
					);
				}

				return base.BindInvoke (binder, args);
			}

			public override DynamicMetaObject BindBinaryOperation (BinaryOperationBinder binder, DynamicMetaObject arg)
			{
				if (IsOverridden (DynamicObject_TryBinaryOperation))
				{
					DynamicMetaObject localArg = arg;

					return CallMethodWithResult (
						DynamicObject_TryBinaryOperation,
						binder,
						new[] { arg.Expression },
						(GreedyMetaDynamic @this, BinaryOperationBinder b, DynamicMetaObject e) => b.FallbackBinaryOperation (@this, localArg, e)
					);
				}

				return base.BindBinaryOperation (binder, arg);
			}

			public override DynamicMetaObject BindUnaryOperation (UnaryOperationBinder binder)
			{
				if (IsOverridden (DynamicObject_TryUnaryOperation))
				{
					return CallMethodWithResult (
						DynamicObject_TryUnaryOperation,
						binder,
						s_noArgs,
						(GreedyMetaDynamic @this, UnaryOperationBinder b, DynamicMetaObject e) => b.FallbackUnaryOperation (@this, e)
					);
				}

				return base.BindUnaryOperation (binder);
			}

			public override DynamicMetaObject BindGetIndex (GetIndexBinder binder, DynamicMetaObject[] indexes)
			{
				if (IsOverridden (DynamicObject_TryGetIndex))
				{
					DynamicMetaObject[] localIndexes = indexes;

					return CallMethodWithResult (
						DynamicObject_TryGetIndex,
						binder,
						GetExpressions (indexes),
						(GreedyMetaDynamic @this, GetIndexBinder b, DynamicMetaObject e) => b.FallbackGetIndex (@this, localIndexes, e)
					);
				}

				return base.BindGetIndex (binder, indexes);
			}

			public override DynamicMetaObject BindSetIndex (SetIndexBinder binder, DynamicMetaObject[] indexes, DynamicMetaObject value)
			{
				if (IsOverridden (DynamicObject_TrySetIndex))
				{
					DynamicMetaObject[] localIndexes = indexes;
					DynamicMetaObject localValue = value;

					return CallMethodReturnLast (
						DynamicObject_TrySetIndex,
						binder,
						GetExpressions (indexes),
						value.Expression,
						(GreedyMetaDynamic @this, SetIndexBinder b, DynamicMetaObject e) => b.FallbackSetIndex (@this, localIndexes, localValue, e)
					);
				}

				return base.BindSetIndex (binder, indexes, value);
			}

			public override DynamicMetaObject BindDeleteIndex (DeleteIndexBinder binder, DynamicMetaObject[] indexes)
			{
				if (IsOverridden (DynamicObject_TryDeleteIndex))
				{
					DynamicMetaObject[] localIndexes = indexes;

					return CallMethodNoResult (
						DynamicObject_TryDeleteIndex,
						binder,
						GetExpressions (indexes),
						(GreedyMetaDynamic @this, DeleteIndexBinder b, DynamicMetaObject e) => b.FallbackDeleteIndex (@this, localIndexes, e)
					);
				}

				return base.BindDeleteIndex (binder, indexes);
			}

			private delegate DynamicMetaObject Fallback<TBinder> (GreedyMetaDynamic @this, TBinder binder, DynamicMetaObject errorSuggestion);

#pragma warning disable CA1825 // used in reference comparison, requires unique object identity
			private static readonly Expression[] s_noArgs = new Expression[0];
#pragma warning restore CA1825

			private static ReadOnlyCollection<Expression> GetConvertedArgs (params Expression[] args)
			{
				var paramArgs = new Expression[args.Length];

				for (int i = 0; i < args.Length; i++)
				{
					paramArgs[i] = Expression.Convert (args[i], typeof (object));
				}

				return new TrueReadOnlyCollection<Expression> (paramArgs);
			}

			/// <summary>
			/// Helper method for generating expressions that assign byRef call
			/// parameters back to their original variables.
			/// </summary>
			private static Expression ReferenceArgAssign (Expression callArgs, Expression[] args)
			{
				ReadOnlyCollectionBuilder<Expression> block = null;

				for (int i = 0; i < args.Length; i++)
				{
					ParameterExpression variable = args[i] as ParameterExpression;

					if (variable.IsByRef)
					{
						if (block == null)
							block = new ReadOnlyCollectionBuilder<Expression> ();

						block.Add (
							Expression.Assign (
								variable,
								Expression.Convert (
									Expression.ArrayIndex (
										callArgs,
										AstUtils.Constant (i)
									),
									variable.Type
								)
							)
						);
					}
				}

				if (block != null)
					return Expression.Block (block);
				else
					return AstUtils.Empty;
			}

			/// <summary>
			/// Helper method for generating arguments for calling methods
			/// on GreedyDynamicObject.  parameters is either a list of ParameterExpressions
			/// to be passed to the method as an object[], or NoArgs to signify that
			/// the target method takes no object[] parameter.
			/// </summary>
			private static Expression[] BuildCallArgs<TBinder> (TBinder binder, /*JJA*/ bool isInvokeMember, Expression[] parameters, Expression arg0, Expression arg1)
				where TBinder : DynamicMetaObjectBinder
			{
				// JJA
				if (isInvokeMember)
					return new Expression[] { Constant (binder), arg0, arg1, Expression.Constant (GetParameterModifiers (parameters)) };

				if (!object.ReferenceEquals (parameters, s_noArgs))
					return arg1 != null ? new Expression[] { Constant (binder), arg0, arg1 } : new Expression[] { Constant (binder), arg0 };
				else
					return arg1 != null ? new Expression[] { Constant (binder), arg1 } : new Expression[] { Constant (binder) };
			}

			private static ConstantExpression Constant<TBinder> (TBinder binder)
			{
				return Expression.Constant (binder, typeof (TBinder));
			}

			/// <summary>
			/// Helper method for generating a MetaObject which calls a
			/// specific method on Dynamic that returns a result
			/// </summary>
			private DynamicMetaObject CallMethodWithResult<TBinder> (MethodInfo method, TBinder binder, Expression[] args, Fallback<TBinder> fallback)
				where TBinder : DynamicMetaObjectBinder
			{
				return CallMethodWithResult (method, binder, args, fallback, null);
			}

			/// <summary>
			/// Helper method for generating a MetaObject which calls a
			/// specific method on Dynamic that returns a result
			/// </summary>
			private DynamicMetaObject CallMethodWithResult<TBinder> (MethodInfo method, TBinder binder, Expression[] args, Fallback<TBinder> fallback, Fallback<TBinder> fallbackInvoke)
				where TBinder : DynamicMetaObjectBinder
			{
				//
				// First, call fallback to do default binding
				// This produces either an error or a call to a .NET member
				//
				DynamicMetaObject fallbackResult = fallback (this, binder, null);

				DynamicMetaObject callDynamic = BuildCallMethodWithResult (method, binder, args, fallbackResult, fallbackInvoke);

				// JJA - to make TryConvert always work
				if (method.Name == "TryConvert")
					return callDynamic;

				//
				// Now, call fallback again using our new MO as the error
				// When we do this, one of two things can happen:
				//   1. Binding will succeed, and it will ignore our call to
				//      the dynamic method, OR
				//   2. Binding will fail, and it will use the MO we created
				//      above.
				//
				return fallback (this, binder, callDynamic);
			}

			/// <summary>
			/// Helper method for generating a MetaObject which calls a
			/// specific method on GreedyDynamicObject that returns a result.
			///
			/// args is either an array of arguments to be passed
			/// to the method as an object[] or NoArgs to signify that
			/// the target method takes no parameters.
			/// </summary>
			private DynamicMetaObject BuildCallMethodWithResult<TBinder> (MethodInfo method, TBinder binder, Expression[] args, DynamicMetaObject fallbackResult, Fallback<TBinder> fallbackInvoke)
				where TBinder : DynamicMetaObjectBinder
			{
				if (!IsOverridden (method))
				{
					return fallbackResult;
				}

				//
				// Build a new expression like:
				// {
				//   object result;
				//   TryGetMember(payload, out result) ? fallbackInvoke(result) : fallbackResult
				// }
				//
				ParameterExpression result = Expression.Parameter (typeof (object), null);
				ParameterExpression callArgs = method != DynamicObject_TryBinaryOperation ? Expression.Parameter (typeof (object[]), null) : Expression.Parameter (typeof (object), null);
				ReadOnlyCollection<Expression> callArgsValue = GetConvertedArgs (args);

				var resultMO = new DynamicMetaObject (result, BindingRestrictions.Empty);

				// Need to add a conversion if calling TryConvert
				if (binder.ReturnType != typeof (object))
				{
					Debug.Assert (binder is ConvertBinder && fallbackInvoke == null);

					UnaryExpression convert = Expression.Convert (resultMO.Expression, binder.ReturnType);
					// will always be a cast or unbox
					Debug.Assert (convert.Method == null);


					// Prepare a good exception message in case the convert will fail
					// JJA - we don't have access to the Strings type:
					//string convertFailed = System.Linq.Expressions.Strings.DynamicObjectResultNotAssignable (
					//	"{0}",
					//	this.Value.GetType(),
					//	binder.GetType(),
					//	binder.ReturnType
					//);				
					string convertFailed = $"Dynamic conversion failed: Could not convert type '{Value.WrappedType}' to '{binder.ReturnType}'.";

					Expression condition;
					// If the return type can not be assigned null then just check for type assignability otherwise allow null.
					if (binder.ReturnType.IsValueType && Nullable.GetUnderlyingType (binder.ReturnType) == null)
					{
						condition = Expression.TypeIs (resultMO.Expression, binder.ReturnType);
					}
					else
					{
						condition = Expression.OrElse (
										Expression.Equal (resultMO.Expression, AstUtils.Null),
										Expression.TypeIs (resultMO.Expression, binder.ReturnType));
					}

					Expression checkedConvert = Expression.Condition (
						condition,
						convert,
						Expression.Throw (
							Expression.New (
								InvalidCastException_Ctor_String,
								new TrueReadOnlyCollection<Expression> (
									Expression.Call (
										String_Format_String_ObjectArray,
										Expression.Constant (convertFailed),
										Expression.NewArrayInit (
											typeof (object),
											new TrueReadOnlyCollection<Expression> (
												Expression.Condition (
													Expression.Equal (resultMO.Expression, AstUtils.Null),
													Expression.Constant ("null"),
													Expression.Call (
														resultMO.Expression,
														Object_GetType
													),
													typeof (object)
												)
											)
										)
									)
								)
							),
							binder.ReturnType
						),
						binder.ReturnType
					);

					resultMO = new DynamicMetaObject (checkedConvert, resultMO.Restrictions);
				}

				if (fallbackInvoke != null)
				{
					resultMO = fallbackInvoke (this, binder, resultMO);
				}

				var callDynamic = new DynamicMetaObject (
					Expression.Block (
						new TrueReadOnlyCollection<ParameterExpression> (result, callArgs),
						new TrueReadOnlyCollection<Expression> (
							method != DynamicObject_TryBinaryOperation ? Expression.Assign (callArgs, Expression.NewArrayInit (typeof (object), callArgsValue)) : Expression.Assign (callArgs, callArgsValue[0]),
							Expression.Condition (
								Expression.Call (
									GetLimitedSelf (),
									method,
									BuildCallArgs (
										binder,
										method.Name == "TryInvokeMember", // JJA
										args,
										callArgs,
										result
									)
								),
								Expression.Block (
									method != DynamicObject_TryBinaryOperation ? ReferenceArgAssign (callArgs, args) : AstUtils.Empty,
									resultMO.Expression
								),
								fallbackResult.Expression,
								binder.ReturnType
							)
						)
					),
					GetRestrictions ().Merge (resultMO.Restrictions).Merge (fallbackResult.Restrictions)
				);
				return callDynamic;
			}

			/// <summary>
			/// Helper method for generating a MetaObject which calls a
			/// specific method on Dynamic, but uses one of the arguments for
			/// the result.
			///
			/// args is either an array of arguments to be passed
			/// to the method as an object[] or NoArgs to signify that
			/// the target method takes no parameters.
			/// </summary>
			private DynamicMetaObject CallMethodReturnLast<TBinder> (MethodInfo method, TBinder binder, Expression[] args, Expression value, Fallback<TBinder> fallback)
				where TBinder : DynamicMetaObjectBinder
			{
				//
				// First, call fallback to do default binding
				// This produces either an error or a call to a .NET member
				//
				DynamicMetaObject fallbackResult = fallback (this, binder, null);

				//
				// Build a new expression like:
				// {
				//   object result;
				//   TrySetMember(payload, result = value) ? result : fallbackResult
				// }
				//

				ParameterExpression result = Expression.Parameter (typeof (object), null);
				ParameterExpression callArgs = Expression.Parameter (typeof (object[]), null);
				ReadOnlyCollection<Expression> callArgsValue = GetConvertedArgs (args);

				var callDynamic = new DynamicMetaObject (
					Expression.Block (
						new TrueReadOnlyCollection<ParameterExpression> (result, callArgs),
						new TrueReadOnlyCollection<Expression> (
							Expression.Assign (callArgs, Expression.NewArrayInit (typeof (object), callArgsValue)),
							Expression.Condition (
								Expression.Call (
									GetLimitedSelf (),
									method,
									BuildCallArgs (
										binder,
										method.Name == "TryInvokeMember", // JJA
										args,
										callArgs,
										Expression.Assign (result, Expression.Convert (value, typeof (object)))
									)
								),
								Expression.Block (
									ReferenceArgAssign (callArgs, args),
									result
								),
								fallbackResult.Expression,
								typeof (object)
							)
						)
					),
					GetRestrictions ().Merge (fallbackResult.Restrictions)
				);

				//
				// Now, call fallback again using our new MO as the error
				// When we do this, one of two things can happen:
				//   1. Binding will succeed, and it will ignore our call to
				//      the dynamic method, OR
				//   2. Binding will fail, and it will use the MO we created
				//      above.
				//
				return fallback (this, binder, callDynamic);
			}

			/// <summary>
			/// Helper method for generating a MetaObject which calls a
			/// specific method on Dynamic, but uses one of the arguments for
			/// the result.
			///
			/// args is either an array of arguments to be passed
			/// to the method as an object[] or NoArgs to signify that
			/// the target method takes no parameters.
			/// </summary>
			private DynamicMetaObject CallMethodNoResult<TBinder> (MethodInfo method, TBinder binder, Expression[] args, Fallback<TBinder> fallback)
				where TBinder : DynamicMetaObjectBinder
			{
				//
				// First, call fallback to do default binding
				// This produces either an error or a call to a .NET member
				//
				DynamicMetaObject fallbackResult = fallback (this, binder, null);
				ParameterExpression callArgs = Expression.Parameter (typeof (object[]), null);
				ReadOnlyCollection<Expression> callArgsValue = GetConvertedArgs (args);

				//
				// Build a new expression like:
				//   if (TryDeleteMember(payload)) { } else { fallbackResult }
				//
				var callDynamic = new DynamicMetaObject (
					Expression.Block (
						new TrueReadOnlyCollection<ParameterExpression> (callArgs),
						new TrueReadOnlyCollection<Expression> (
							Expression.Assign (callArgs, Expression.NewArrayInit (typeof (object), callArgsValue)),
							Expression.Condition (
								Expression.Call (
									GetLimitedSelf (),
									method,
									BuildCallArgs (
										binder,
										method.Name == "TryInvokeMember", // JJA
										args,
										callArgs,
										null
									)
								),
								Expression.Block (
									ReferenceArgAssign (callArgs, args),
									AstUtils.Empty
								),
								fallbackResult.Expression,
								typeof (void)
							)
						)
					),
					GetRestrictions ().Merge (fallbackResult.Restrictions)
				);

				//
				// Now, call fallback again using our new MO as the error
				// When we do this, one of two things can happen:
				//   1. Binding will succeed, and it will ignore our call to
				//      the dynamic method, OR
				//   2. Binding will fail, and it will use the MO we created
				//      above.
				//
				return fallback (this, binder, callDynamic);
			}

			/// <summary>
			/// Checks if the derived type has overridden the specified method.  If there is no
			/// implementation for the method provided then Dynamic falls back to the base class
			/// behavior which lets the call site determine how the binder is performed.
			/// </summary>
			private bool IsOverridden (MethodInfo method)
			{
				MemberInfo[] methods = Value.GetType ().GetMember (method.Name, MemberTypes.Method, BindingFlags.Public | BindingFlags.Instance);

				foreach (MethodInfo mi in methods)
				{
					if (mi.DeclaringType != typeof (GreedyDynamicObject) && mi.GetBaseDefinition () == method)
					{
						return true;
					}
				}

				return false;
			}

			/// <summary>
			/// Returns a Restrictions object which includes our current restrictions merged
			/// with a restriction limiting our type
			/// </summary>
			private BindingRestrictions GetRestrictions ()
			{
				Debug.Assert (Restrictions == BindingRestrictions.Empty, "We don't merge, restrictions are always empty");

				// JJA - added method below which was private
				return GetTypeRestriction (this);
			}

			static BindingRestrictions GetTypeRestriction (DynamicMetaObject obj)
			{
				if (obj.Value == null && obj.HasValue)
				{
					return BindingRestrictions.GetInstanceRestriction (obj.Expression, null);
				}
				return BindingRestrictions.GetTypeRestriction (obj.Expression, obj.LimitType);
			}

			/// <summary>
			/// Returns our Expression converted to GreedyDynamicObject
			/// </summary>
			private Expression GetLimitedSelf ()
			{
				// Convert to GreedyDynamicObject rather than LimitType, because
				// the limit type might be non-public.
				if (TypeUtils.AreEquivalent (Expression.Type, typeof (GreedyDynamicObject)))
				{
					return Expression;
				}
				return Expression.Convert (Expression, typeof (GreedyDynamicObject));
			}

			private new GreedyDynamicObject Value => (GreedyDynamicObject)base.Value;

			// It is okay to throw NotSupported from this binder. This object
			// is only used by GreedyDynamicObject.GetMember--it is not expected to
			// (and cannot) implement binding semantics. It is just so the DO
			// can use the Name and IgnoreCase properties.
			private sealed class GetBinderAdapter : GetMemberBinder
			{
				internal GetBinderAdapter (InvokeMemberBinder binder)
					: base (binder.Name, binder.IgnoreCase)
				{
				}

				public override DynamicMetaObject FallbackGetMember (DynamicMetaObject target, DynamicMetaObject errorSuggestion)
				{
					throw new NotSupportedException ();
				}
			}

			internal static Expression[] GetExpressions (DynamicMetaObject[] objects)
			{
				Expression[] array = new Expression[objects.Length];

				for (int i = 0; i < objects.Length; i++)
				{
					DynamicMetaObject dynamicMetaObject = objects[i];
					Expression expression = array[i] = dynamicMetaObject.Expression;
				}

				return array;
			}

			// JJA
			internal static ParameterModifier GetParameterModifiers (Expression[] objects)
			{
				var mod = new ParameterModifier (Math.Max (1, objects.Length));
				for (int i = 0; i < objects.Length; i++)
				{
					if (objects[i] is ParameterExpression pa && pa.IsByRef) mod[i] = true;
					//DynamicMetaObject dynamicMetaObject = objects [i];
					//if (dynamicMetaObject.Expression is ParameterExpression pa && pa.IsByRef) mod[i] = true;
				}
				return mod;
			}
		}
	}

	static class TypeUtils
	{
		public static bool AreEquivalent (Type t1, Type t2)
		{
			if (t1 != null)
			{
				return t1.IsEquivalentTo (t2);
			}
			return false;
		}
	}

	static class AstUtils
	{
		internal static readonly object BoxedFalse = false;

		internal static readonly object BoxedTrue = true;

		internal static readonly object BoxedIntM1 = -1;

		internal static readonly object BoxedInt0 = 0;

		internal static readonly object BoxedInt1 = 1;

		internal static readonly object BoxedInt2 = 2;

		internal static readonly object BoxedInt3 = 3;

		internal static readonly object BoxedDefaultSByte = (sbyte)0;

		internal static readonly object BoxedDefaultChar = '\0';

		internal static readonly object BoxedDefaultInt16 = (short)0;

		internal static readonly object BoxedDefaultInt64 = 0L;

		internal static readonly object BoxedDefaultByte = (byte)0;

		internal static readonly object BoxedDefaultUInt16 = (ushort)0;

		internal static readonly object BoxedDefaultUInt32 = 0u;

		internal static readonly object BoxedDefaultUInt64 = 0uL;

		internal static readonly object BoxedDefaultSingle = 0f;

		internal static readonly object BoxedDefaultDouble = 0.0;

		internal static readonly object BoxedDefaultDecimal = 0m;

		internal static readonly object BoxedDefaultDateTime = default (DateTime);

		private static readonly ConstantExpression s_true = Expression.Constant (BoxedTrue);

		private static readonly ConstantExpression s_false = Expression.Constant (BoxedFalse);

		private static readonly ConstantExpression s_m1 = Expression.Constant (BoxedIntM1);

		private static readonly ConstantExpression s_0 = Expression.Constant (BoxedInt0);

		private static readonly ConstantExpression s_1 = Expression.Constant (BoxedInt1);

		private static readonly ConstantExpression s_2 = Expression.Constant (BoxedInt2);

		private static readonly ConstantExpression s_3 = Expression.Constant (BoxedInt3);

		internal static readonly DefaultExpression Empty = Expression.Empty ();

		internal static readonly ConstantExpression Null = Expression.Constant (null);

		internal static ConstantExpression Constant (bool value)
		{
			if (!value)
			{
				return s_false;
			}
			return s_true;
		}

		internal static ConstantExpression Constant (int value)
		{
			switch (value)
			{
				case -1:
					return s_m1;
				case 0:
					return s_0;
				case 1:
					return s_1;
				case 2:
					return s_2;
				case 3:
					return s_3;
				default:
					return Expression.Constant (value);
			}
		}
	}

	internal static partial class CachedReflectionInfo
	{
		private static MethodInfo s_String_Format_String_ObjectArray;
		internal static MethodInfo String_Format_String_ObjectArray =>
								  s_String_Format_String_ObjectArray ??
								 (s_String_Format_String_ObjectArray = typeof (string).GetMethod (nameof (string.Format), new Type[] { typeof (string), typeof (object[]) }));

		private static ConstructorInfo s_InvalidCastException_Ctor_String;
		internal static ConstructorInfo InvalidCastException_Ctor_String =>
									   s_InvalidCastException_Ctor_String ??
									  (s_InvalidCastException_Ctor_String = typeof (InvalidCastException).GetConstructor (new Type[] { typeof (string) }));

		private static MethodInfo s_DynamicObject_TryGetMember;
		internal static MethodInfo DynamicObject_TryGetMember =>
								  s_DynamicObject_TryGetMember ??
								 (s_DynamicObject_TryGetMember = typeof (GreedyDynamicObject).GetMethod (nameof (GreedyDynamicObject.TryGetMember)));

		private static MethodInfo s_DynamicObject_TrySetMember;
		internal static MethodInfo DynamicObject_TrySetMember =>
								  s_DynamicObject_TrySetMember ??
								 (s_DynamicObject_TrySetMember = typeof (GreedyDynamicObject).GetMethod (nameof (GreedyDynamicObject.TrySetMember)));

		private static MethodInfo s_DynamicObject_TryDeleteMember;
		internal static MethodInfo DynamicObject_TryDeleteMember =>
								  s_DynamicObject_TryDeleteMember ??
								 (s_DynamicObject_TryDeleteMember = typeof (GreedyDynamicObject).GetMethod (nameof (GreedyDynamicObject.TryDeleteMember)));

		private static MethodInfo s_DynamicObject_TryGetIndex;
		internal static MethodInfo DynamicObject_TryGetIndex =>
								  s_DynamicObject_TryGetIndex ??
								 (s_DynamicObject_TryGetIndex = typeof (GreedyDynamicObject).GetMethod (nameof (GreedyDynamicObject.TryGetIndex)));

		private static MethodInfo s_DynamicObject_TrySetIndex;
		internal static MethodInfo DynamicObject_TrySetIndex =>
								  s_DynamicObject_TrySetIndex ??
								 (s_DynamicObject_TrySetIndex = typeof (GreedyDynamicObject).GetMethod (nameof (GreedyDynamicObject.TrySetIndex)));

		private static MethodInfo s_DynamicObject_TryDeleteIndex;
		internal static MethodInfo DynamicObject_TryDeleteIndex =>
								  s_DynamicObject_TryDeleteIndex ??
								 (s_DynamicObject_TryDeleteIndex = typeof (GreedyDynamicObject).GetMethod (nameof (GreedyDynamicObject.TryDeleteIndex)));

		private static MethodInfo s_DynamicObject_TryConvert;
		internal static MethodInfo DynamicObject_TryConvert =>
								  s_DynamicObject_TryConvert ??
								 (s_DynamicObject_TryConvert = typeof (GreedyDynamicObject).GetMethod (nameof (GreedyDynamicObject.TryConvert)));

		private static MethodInfo s_DynamicObject_TryInvoke;
		internal static MethodInfo DynamicObject_TryInvoke =>
								  s_DynamicObject_TryInvoke ??
								 (s_DynamicObject_TryInvoke = typeof (GreedyDynamicObject).GetMethod (nameof (GreedyDynamicObject.TryInvoke)));

		private static MethodInfo s_DynamicObject_TryInvokeMember;
		internal static MethodInfo DynamicObject_TryInvokeMember =>
								  s_DynamicObject_TryInvokeMember ??
								 (s_DynamicObject_TryInvokeMember = typeof (GreedyDynamicObject).GetMethod (nameof (GreedyDynamicObject.TryInvokeMember)));

		private static MethodInfo s_DynamicObject_TryBinaryOperation;
		internal static MethodInfo DynamicObject_TryBinaryOperation =>
								  s_DynamicObject_TryBinaryOperation ??
								 (s_DynamicObject_TryBinaryOperation = typeof (GreedyDynamicObject).GetMethod (nameof (GreedyDynamicObject.TryBinaryOperation)));

		private static MethodInfo s_DynamicObject_TryUnaryOperation;
		internal static MethodInfo DynamicObject_TryUnaryOperation =>
								  s_DynamicObject_TryUnaryOperation ??
								 (s_DynamicObject_TryUnaryOperation = typeof (GreedyDynamicObject).GetMethod (nameof (GreedyDynamicObject.TryUnaryOperation)));

		private static MethodInfo s_DynamicObject_TryCreateInstance;
		internal static MethodInfo DynamicObject_TryCreateInstance =>
								  s_DynamicObject_TryCreateInstance ??
								 (s_DynamicObject_TryCreateInstance = typeof (GreedyDynamicObject).GetMethod (nameof (GreedyDynamicObject.TryCreateInstance)));
	}

	internal static partial class CachedReflectionInfo
	{
		private static ConstructorInfo s_Nullable_Boolean_Ctor;

		internal static ConstructorInfo Nullable_Boolean_Ctor
			=> s_Nullable_Boolean_Ctor ?? (s_Nullable_Boolean_Ctor = typeof (bool?).GetConstructor (new[] { typeof (bool) }));

		private static ConstructorInfo s_Decimal_Ctor_Int32;
		internal static ConstructorInfo Decimal_Ctor_Int32 =>
									   s_Decimal_Ctor_Int32 ??
									  (s_Decimal_Ctor_Int32 = typeof (decimal).GetConstructor (new[] { typeof (int) }));

		private static ConstructorInfo s_Decimal_Ctor_UInt32;
		internal static ConstructorInfo Decimal_Ctor_UInt32 =>
									   s_Decimal_Ctor_UInt32 ??
									  (s_Decimal_Ctor_UInt32 = typeof (decimal).GetConstructor (new[] { typeof (uint) }));

		private static ConstructorInfo s_Decimal_Ctor_Int64;
		internal static ConstructorInfo Decimal_Ctor_Int64 =>
									   s_Decimal_Ctor_Int64 ??
									  (s_Decimal_Ctor_Int64 = typeof (decimal).GetConstructor (new[] { typeof (long) }));

		private static ConstructorInfo s_Decimal_Ctor_UInt64;
		internal static ConstructorInfo Decimal_Ctor_UInt64 =>
									   s_Decimal_Ctor_UInt64 ??
									  (s_Decimal_Ctor_UInt64 = typeof (decimal).GetConstructor (new[] { typeof (ulong) }));

		private static ConstructorInfo s_Decimal_Ctor_Int32_Int32_Int32_Bool_Byte;
		internal static ConstructorInfo Decimal_Ctor_Int32_Int32_Int32_Bool_Byte =>
									   s_Decimal_Ctor_Int32_Int32_Int32_Bool_Byte ??
									  (s_Decimal_Ctor_Int32_Int32_Int32_Bool_Byte = typeof (decimal).GetConstructor (new[] { typeof (int), typeof (int), typeof (int), typeof (bool), typeof (byte) }));

		private static FieldInfo s_Decimal_One;
		internal static FieldInfo Decimal_One
			=> s_Decimal_One ?? (s_Decimal_One = typeof (decimal).GetField (nameof (decimal.One)));

		private static FieldInfo s_Decimal_MinusOne;
		internal static FieldInfo Decimal_MinusOne
			=> s_Decimal_MinusOne ?? (s_Decimal_MinusOne = typeof (decimal).GetField (nameof (decimal.MinusOne)));

		private static FieldInfo s_Decimal_MinValue;
		internal static FieldInfo Decimal_MinValue
			=> s_Decimal_MinValue ?? (s_Decimal_MinValue = typeof (decimal).GetField (nameof (decimal.MinValue)));

		private static FieldInfo s_Decimal_MaxValue;
		internal static FieldInfo Decimal_MaxValue
			=> s_Decimal_MaxValue ?? (s_Decimal_MaxValue = typeof (decimal).GetField (nameof (decimal.MaxValue)));

		private static FieldInfo s_Decimal_Zero;
		internal static FieldInfo Decimal_Zero
			=> s_Decimal_Zero ?? (s_Decimal_Zero = typeof (decimal).GetField (nameof (decimal.Zero)));

		private static FieldInfo s_DateTime_MinValue;
		internal static FieldInfo DateTime_MinValue
			=> s_DateTime_MinValue ?? (s_DateTime_MinValue = typeof (DateTime).GetField (nameof (DateTime.MinValue)));

		private static MethodInfo s_MethodBase_GetMethodFromHandle_RuntimeMethodHandle;
		internal static MethodInfo MethodBase_GetMethodFromHandle_RuntimeMethodHandle =>
								  s_MethodBase_GetMethodFromHandle_RuntimeMethodHandle ??
								 (s_MethodBase_GetMethodFromHandle_RuntimeMethodHandle = typeof (MethodBase).GetMethod (nameof (MethodBase.GetMethodFromHandle), new[] { typeof (RuntimeMethodHandle) }));

		private static MethodInfo s_MethodBase_GetMethodFromHandle_RuntimeMethodHandle_RuntimeTypeHandle;
		internal static MethodInfo MethodBase_GetMethodFromHandle_RuntimeMethodHandle_RuntimeTypeHandle =>
								  s_MethodBase_GetMethodFromHandle_RuntimeMethodHandle_RuntimeTypeHandle ??
								 (s_MethodBase_GetMethodFromHandle_RuntimeMethodHandle_RuntimeTypeHandle = typeof (MethodBase).GetMethod (nameof (MethodBase.GetMethodFromHandle), new[] { typeof (RuntimeMethodHandle), typeof (RuntimeTypeHandle) }));

		private static MethodInfo s_MethodInfo_CreateDelegate_Type_Object;
		internal static MethodInfo MethodInfo_CreateDelegate_Type_Object =>
								  s_MethodInfo_CreateDelegate_Type_Object ??
								 (s_MethodInfo_CreateDelegate_Type_Object = typeof (MethodInfo).GetMethod (nameof (MethodInfo.CreateDelegate), new[] { typeof (Type), typeof (object) }));

		private static MethodInfo s_String_op_Equality_String_String;
		internal static MethodInfo String_op_Equality_String_String =>
								  s_String_op_Equality_String_String ??
								 (s_String_op_Equality_String_String = typeof (string).GetMethod ("op_Equality", new[] { typeof (string), typeof (string) }));

		private static MethodInfo s_String_Equals_String_String;
		internal static MethodInfo String_Equals_String_String =>
								  s_String_Equals_String_String ??
								 (s_String_Equals_String_String = typeof (string).GetMethod ("Equals", new[] { typeof (string), typeof (string) }));

		private static MethodInfo s_DictionaryOfStringInt32_Add_String_Int32;
		internal static MethodInfo DictionaryOfStringInt32_Add_String_Int32 =>
								  s_DictionaryOfStringInt32_Add_String_Int32 ??
								 (s_DictionaryOfStringInt32_Add_String_Int32 = typeof (Dictionary<string, int>).GetMethod (nameof (Dictionary<string, int>.Add), new[] { typeof (string), typeof (int) }));

		private static ConstructorInfo s_DictionaryOfStringInt32_Ctor_Int32;
		internal static ConstructorInfo DictionaryOfStringInt32_Ctor_Int32 =>
									   s_DictionaryOfStringInt32_Ctor_Int32 ??
									  (s_DictionaryOfStringInt32_Ctor_Int32 = typeof (Dictionary<string, int>).GetConstructor (new[] { typeof (int) }));

		private static MethodInfo s_Type_GetTypeFromHandle;
		internal static MethodInfo Type_GetTypeFromHandle =>
								  s_Type_GetTypeFromHandle ??
								 (s_Type_GetTypeFromHandle = typeof (Type).GetMethod (nameof (Type.GetTypeFromHandle)));

		private static MethodInfo s_Object_GetType;
		internal static MethodInfo Object_GetType =>
								  s_Object_GetType ??
								 (s_Object_GetType = typeof (object).GetMethod (nameof (object.GetType)));

		private static MethodInfo s_Decimal_op_Implicit_Byte;
		internal static MethodInfo Decimal_op_Implicit_Byte =>
								  s_Decimal_op_Implicit_Byte ??
								 (s_Decimal_op_Implicit_Byte = typeof (decimal).GetMethod ("op_Implicit", new[] { typeof (byte) }));

		private static MethodInfo s_Decimal_op_Implicit_SByte;
		internal static MethodInfo Decimal_op_Implicit_SByte =>
								  s_Decimal_op_Implicit_SByte ??
								 (s_Decimal_op_Implicit_SByte = typeof (decimal).GetMethod ("op_Implicit", new[] { typeof (sbyte) }));

		private static MethodInfo s_Decimal_op_Implicit_Int16;
		internal static MethodInfo Decimal_op_Implicit_Int16 =>
								  s_Decimal_op_Implicit_Int16 ??
								 (s_Decimal_op_Implicit_Int16 = typeof (decimal).GetMethod ("op_Implicit", new[] { typeof (short) }));

		private static MethodInfo s_Decimal_op_Implicit_UInt16;
		internal static MethodInfo Decimal_op_Implicit_UInt16 =>
								  s_Decimal_op_Implicit_UInt16 ??
								 (s_Decimal_op_Implicit_UInt16 = typeof (decimal).GetMethod ("op_Implicit", new[] { typeof (ushort) }));

		private static MethodInfo s_Decimal_op_Implicit_Int32;
		internal static MethodInfo Decimal_op_Implicit_Int32 =>
								  s_Decimal_op_Implicit_Int32 ??
								 (s_Decimal_op_Implicit_Int32 = typeof (decimal).GetMethod ("op_Implicit", new[] { typeof (int) }));

		private static MethodInfo s_Decimal_op_Implicit_UInt32;
		internal static MethodInfo Decimal_op_Implicit_UInt32 =>
								  s_Decimal_op_Implicit_UInt32 ??
								 (s_Decimal_op_Implicit_UInt32 = typeof (decimal).GetMethod ("op_Implicit", new[] { typeof (uint) }));

		private static MethodInfo s_Decimal_op_Implicit_Int64;
		internal static MethodInfo Decimal_op_Implicit_Int64 =>
								  s_Decimal_op_Implicit_Int64 ??
								 (s_Decimal_op_Implicit_Int64 = typeof (decimal).GetMethod ("op_Implicit", new[] { typeof (long) }));

		private static MethodInfo s_Decimal_op_Implicit_UInt64;
		internal static MethodInfo Decimal_op_Implicit_UInt64 =>
								  s_Decimal_op_Implicit_UInt64 ??
								 (s_Decimal_op_Implicit_UInt64 = typeof (decimal).GetMethod ("op_Implicit", new[] { typeof (ulong) }));

		private static MethodInfo s_Decimal_op_Implicit_Char;
		internal static MethodInfo Decimal_op_Implicit_Char =>
								  s_Decimal_op_Implicit_Char ??
								 (s_Decimal_op_Implicit_Char = typeof (decimal).GetMethod ("op_Implicit", new[] { typeof (char) }));

		private static MethodInfo s_Math_Pow_Double_Double;
		internal static MethodInfo Math_Pow_Double_Double =>
								  s_Math_Pow_Double_Double ??
								 (s_Math_Pow_Double_Double = typeof (Math).GetMethod (nameof (Math.Pow), new[] { typeof (double), typeof (double) }));

	}
}
