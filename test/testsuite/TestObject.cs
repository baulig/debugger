using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;
using Mono.Debugger.Test.Framework;

namespace Mono.Debugger.Tests
{
	[DebuggerTestFixture]
	public class TestObject : DebuggerTestFixture
	{
		public TestObject ()
			: base ("TestObject")
		{ }

		[Test]
		[Category("ManagedTypes")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			const int line_main = 39;
			const int line_main_2 = 44;

			AssertStopped (thread, "X.Main()", line_main);

			int bpt_main_2 = AssertBreakpoint (line_main_2);
			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt_main_2, "X.Main()", line_main_2);

			AssertPrint (thread, "obj", "(object) &(Bar) { <Foo> = { }, Data = 81 }");
			AssertPrint (thread, "boxed", "(object) &(Hello) { Data = 305419896 }");
			AssertPrint (thread, DisplayFormat.HexaDecimal, "boxed",
				     "(object) &(Hello) { Data = 0x12345678 }");
			AssertPrint (thread, "obj.ToString ()", "(string) \"Bar\"");
			AssertPrint (thread, "obj.GetType ()", "(System.MonoType) { \"Bar\" }");
			AssertPrint (thread, "boxed.GetType()", "(System.MonoType) { \"Hello\" }");
			AssertPrint (thread, "boxed.ToString ()", "(string) \"0x12345678\"");
			AssertPrint (thread, "value", "(Hello) { \"0x12345678\" }");
			AssertPrint (thread, "value.ToString ()", "(string) \"0x12345678\"");

			AssertExecute ("continue");
			AssertTargetOutput ("Bar");
			AssertTargetOutput ("Bar");
			AssertTargetOutput ("0x12345678");
			AssertTargetOutput ("0x12345678");
			AssertTargetExited (thread.Process);
		}
	}
}
