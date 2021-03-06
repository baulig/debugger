using System;
using System.IO;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;
using Mono.Debugger.Test.Framework;

namespace Mono.Debugger.Tests
{
	[DebuggerTestFixture(Timeout = 10000)]
	public class TestSession : DebuggerTestFixture
	{
		public TestSession ()
			: base ("TestSession")
		{ }

		const int line_main = 7;
		const int line_test = 15;
		const int line_bar = 23;
		const int line_world = 33;
		const int line_pub = 38;

		int bpt_test;
		int bpt_bar;

		public override void SetUp ()
		{
			base.SetUp ();

			bpt_test = AssertBreakpoint (line_test);
			bpt_bar = AssertBreakpoint ("Foo.Bar");
		}

		[Test]
		[Category("Session")]
		public void Main ()
		{
			Compile ();

			AssertExecute ("enable " + bpt_test);
			AssertExecute ("enable " + bpt_bar);

			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main()", line_main);

			byte[] session;
			using (MemoryStream ms = new MemoryStream ()) {
				Interpreter.SaveSession (ms);
				session = ms.GetBuffer ();
			}
			AssertExecute ("kill");
			AssertTargetExited (thread.Process);

			// Load

			using (MemoryStream ms = new MemoryStream (session))
				process = LoadSession (ms);
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			thread = process.MainThread;

			AssertStopped (thread, "X.Main()", line_main);

			AssertExecute ("continue");
			AssertTargetOutput ("Hello World");
			AssertHitBreakpoint (thread, bpt_test, "X.Test()", line_test);
			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt_bar, "Foo.Bar()", line_bar);

			AssertExecute ("continue");
			AssertTargetOutput ("Irish Pub");
			AssertTargetOutput ("WORLD!");
			AssertTargetExited (thread.Process);

			// LoadAgain

			using (MemoryStream ms = new MemoryStream (session))
				process = LoadSession (ms);
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			thread = process.MainThread;

			AssertStopped (thread, "X.Main()", line_main);

			AssertExecute ("continue");
			AssertTargetOutput ("Hello World");
			AssertHitBreakpoint (thread, bpt_test, "X.Test()", line_test);
			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt_bar, "Foo.Bar()", line_bar);

			AssertExecute ("continue");
			AssertTargetOutput ("Irish Pub");
			AssertTargetOutput ("WORLD!");
			AssertTargetExited (thread.Process);
		}

		[Test]
		[Category("Session")]
		public void TestBreakpoint ()
		{
			Compile ();

			AssertExecute ("enable " + bpt_test);
			AssertExecute ("enable " + bpt_bar);

			AssertExecute ("run");
			Process process = AssertMainProcessCreated ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main()", line_main);

			AssertExecute ("continue");
			AssertTargetOutput ("Hello World");
			AssertHitBreakpoint (thread, bpt_test, "X.Test()", line_test);
			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt_bar, "Foo.Bar()", line_bar);

			AssertExecute ("next");
			AssertStopped (thread, "Foo.Bar()", line_bar+1);

			//
			// This breakpoint is contect-sensitive; ie. we can't use the
			// expression "hello.World ()" to insert it when we're at the
			// beginning of Main().
			//
			// We test here whether the session code correctly handles
			// this situation.
			//
			int bpt_world = AssertBreakpoint ("hello.World ()");

			AssertExecute ("run -yes");
			AssertTargetExited (thread.Process);

			process = AssertMainProcessCreated ();
			thread = process.MainThread;

			AssertStopped (thread, "X.Main()", line_main);

			AssertExecute ("continue");
			AssertTargetOutput ("Hello World");
			AssertHitBreakpoint (thread, bpt_test, "X.Test()", line_test);
			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt_bar, "Foo.Bar()", line_bar);
			AssertExecute ("continue");
			AssertTargetOutput ("Irish Pub");
			AssertHitBreakpoint (thread, bpt_world, "Hello.World()", line_world);
			AssertExecute ("continue");

			AssertTargetOutput ("WORLD!");
			AssertTargetExited (thread.Process);

			int bpt_pub = AssertBreakpoint ("Hello.IrishPub");
			AssertExecute ("disable " + bpt_test);
			AssertExecute ("disable " + bpt_bar);

			AssertExecute ("run");
			process = AssertMainProcessCreated ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);
			thread = process.MainThread;

			AssertStopped (thread, "X.Main()", line_main);

			AssertExecute ("continue");
			AssertTargetOutput ("Hello World");
			AssertHitBreakpoint (thread, bpt_pub, "Hello.IrishPub()", line_pub);
			AssertExecute ("continue");
			AssertTargetOutput ("Irish Pub");
			AssertHitBreakpoint (thread, bpt_world, "Hello.World()", line_world);
			AssertExecute ("continue");

			AssertTargetOutput ("WORLD!");
			AssertTargetExited (thread.Process);

			AssertExecute ("delete " + bpt_world);
		}
	}
}
