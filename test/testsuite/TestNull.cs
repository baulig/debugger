using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestNull : TestSuite
	{
		public TestNull ()
			: base ("../test/src/TestNull.exe")
		{ }

		[Test]
		public void Main ()
		{
			Process process = Interpreter.Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			const int line_main = 14;
			const int line_main_2 = 21;

			AssertStopped (thread, "X.Main()", line_main);

			int bpt_main_2 = AssertBreakpoint (line_main_2);
			Execute ("continue");
			AssertHitBreakpoint (thread, bpt_main_2, "X.Main()", line_main_2);

			AssertPrint (thread, "x", "(X) null");
			AssertPrint (thread, "hello", "(System.String) null");
			AssertPrint (thread, "int_array", "(System.Int32[]) null");
			AssertPrint (thread, "x_array", "(X[]) null");
			AssertPrint (thread, "y_array", "(X[]) [ null ]");
			AssertPrint (thread, "z_array", "(X[]) [ { Foo = 5 } ]");
			Execute ("set x = new X (81)");
			Execute ("set y_array [0] = new X (9)");
			Execute ("set z_array [0] = null");
			AssertPrint (thread, "x", "(X) { Foo = 81 }");
			AssertPrint (thread, "y_array", "(X[]) [ { Foo = 9 } ]");
			AssertPrint (thread, "z_array", "(X[]) [ null ]");

			Execute ("kill");
		}
	}
}
