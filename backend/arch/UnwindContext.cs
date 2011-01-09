using System;
using Mono.Debugger.Backend;

namespace Mono.Debugger.Architectures
{
	internal class UnwindContext
	{
		public StackFrame Frame {
			get; private set;
		}

		public Architecture Architecture {
			get { return Frame.Thread.Architecture; }
		}

		public TargetAddress StartAddress {
			get; private set;
		}

		public byte[] PrologueCode {
			get; private set;
		}

		public UnwindContext (StackFrame frame, TargetAddress start_address, byte[] prologue_code)
		{
			this.Frame = frame;

			this.StartAddress = start_address;
			this.PrologueCode = prologue_code;

			var original_values = frame.Registers.Values;
			original_registers = new TargetAddress [original_values.Length];
			registers = new RegisterValue [original_registers.Length];

			for (int i = 0; i < original_registers.Length; i++) {
				original_registers [i] = make_address (original_values [i]);
				registers [i].State = RegisterState.Unknown;
			}
		}

		protected TargetAddress make_address (long value)
		{
			return new TargetAddress (Architecture.TargetMemoryInfo.AddressDomain, value);
		}

		public enum RegisterState
		{
			Unknown = 0,
			Preserved,
			Register,
			Memory
		}

		public struct RegisterValue
		{
			public RegisterState State;
			public int BaseRegister;
			public long Offset;
		}

		public RegisterValue[] Registers {
			get { return registers; }
		}

		TargetAddress[] original_registers;
		RegisterValue[] registers;

		public void Dump ()
		{
			Console.WriteLine ();
			Console.WriteLine ("UNWIND CONTEXT:");
			Console.WriteLine (Frame);

			if (PrologueCode != null) {
				Console.WriteLine ();
				Console.WriteLine ("PROLOGUE:");
				Console.WriteLine (TargetBinaryReader.HexDump (StartAddress, PrologueCode));
			}
			Console.WriteLine ();

			for (int i = 0; i < registers.Length; i++) {
				Console.WriteLine ("{0,8} : {1} - {2,8} {3} {4:x}", Architecture.RegisterNames [i],
						   original_registers [i], registers [i].State, registers [i].BaseRegister,
						   registers [i].Offset);
			}

			Console.WriteLine ();
		}
	}
}
