using System;
using Xunit;
using Uncapsulator;
using static Uncapsulator.TypeUncapsulator;
using System.Text;
using System.Collections.Generic;
using System.Linq;

namespace Tests
{
	public class UnitTests
	{
		dynamic uncap = new Demo ().Uncapsulate ();
		dynamic uncapStatic = Uncapsulate<Demo> ();

		[Fact] void TestPrivateField () => Assert.Equal (123, (int)uncap._private);
		[Fact] void TestPrivateFieldLongConvert () => Assert.Equal (123, (long)uncap._private);
		[Fact] void TestPrivateFieldUri () => Assert.Contains ("MustHaveAuthority", ((string)new Uri ("http://www.linqpad.net").Uncapsulate ()._syntax._flags.ToString ()));
		[Fact] void TestConversion () => Assert.IsAssignableFrom<Exception> ((Exception)uncap._ex);
		[Fact] void TestPrivateFieldSet () { uncap._private = 234; Assert.Equal (234, (int)uncap._private); }
		[Fact] void TestPrivateMethod () => Assert.Equal ("Private Method", (string)uncap.PrivateMethod ());
		[Fact] void TestNestedObjectAccess () => Assert.Equal (123, (int)uncap.NestedPrivate.NestedPrivate._private);
		[Fact] void TestGenericMethod () => Assert.Equal ("StringBuilder", (string)uncap.PrivateGeneric<StringBuilder> ());
		[Fact] void TestGenericMethodWithParam1 () => Assert.Equal (0, ((StringBuilder)uncap.PrivateGeneric<StringBuilder> (new StringBuilder ())).Length);
		[Fact] void TestGenericMethodWithParam2 () => Assert.Equal ("123 StringBuilder", (string)uncap.PrivateGeneric<StringBuilder> (123));
		[Fact] void TestNullMethod () => Assert.Equal (null, uncap.NullMethod ().ToObject ());
		[Fact] void TestNullMethodThrows () => Assert.StartsWith ("You attempted to call method 'PrivateMethod", Assert.Throws<UncapsulatorException> (() => uncap.NullMethod ().PrivateMethod ()).Message);
		[Fact] void TestNullProperty () => Assert.Null ((string)uncap.NullProp);
		[Fact] void TestNullPropertyThrows () => Assert.StartsWith ("You attempted to call method 'PrivateMethod", Assert.Throws<UncapsulatorException> (() => uncap.NullProp.PrivateMethod ()).Message);
		[Fact] void TestUnwrap () => Assert.Equal (123, (int)uncap.DemoDemo (uncap)._private);
		[Fact] void TestOptional () => Assert.Equal ("123optional", (string)uncap.Optional (123));
		[Fact] void TestOptionalRef () { int x = 123; Assert.Equal ("234optional", (string)uncap.OptionalRef (ref x)); Assert.Equal (234, x); }
		[Fact] void TestInvokeDelegate () => Assert.Equal ("test", (string)uncap.TestFunc ("test"));

		[Fact] void TestIndexerGet () => Assert.Equal (345, (int)uncap[345]);
		[Fact] void TestIndexerSet () { uncap[100] = 200; Assert.Equal (300, (int)uncap._private); }
		[Fact] void TestIndexerGetOverload () => Assert.Equal ('q', (char)uncap['q']);
		[Fact] void TestArrayIndexerGet () => Assert.Equal ('e', (char)uncap._array[1]);
		[Fact] void TestArrayIndexerSet () { uncap._array[1] = 'x'; Assert.Equal ('x', (char)uncap._array[1]); }

		[Fact] void TestMethodWithBadName () => Assert.Contains ("does not contain a method", Assert.Throws<UncapsulatorException> (() => uncap.PrivateMethod2 ()).Message);
		[Fact] void TestMethodWithBadOverload () => Assert.Contains ("overload", Assert.Throws<UncapsulatorException> (() => uncap.PrivateMethod (DateTime.Now)).Message);
		[Fact] void TestMemberWithBadName () => Assert.Contains ("does not contain a definition", Assert.Throws<UncapsulatorException> (() => uncap._notThere).Message);

