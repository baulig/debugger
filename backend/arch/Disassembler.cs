using System;

namespace Mono.Debugger
{
	internal abstract class Disassembler : IDisposable
	{
		// <summary>
		//   Get the size of the current instruction.
		// </summary>
		public abstract int GetInstructionSize (TargetMemoryAccess memory,
							TargetAddress address);

		// <summary>
		//   Disassemble one method.
		// </summary>
		public abstract AssemblerMethod DisassembleMethod (TargetMemoryAccess memory,
								   Method method);

		// <summary>
		//   Disassemble one instruction.
		//   If @imethod is non-null, it specifies the current method which will
		//   be used to lookup function names from trampoline calls.
		// </summary>
		public abstract AssemblerLine DisassembleInstruction (TargetMemoryAccess memory,
								      Method method,
								      TargetAddress address);

		//
		// IDisposable
		//

		private bool disposed = false;

		protected virtual void DoDispose ()
		{ }

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (!this.disposed) {
				this.disposed = true;

				// Release unmanaged resources
				lock (this) {
					DoDispose ();
				}
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~Disassembler ()
		{
			Dispose (false);
		}
	}
}
