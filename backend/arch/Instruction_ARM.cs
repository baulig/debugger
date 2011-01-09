using System;
using Mono.Debugger.Backend;

namespace Mono.Debugger.Architectures
{
	internal class Instruction_ARM : Instruction
	{
		bool Bit (ushort n)
		{
			return Opcodes.Bit ((uint) (code[0] + (code[1] << 8) + (code[2] << 16) + (code[3] << 24)), n);
		}

		bool Bit (int n)
		{
			return Bit ((ushort) n);
		}

		uint Bits (ushort start, ushort end)
		{
			return Opcodes.Bits ((uint) (code[0] + (code[1] << 8) + (code[2] << 16) + (code[3] << 24)), start, end);
		}

		int SBits (ushort start, ushort end)
		{
			return Opcodes.SBits ((uint) (code[0] + (code[1] << 8) + (code[2] << 16) + (code[3] << 24)), start, end);
		}

		TargetAddress MakeAddress (ulong value)
		{
			return new TargetAddress (opcodes.TargetMemoryInfo.AddressDomain, (long) (value & 0x03fffffc));
		}

		uint GetRegister (uint rn)
		{
			return (uint) registers.Values [rn];
		}

		uint StatusRegister {
			get { return (uint) registers.Values [(int) ARM_Register.CPSR]; }
		}

		bool IsConditional {
			get { return Bits (28, 31) != 0x0e; }
		}

		bool IsCarryFlagSet {
			get { return (StatusRegister & 0x20000000) != 0; }
		}

		ulong GetShiftedRegister ()
		{
			ushort shift;
			ulong res;
			var rm = Bits (0, 3);
			var shifttype = Bits (5, 6);

			if (Bit (4)) {
				var rs = Bits (8, 11);
				shift = (ushort) ((rs == 15 ? address.Address + 8 : GetRegister (rs)) & 0xFF);
			} else
				shift = (ushort) Bits (7, 11);

			res = (ulong) (rm == 15 ? (address.Address + (Bit (4) ? 12 : 8)) : GetRegister (rm));

			switch (shifttype) {
			case 0: /* LSL */
				res = shift >= 32 ? 0 : res << shift;
				break;

			case 1: /* LSR */
				res = shift >= 32 ? 0 : res >> shift;
				break;

			case 2: /* ASR */
				if (shift >= 32)
					shift = 31;
				res = ((res & 0x80000000L) != 0) ? ~((~res) >> shift) : res >> shift;
				break;

			case 3: /* ROR/RRX */
				shift &= 31;
				if (shift == 0)
					res = (res >> 1) | (IsCarryFlagSet ? 0x80000000UL : 0);
				else
					res = (res >> shift) | (res << (32 - shift));
				break;
			}

			return res & 0xffffffff;
		}

		internal Instruction_ARM (Opcodes_ARM opcodes, TargetMemoryAccess memory, TargetAddress address)
		{
			this.opcodes = opcodes;
			this.address = address;
			this.code = memory.ReadBuffer (address, 4);
			this.registers = memory.GetRegisters ();

			read_instruction (memory);
		}

		internal Instruction_ARM (Opcodes_ARM opcodes, TargetMemoryAccess memory, TargetAddress address,
					  Registers registers, uint insn)
		{
			this.opcodes = opcodes;
			this.address = address;
			this.code = BitConverter.GetBytes (insn);
			this.registers = registers;

			read_instruction (memory);
		}

