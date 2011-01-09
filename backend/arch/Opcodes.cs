using System;
using Mono.Debugger.Backend;

namespace Mono.Debugger.Architectures
{
	internal abstract class Opcodes : DebuggerMarshalByRefObject, IDisposable
	{
		#region Bit Magic

		static ulong submask (ushort x)
		{
			return ((1UL << (x + 1)) - 1);
		}

		internal static bool Bit (uint value, ushort n)
		{
			return ((value >> n) & 1) != 0;
		}

		internal static bool Bit (ulong value, ushort n)
		{
			return ((value >> n) & 1) != 0;
		}

		internal static uint Bits (uint value, ushort start, ushort end)
		{
			return (value >> start) & (uint) submask ((ushort) (end - start));
		}

		internal static ulong Bits (ulong value, ushort start, ushort end)
		{
			return (value >> start) & submask ((ushort) (end - start));
		}

		internal static int SBits (uint value, ushort start, ushort end)
		{
			return (int) (Bits (value, start, end) | ((Bit (value, end) ? 1UL : 0UL) * ~ submask ((ushort) (end - start))));
		}

		internal static long SBits (ulong value, ushort start, ushort end)
		{
			return (long) (Bits (value, start, end) | ((Bit (value, end) ? 1UL : 0UL) * ~ submask ((ushort) (end - start))));
		}

		#endregion

		protected Opcodes (Architecture arch, TargetMemoryInfo target_info)
		{
			this.Architecture = arch;
			this.TargetMemoryInfo = target_info;
		}

		internal Architecture Architecture {
			get; private set;
		}

		internal TargetMemoryInfo TargetMemoryInfo {
			get; private set;
		}

		internal Disassembler Disassembler {
			get { return Architecture.Disassembler; }
		}

		internal abstract Instruction ReadInstruction (TargetMemoryAccess memory,
							       TargetAddress address);

		internal abstract byte[] GenerateNopInstruction ();

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("Opcodes");
		}

		protected virtual void DoDispose ()
		{
		}

		private void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			lock (this) {
				if (disposed)
					return;

				disposed = true;
			}

			// If this is a call to Dispose, dispose all managed resources.
			if (disposing)
				DoDispose ();
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~Opcodes ()
		{
			Dispose (false);
		}
	}
}
