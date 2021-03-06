using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;
using Mono.Debugger.Test.Framework;

namespace Mono.Debugger.Tests
{
	[DebuggerTestFixture]
	public class TestMultiThread2 : DebuggerTestFixture
	{
		public TestMultiThread2 ()
			: base ("TestMultiThread2")
		{ }

		public override void SetUp ()
		{
			base.SetUp ();
			Interpreter.IgnoreThreadCreation = true;
		}

		[Test]
		[Category("Threads")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);

			Thread thread = process.MainThread;
			AssertStopped (thread, "main", "X.Main()");

			AssertExecute ("next -bg");

			DebuggerEvent e = AssertEvent ();

			if (e.Type != DebuggerEventType.TargetEvent)
				Assert.Fail ("Got unknown event: {0}", e);
			TargetEventArgs args = (TargetEventArgs) e.Data2;
			if (args.Type != TargetEventType.TargetHitBreakpoint)
				Assert.Fail ("Got unknown event: {0}", args);

			Thread child = (Thread) e.Data;
			AssertFrame (child, "thread main", "X.ThreadMain()");

			AssertExecute ("kill");
		}
	}
}