		void read_instruction (TargetMemoryAccess memory)
		{
			//
			// code[0] is Bits 0..7
			// code[1] is Bits 8..15
			// code[2] is Bits 16..23
			// code[3] is Bits 24..31

			if (Bits (24, 27) == 0x0a) {
				effective_address = address + 8 + (SBits (0, 23) << 2);
				type = IsConditional ? Type.ConditionalJump : Type.Jump;
			} else if (Bits (24, 27) == 0x0b) {
				// FIXME: Add Type.ConditionalCall
				effective_address = address + 8 + (SBits (0, 23) << 2);
				type = IsConditional ? Type.ConditionalJump : Type.Call;
			} else if ((Bits (24, 27) < 4) && (Bits (12, 15) == 0x0f)) {
				ulong operand1, operand2, result;
				uint rn;

				if ((Bits (4, 27) == 0x12fff1) || Bits (4, 27) == 0x12fff3) { // BX <reg>, BLX <reg>
					rn = Bits (0, 3);
					if (rn == 15)
						effective_address = address + 8;
					else
						effective_address = MakeAddress (GetRegister (rn));
					type = Bits (4, 7) == 1 ? Type.IndirectJump : Type.IndirectCall;
					Console.WriteLine ("BX/BLX: {0} {1:x}", rn, effective_address);
				}

				rn = Bits (16, 19);
				operand1 = (ulong) (rn == 15 ? address.Address + 8 : GetRegister (rn));

				if (Bit (25)) {
					ulong immval = (ulong) Bits (0, 7);
					ushort rotate = (ushort) (2 * Bits (8, 11));
					operand2 = ((immval >> rotate) | (immval << (32 - rotate))) & 0xffffffff;
				} else {
					operand2 = GetShiftedRegister ();
				}

				switch (Bits (21, 24)) {
				case 0x0: /*and */
					result = operand1 & operand2;
					break;

				case 0x1: /*eor */
					result = operand1 ^ operand2;
					break;

				case 0x2: /*sub */
					result = operand1 - operand2;
					break;

				case 0x3: /*rsb */
					result = operand2 - operand1;
					break;

				case 0x4: /*add */
					result = operand1 + operand2;
					break;

				case 0x5: /*adc */
					result = operand1 + operand2;
					if (IsCarryFlagSet)
						result++;
					break;

				case 0x6: /*sbc */
					result = operand1 - operand2;
					if (IsCarryFlagSet)
						result++;
					break;

				case 0x7: /*rsc */
					result = operand2 - operand1;
					if (IsCarryFlagSet)
						result++;
					break;

				case 0x8:
				case 0x9:
				case 0xa:
				case 0xb: /* tst, teq, cmp, cmn */
					result = (ulong) (address.Address + 8);
					break;

				case 0xc: /*orr */
					result = operand1 | operand2;
					break;

				case 0xd: /*mov */
					result = operand2;
					break;

				case 0xe: /*bic */
					result = operand1 & ~operand2;
					break;

				case 0xf: /*mvn */
					result = ~operand2;
					break;

				default:
					throw new ArgumentException ();
				}

				effective_address = MakeAddress (result);
				type = Type.IndirectJump;
			} else if ((Bits (24, 27) >= 4) && (Bits (24, 27) < 8) && (Bits (12, 15) == 0x0f)) { // data transfer
				/* byte write to PC */
				var rn = Bits (16, 19);
				var base_addr = MakeAddress (rn == 15 ? (ulong) address.Address + 8 : GetRegister (rn));

				if (Bit (24)) {
					/* pre-indexed */
					long offset = Bit (25) ? (long) GetShiftedRegister () : Bits (0, 11);

					if (Bit (23))
						base_addr += offset;
					else
						base_addr -= offset;
				}

				effective_address = MakeAddress ((ulong) memory.ReadInteger (base_addr));
				type = Type.IndirectJump;
			} else {
				type = Type.Unknown;
			}

			Console.WriteLine ("READ INSTRUCTION: {0} - {1:x} {2:x} {3:x} {4:x} - {5} {6}",
					   address, code [0], code [1], code [2], code [3], type, effective_address);
		}

		protected readonly Opcodes_ARM opcodes;
		TargetAddress address;
		Type type = Type.Unknown;
		TargetAddress effective_address = TargetAddress.Null;
		Registers registers;
		bool is_ip_relative;
		byte[] code;

		public override Opcodes Opcodes {
			get { return opcodes; }
		}

		public override TargetAddress Address {
			get { return address; }
		}

		public override Type InstructionType {
			get { return type; }
		}

		public override bool IsIpRelative {
			get {
				switch (type) {
				case Type.ConditionalJump:
				case Type.IndirectCall:
				case Type.Call:
				case Type.IndirectJump:
				case Type.Jump:
					return true;

				default:
					return is_ip_relative;
				}
			}
		}

		public override bool HasInstructionSize {
			get { return true; }
		}

		public override int InstructionSize {
			get { return 4; }
		}

		public override byte[] Code {
			get { return code; }
		}

		public override TargetAddress GetEffectiveAddress (TargetMemoryAccess memory)
		{
			return effective_address;
		}

		public override bool CanInterpretInstruction {
			get { return false; }
		}

		public override bool InterpretInstruction (Inferior inferior)
		{
			if (type == Type.Jump) {
				registers [(int) ARM_Register.PC].SetValue (effective_address);
				inferior.SetRegisters (registers);
				return true;
			} else if (type == Type.Call) {
				registers [(int) ARM_Register.LR].SetValue (address + 4);
				registers [(int) ARM_Register.PC].SetValue (effective_address);
				inferior.SetRegisters (registers);
				return true;
			}

			return false;
		}

		public bool ScanPrologue (UnwindContext context)
		{
			if (IsConditional)
				return false;

			if (Bits (25, 27) == 4) { // Block data transfer
				if (Bit (20)) // load
					return false;

				var Rn = (int) Bits (16, 19);

				Report.Debug (DebugFlags.StackUnwind, "  found store: {0} {1} {2} {3} - {4:x}",
					      opcodes.Architecture.RegisterNames [Rn],
					      Bit (23) ? "up" : "down", Bit (22) ? "pre" : "post",
					      Bit (21) ? "writeback" : "no-writeback",
					      Bits (0, 15));

				if (context.Registers [Rn].State != UnwindContext.RegisterState.Preserved)
					return false;

				int count = 0;
				for (int i = 0; i < 15; i++) {
					if (Bit (i))
						count++;
				}

				int startoffset, endoffset;

				if (Bit (23)) { // up
					startoffset = (int) context.Registers [Rn].Offset;
					endoffset = startoffset + 4 * count;
				} else { // down
					startoffset = (int) context.Registers [Rn].Offset - 4 * count;
					endoffset = startoffset;
				}

				int offset = startoffset;
				if (Bit (24)) // pre
					offset += 4;

				for (int i = 0; i < 15; i++) {
					if (!Bit (i))
						continue;

					context.Registers [i].State = UnwindContext.RegisterState.Memory;
					context.Registers [i].BaseRegister = Rn;
					context.Registers [i].Offset = offset;

					offset += 4;
				}

				if (Bit (21)) // writeback
					context.Registers [Rn].Offset = endoffset;
			}

			return false;
		}
	}
}