		[Fact] void TestStaticMethod () => Assert.Equal ("Private Static", (string)uncapStatic.PrivateStatic ());
		[Fact] void TestStaticProperty () => Assert.Equal ("Static Property", (string)uncapStatic.PublicStaticProp);
		[Fact] void TestStaticField () => Assert.Equal ("Static Field", (string)uncapStatic.PrivateStaticField);

		[Fact] void TestByRef () { int x = 5; uncap.RefTest (ref x); Assert.Equal (10, x); }
		[Fact] void TestOut () { uncap.OutTest (out string s); Assert.Equal ("OutTest", s); }
		[Fact] void TestByRefGeneric () { uncap.OutTestGeneric<StringBuilder> (out StringBuilder sb); Assert.Equal (0, sb.Length); }

		[Fact] void TestInterface () => Assert.Equal ("InternalMethod", (string)uncap._inner.CastTo ("IInternal").InternalMethod ());
		[Fact] void TestCastToBase1 () => Assert.Equal (1, (int)uncap.CastTo ("Super")._private);
		[Fact] void TestCastToBase2 () => Assert.Equal (1, (int)uncap.CastTo<Super> ()._private);

		[Fact] void TestConstructor () => Assert.Equal (123, (int)Uncapsulate<Demo> ().@new (123)._private);

		[Fact] void TestToDynamicSequence () => Assert.Equal (4, ((IEnumerable<dynamic>)uncap._sequence.ToDynamicSequence ()).Sum (item => (int)item.Y));
		[Fact] void TestToNonDynamicSequence () => Assert.Throws<UncapsulatorException> (() => uncap._private.ToDynamicSequence ());

		class Super { int _private = 1; }
		class Demo : Super
		{
			int _private = 123;
			ArgumentException _ex = new ArgumentException ("test");
			char[] _array = "Hello".ToCharArray ();
			string PrivateMethod () => "Private Method";
			Demo NestedPrivate => new Demo ();
			string PrivateGeneric<T> () => typeof (T).Name;
			T PrivateGeneric<T> (T arg1) => arg1;
			string PrivateGeneric<T> (int arg) => arg.ToString () + " " + typeof (T).Name;
			string NullMethod () => null;
			string NullProp => null;
			Demo DemoDemo (Demo other) => other;
			static string PrivateStatic () => "Private Static";
			public static string PublicStaticProp => "Static Property";
			static string PrivateStaticField = "Static Field";
			void RefTest (ref int x) => x *= 2;
			void OutTest (out string s) => s = "OutTest";
			void OutTestGeneric<T> (out T x) where T : new() => x = new T ();
			string Optional (int x, string y = "optional") => x + y;
			string OptionalRef (ref int x, string y = "optional") => (x = 234) + y;
			object ManyOptional (int a = 1, int b = 2, int c = 3) => new { a, b, c };
			Func<string, string> TestFunc = s => s;

			int this[int x]
			{
				get => x;
				set => _private = x + value;
			}
			public char this[char x] => x;
			public string this[string x] => x;

			Inner _inner = new Inner ();
			Inner[] _sequence = new[] { new Inner (), new Inner () };

			class Inner : IInternal
			{
				string IInternal.InternalMethod () => "InternalMethod";
				int IInternal.InternalProp => 999;
				int X = 1, Y = 2;
			}

			interface IInternal
			{
				string InternalMethod ();
				int InternalProp { get; }
			}

			static class StaticInner
			{
				static string PrivateStatic () => "Private static inner";
			}

			public Demo () { }

			Demo (int x) => _private = x;
		}

		class Sub : Demo
		{
			int _private = 234;
			static int _sub = 12341234;
		}
	}
}
