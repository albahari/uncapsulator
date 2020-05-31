using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Uncapsulator
{
	/// <summary>
	/// For uncapsulating types.
	/// Add 'using static Uncapsulator.TypeUncapsulator' to your source to make methods in this class easy to call.
	/// </summary>
	public static class TypeUncapsulator
	{
		/// <summary>
		/// Returns a dynamic proxy that lets you access private static members of the type.
		/// </summary>
		public static dynamic Uncapsulate<T> () => Uncapsulate (typeof (T));

		/// <summary>
		/// Returns a dynamic proxy that lets you access private static members of the type.
		/// </summary>
		public static dynamic Uncapsulate (Type type) => new Uncapsulator (null, null, type);

		/// <summary>
		/// Returns a dynamic proxy that lets you access private static members of the type.
		/// </summary>
		public static dynamic Uncapsulate (string fullTypeName, string simpleAssemblyName)
			=> Uncapsulate (fullTypeName, Assembly.Load (simpleAssemblyName));

		/// <summary>
		/// Returns a dynamic proxy that lets you access private static members of the type.
		/// </summary>
		public static dynamic Uncapsulate (string fullTypeName, Assembly assembly)
			=> Uncapsulate (assembly.GetType (fullTypeName, true));
	}
}
