using System;
using Math = System.Math;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger.Backend;
using Mono.Debugger.Languages;

namespace Mono.Debugger
{
	public class Backtrace : DebuggerMarshalByRefObject
	{
		public enum Mode {
			Default,
			Native,
			Managed
		}

		StackFrame last_frame;
		ArrayList frames;
		int current_frame_idx;

		bool tried_lmf;
		TargetAddress lmf_address;

		public Backtrace (StackFrame first_frame)
		{
			this.last_frame = first_frame;

			frames = new ArrayList ();
			frames.Add (first_frame);
		}

		public int Count {
			get { return frames.Count; }
		}

		public StackFrame[] Frames {
			get {
				StackFrame[] retval = new StackFrame [frames.Count];
				frames.CopyTo (retval, 0);
				return retval;
			}
		}

		public StackFrame this [int number] {
			get { return (StackFrame) frames [number]; }
		}

		public StackFrame CurrentFrame {
			get { return this [current_frame_idx]; }
		}

		public int CurrentFrameIndex {
			get { return current_frame_idx; }

			set {
				if ((value < 0) || (value >= frames.Count))
					throw new ArgumentException ();

				current_frame_idx = value;
			}
		}

		internal void GetBacktrace (ThreadServant thread, TargetMemoryAccess memory,
					    Mode mode, TargetAddress until, int max_frames)
		{
			while (TryUnwind (thread, memory, mode, until)) {
				if ((max_frames != -1) && (frames.Count > max_frames))
					break;
			}

			// Ugly hack: in Mode == Mode.Default, we accept wrappers but not as the
			//            last frame.
			if ((mode == Mode.Default) && (frames.Count > 1)) {
				StackFrame last = this [frames.Count - 1];
				if (!IsFrameOkForMode (last, Mode.Managed))
					frames.Remove (last);
			}
		}

		private StackFrame TryLMF (ThreadServant thread, TargetMemoryAccess memory)
		{
			try {
				if (lmf_address.IsNull)
					return null;

				StackFrame new_frame = thread.Architecture.GetLMF (thread, memory, ref lmf_address);
				if (new_frame == null)
					return null;

				// Sanity check; don't loop.
				if (new_frame.StackPointer <= last_frame.StackPointer)
					return null;

				return new_frame;
			} catch (TargetException) {
				return null;
			}
		}

		private bool TryCallback (ThreadServant thread, TargetMemoryAccess memory,
					  ref StackFrame frame, bool exact_match)
		{
			try {
				if (frame == null)
					return false;

				Inferior.CallbackFrame callback = thread.GetCallbackFrame (
					frame.StackPointer, exact_match);
				if (callback == null)
					return false;

				frame = thread.Architecture.CreateFrame (
					thread.Client, FrameType.Normal, memory, callback.Registers);

				FrameType callback_type;
				string frame_name = "<method called from mdb>";

				if (callback.IsRuntimeInvokeFrame) {
					callback_type = FrameType.RuntimeInvoke;
					TargetFunctionType func = thread.GetRuntimeInvokedFunction (callback.ID);
					if (func != null)
						frame_name = String.Format ("<Invocation of: {0}>", func.FullName);
				} else {
					callback_type = FrameType.Callback;
				}

				AddFrame (new StackFrame (
					thread.Client, callback_type, callback.CallAddress, callback.StackPointer,
					TargetAddress.Null, callback.Registers, thread.NativeLanguage,
					new Symbol (frame_name, callback.CallAddress, 0)));
				return true;
			} catch (TargetException) {
				return false;
			}
		}

		private bool IsFrameOkForMode (StackFrame frame, Mode mode)
		{
			if (mode == Mode.Native)
				return true;
			if ((mode == Mode.Default) && !frame.Thread.Process.IsManaged)
				return true;
			if ((frame.Language == null) || !frame.Language.IsManaged)
				return false;
			if (mode == Mode.Default)
				return true;
			if ((frame.SourceAddress == null) || (frame.Method == null))
				return false;
			return frame.Method.WrapperType == WrapperType.None;
		}

		internal bool TryUnwind (ThreadServant thread, TargetMemoryAccess memory,
					 Mode mode, TargetAddress until)
		{
			StackFrame new_frame = null;
			try {
				new_frame = last_frame.UnwindStack (memory);
			} catch (TargetException) {
			}

			if (!TryCallback (thread, memory, ref new_frame, true)) {
				if ((new_frame == null) || !IsFrameOkForMode (new_frame, mode)) {
					if (!tried_lmf) {
						tried_lmf = true;
						Console.WriteLine ("TRY UNWIND: {0} {1}", thread, thread.LMFAddress);
						if (thread.LMFAddress.IsNull)
							return false;
						lmf_address = memory.ReadAddress (thread.LMFAddress);
						Console.WriteLine ("TRY UNWIND #1: {0} {1} {2}",
								   thread, thread.LMFAddress, lmf_address);
					}

					if (!lmf_address.IsNull)
						new_frame = TryLMF (thread, memory);
					else
						return false;
				}
			}

			if (new_frame == null)
				return false;

			// Sanity check; don't loop.
			if (new_frame.StackPointer <= last_frame.StackPointer)
				return false;

			if (!until.IsNull && (new_frame.StackPointer >= until))
				return false;

			AddFrame (new_frame);
			return true;
		}

		public string Print ()
		{
			StringBuilder sb = new StringBuilder ();
			foreach (StackFrame frame in frames)
				sb.Append (String.Format ("{0}\n", frame));
			return sb.ToString ();
		}

		internal void AddFrame (StackFrame new_frame)
		{
			new_frame.SetLevel (frames.Count);
			frames.Add (new_frame);
			last_frame = new_frame;
		}
	}
}
