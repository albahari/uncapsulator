using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Uncapsulator
{
	/// <summary>
	/// This is a fork of DynamicObject that gives higher priority to custom binding, including cast conversions and method calls such as ToString().
	/// See https://docs.microsoft.com/en-us/archive/blogs/cburrows/on-dynamic-objects-and-dynamicobject 
	/// It also supports pass-by-reference parameters when calling methods.
	/// </summary>
	/// <remarks>
	/// https://github.com/dotnet/runtime/blob/master/src/libraries/System.Linq.Expressions/src/System/Dynamic/DynamicObject.cs
	/// DynamicObject is subject to the MIT License: https://github.com/dotnet/runtime/blob/master/LICENSE.TXT
	/// </remarks>
	partial class GreedyDynamicObject : IDynamicMetaObjectProvider
	{
		internal protected virtual Type WrappedType => GetType ();

		/// <summary>
		/// Enables derived types to create a new instance of <see cref="GreedyDynamicObject"/>.
		/// </summary>
		/// <remarks>
		/// <see cref="GreedyDynamicObject"/> instances cannot be directly instantiated because they have no
		/// implementation of dynamic behavior.
		/// </remarks>
		protected GreedyDynamicObject ()
		{
		}

		#region Public Virtual APIs

		/// <summary>
		/// Provides the implementation of getting a member.  Derived classes can override
		/// this method to customize behavior.  When not overridden the call site requesting the
		/// binder determines the behavior.
		/// </summary>
		/// <param name="binder">The binder provided by the call site.</param>
		/// <param name="result">The result of the get operation.</param>
		/// <returns>true if the operation is complete, false if the call site should determine behavior.</returns>
		public virtual bool TryGetMember (GetMemberBinder binder, out object result)
		{
			result = null;
			return false;
		}

		/// <summary>
		/// Provides the implementation of setting a member.  Derived classes can override
		/// this method to customize behavior.  When not overridden the call site requesting the
		/// binder determines the behavior.
		/// </summary>
		/// <param name="binder">The binder provided by the call site.</param>
		/// <param name="value">The value to set.</param>
		/// <returns>true if the operation is complete, false if the call site should determine behavior.</returns>
		public virtual bool TrySetMember (SetMemberBinder binder, object value) => false;

		/// <summary>
		/// Provides the implementation of deleting a member.  Derived classes can override
		/// this method to customize behavior.  When not overridden the call site requesting the
		/// binder determines the behavior.
		/// </summary>
		/// <param name="binder">The binder provided by the call site.</param>
		/// <returns>true if the operation is complete, false if the call site should determine behavior.</returns>
		public virtual bool TryDeleteMember (DeleteMemberBinder binder) => false;

		/// <summary>
		/// Provides the implementation of calling a member.  Derived classes can override
		/// this method to customize behavior.  When not overridden the call site requesting the
		/// binder determines the behavior.
		/// </summary>
		/// <param name="binder">The binder provided by the call site.</param>
		/// <param name="args">The arguments to be used for the invocation.</param>
		/// <param name="result">The result of the invocation.</param>
		/// <param name="paramModifier">Indicates for each parameter whether to pass by reference.</param>
		/// <returns>true if the operation is complete, false if the call site should determine behavior.</returns>
		public virtual bool TryInvokeMember (InvokeMemberBinder binder, object[] args, out object result, ParameterModifier paramModifier)    // JJA - added paramModifier
		{
			result = null;
			return false;
		}

		/// <summary>
		/// Provides the implementation of converting the <see cref="GreedyDynamicObject"/> to another type.
		/// Derived classes can override this method to customize behavior.  When not overridden the
		/// call site requesting the binder determines the behavior.
		/// </summary>
		/// <param name="binder">The binder provided by the call site.</param>
		/// <param name="result">The result of the conversion.</param>
		/// <returns>true if the operation is complete, false if the call site should determine behavior.</returns>
		public virtual bool TryConvert (ConvertBinder binder, out object result)
		{
			result = null;
			return false;
		}

		/// <summary>
		/// Provides the implementation of creating an instance of the <see cref="GreedyDynamicObject"/>.
		/// Derived classes can override this method to customize behavior.  When not overridden the
		/// call site requesting the binder determines the behavior.
		/// </summary>
		/// <param name="binder">The binder provided by the call site.</param>
		/// <param name="args">The arguments used for creation.</param>
		/// <param name="result">The created instance.</param>
		/// <returns>true if the operation is complete, false if the call site should determine behavior.</returns>
		public virtual bool TryCreateInstance (CreateInstanceBinder binder, object[] args, out object result)
		{
			result = null;
			return false;
		}

		/// <summary>
		/// Provides the implementation of invoking the <see cref="GreedyDynamicObject"/>.  Derived classes can
		/// override this method to customize behavior.  When not overridden the call site requesting
		/// the binder determines the behavior.
		/// </summary>
		/// <param name="binder">The binder provided by the call site.</param>
		/// <param name="args">The arguments to be used for the invocation.</param>
		/// <param name="result">The result of the invocation.</param>
		/// <returns>true if the operation is complete, false if the call site should determine behavior.</returns>
		public virtual bool TryInvoke (InvokeBinder binder, object[] args, out object result)
		{
			result = null;
			return false;
		}

		/// <summary>
		/// Provides the implementation of performing a binary operation.  Derived classes can
		/// override this method to customize behavior.  When not overridden the call site requesting
		/// the binder determines the behavior.
		/// </summary>
		/// <param name="binder">The binder provided by the call site.</param>
		/// <param name="arg">The right operand for the operation.</param>
		/// <param name="result">The result of the operation.</param>
		/// <returns>true if the operation is complete, false if the call site should determine behavior.</returns>
		public virtual bool TryBinaryOperation (BinaryOperationBinder binder, object arg, out object result)
		{
			result = null;
			return false;
		}

		/// <summary>
		/// Provides the implementation of performing a unary operation.  Derived classes can
		/// override this method to customize behavior.  When not overridden the call site requesting
		/// the binder determines the behavior.
		/// </summary>
		/// <param name="binder">The binder provided by the call site.</param>
		/// <param name="result">The result of the operation.</param>
		/// <returns>true if the operation is complete, false if the call site should determine behavior.</returns>
		public virtual bool TryUnaryOperation (UnaryOperationBinder binder, out object result)
		{
			result = null;
			return false;
		}

		/// <summary>
		/// Provides the implementation of performing a get index operation.  Derived classes can
		/// override this method to customize behavior.  When not overridden the call site requesting
		/// the binder determines the behavior.
		/// </summary>
		/// <param name="binder">The binder provided by the call site.</param>
		/// <param name="indexes">The indexes to be used.</param>
		/// <param name="result">The result of the operation.</param>
		/// <returns>true if the operation is complete, false if the call site should determine behavior.</returns>
		public virtual bool TryGetIndex (GetIndexBinder binder, object[] indexes, out object result)
		{
			result = null;
			return false;
		}

		/// <summary>
		/// Provides the implementation of performing a set index operation.  Derived classes can
		/// override this method to customize behavior.  When not overridden the call site requesting
		/// the binder determines the behavior.
		/// </summary>
		/// <param name="binder">The binder provided by the call site.</param>
		/// <param name="indexes">The indexes to be used.</param>
		/// <param name="value">The value to set.</param>
		/// <returns>true if the operation is complete, false if the call site should determine behavior.</returns>
		public virtual bool TrySetIndex (SetIndexBinder binder, object[] indexes, object value) => false;

		/// <summary>
		/// Provides the implementation of performing a delete index operation.  Derived classes
		/// can override this method to customize behavior.  When not overridden the call site
		/// requesting the binder determines the behavior.
		/// </summary>
		/// <param name="binder">The binder provided by the call site.</param>
		/// <param name="indexes">The indexes to be deleted.</param>
		/// <returns>true if the operation is complete, false if the call site should determine behavior.</returns>
		public virtual bool TryDeleteIndex (DeleteIndexBinder binder, object[] indexes) => false;

		/// <summary>
		/// Returns the enumeration of all dynamic member names.
		/// </summary>
		/// <returns>The list of dynamic member names.</returns>
		public virtual IEnumerable<string> GetDynamicMemberNames () => Array.Empty<string> ();

		#endregion

		#region IDynamicMetaObjectProvider Members

		/// <summary>
		/// Returns the <see cref="DynamicMetaObject" /> responsible for binding operations performed on this object,
		/// using the virtual methods provided by this class.
		/// </summary>
		/// <param name="parameter">The expression tree representation of the runtime value.</param>
		/// <returns>
		/// The <see cref="DynamicMetaObject" /> to bind this object.  The object can be encapsulated inside of another
		/// <see cref="DynamicMetaObject"/> to provide custom behavior for individual actions.
		/// </returns>
		public virtual DynamicMetaObject GetMetaObject (Expression parameter) => new GreedyMetaDynamic (parameter, this);

		#endregion
	}
}
