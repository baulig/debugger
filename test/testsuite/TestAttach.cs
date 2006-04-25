using System;
using SD = System.Diagnostics;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestAttach : TestSuite
	{
		SD.Process child;

		public TestAttach ()
			: base ("TestAttach")
		{ }

		public override void SetUp ()
		{
			base.SetUp ();

			child = SD.Process.Start (MonoExecutable, "--debug " + ExeFileName);
		}

		public override void TearDown ()
		{
			base.TearDown ();

			if (!child.HasExited)
				child.Kill ();
		}

		[Test]
		[Category("Attach")]
		public void Main ()
		{
			Process process = Interpreter.Attach (child.Id);
			Assert.IsTrue (process.MainThread.IsStopped);

			AssertThreadCreated ();
			AssertThreadCreated ();
			AssertThreadCreated ();

			AssertStopped (null, null, -1);
			AssertStopped (null, null, -1);
			AssertStopped (null, null, -1);

			StackFrame frame = process.MainThread.CurrentFrame;
			Assert.IsNotNull (frame);
			Backtrace bt = process.MainThread.GetBacktrace (-1);
			if (bt.Count < 1)
				Assert.Fail ("Cannot get backtrace.");

			process.Detach ();
			AssertProcessExited (process);
			AssertTargetExited ();
		}

		[Test]
		[Category("Attach")]
		public void AttachAgain ()
		{
			Process process = Interpreter.Attach (child.Id);
			Assert.IsTrue (process.MainThread.IsStopped);

			AssertThreadCreated ();
			AssertThreadCreated ();
			AssertThreadCreated ();

			AssertStopped (null, null, -1);
			AssertStopped (null, null, -1);
			AssertStopped (null, null, -1);

			StackFrame frame = process.MainThread.CurrentFrame;
			Assert.IsNotNull (frame);
			Backtrace bt = process.MainThread.GetBacktrace (-1);
			if (bt.Count < 1)
				Assert.Fail ("Cannot get backtrace.");

			process.Detach ();
			AssertProcessExited (process);
			AssertTargetExited ();
		}

		[Test]
		[Category("Attach")]
		public void Kill ()
		{
			Process process = Interpreter.Attach (child.Id);
			Assert.IsTrue (process.MainThread.IsStopped);

			AssertThreadCreated ();
			AssertThreadCreated ();
			AssertThreadCreated ();

			AssertStopped (null, null, -1);
			AssertStopped (null, null, -1);
			AssertStopped (null, null, -1);

			StackFrame frame = process.MainThread.CurrentFrame;
			Assert.IsNotNull (frame);
			Backtrace bt = process.MainThread.GetBacktrace (-1);
			if (bt.Count < 1)
				Assert.Fail ("Cannot get backtrace.");

			Interpreter.Kill ();
			AssertTargetExited ();
			child.WaitForExit ();
		}
	}
}
