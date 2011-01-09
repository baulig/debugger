using System;
using Mono.Debugger.Backend;

namespace Mono.Debugger.Architectures
{
	internal class Instruction_ARM : Instruction
	{
		static int submask (ushort x)
		{
			return (int) ((1L << (x + 1)) - 1);
		}

		static bool bit (int value, ushort n)
		{
			return ((value >> n) & 1) != 0;
		}

		static int bits (int value, ushort start, ushort end)
		{
			return (value >> start) & submask ((ushort) (end - start));
		}

		static int sbits (int value, ushort start, ushort end)
		{
			return bits (value, start, end) | ((bit (value, end) ? 1 : 0) * ~ submask ((ushort) (end - start)));
		}

		bool bit (ushort n)
		{
			return bit (code[0] + (code[1] << 8) + (code[2] << 16) + (code[3] << 24), n);
		}

		int bits (ushort start, ushort end)
		{
			return bits (code[0] + (code[1] << 8) + (code[2] << 16) + (code[3] << 24), start, end);
		}

		int sbits (ushort start, ushort end)
		{
			return sbits (code[0] + (code[1] << 8) + (code[2] << 16) + (code[3] << 24), start, end);
		}

		TargetAddress make_address (ulong value)
		{
			return new TargetAddress (Opcodes.TargetMemoryInfo.AddressDomain, (long) (value & 0x03fffffc));
		}

		uint get_reg (int rn)
		{
			return (uint) registers.Values [rn];
		}

		uint get_status_reg ()
		{
			return (uint) registers.Values [(int) Architecture_ARM.ARM_Register.CPSR];
		}

		ulong shifted_reg_val (bool carry)
		{
			ushort shift;
			ulong res;
			int rm = bits (0, 3);
			int shifttype = bits (5, 6);

			if (bit (4)) {
				int rs = bits (8, 11);
				shift = (ushort) ((rs == 15 ? address.Address + 8 : get_reg (rs)) & 0xFF);
			} else
				shift = (ushort) bits (7, 11);

			res = (ulong) (rm == 15 ? (address.Address + (bit (4) ? 12 : 8)) : get_reg (rm));

			switch (shifttype) {
			case 0:			/* LSL */
				res = shift >= 32 ? 0 : res << shift;
				break;

			case 1:			/* LSR */
				res = shift >= 32 ? 0 : res >> shift;
				break;

			case 2:			/* ASR */
				if (shift >= 32)
					shift = 31;
				res = ((res & 0x80000000L) != 0) ? ~((~res) >> shift) : res >> shift;
				break;

			case 3:			/* ROR/RRX */
				shift &= 31;
				if (shift == 0)
					res = (res >> 1) | (carry ? 0x80000000UL : 0);
				else
					res = (res >> shift) | (res << (32 - shift));
				break;
			}

			return res & 0xffffffff;
		}

		internal Instruction_ARM (Opcodes_ARM opcodes, TargetMemoryAccess memory, TargetAddress address)
		{
			this.Opcodes = opcodes;
			this.address = address;
			this.code = memory.ReadBuffer (address, 4);
			this.registers = memory.GetRegisters ();

			Console.WriteLine ("READ INSTRUCTION: {0} - {1:x} {2:x} {3:x} {4:x} - {5:x} {6:x}",
					   address, code [0], code [1], code [2], code [3],
					   (code [3] & 0xf0) >> 4, (code [3] & 0x0f));

			is_conditional = (bits (28, 31) == 0x0e) || (bits (28, 31) == 0x0f);

			//
			// code[0] is bits 0..7
			// code[1] is bits 8..15
			// code[2] is bits 16..23
			// code[3] is bits 24..31

			if (bits (24, 27) == 0x0a) {
				effective_address = address + 8 + (sbits (0, 23) << 2);
				type = is_conditional ? Type.ConditionalJump : Type.Jump;
			} else if (bits (24, 27) == 0x0b) {
				// FIXME: Add Type.ConditionalCall
				effective_address = address + 8 + (sbits (0, 23) << 2);
				type = is_conditional ? Type.ConditionalJump : Type.Call;
			} else if ((bits (24, 27) < 4) && (bits (12, 15) == 0x0f)) {
				ulong operand1, operand2, result;
				int rn;

				if ((bits (4, 27) == 0x12fff1) || bits (4, 27) == 0x12fff3) { // BX <reg>, BLX <reg>
					rn = bits (0, 3);
					if (rn == 15)
						effective_address = address + 8;
					else
						effective_address = make_address (get_reg (rn));
					type = bits (4, 7) == 1 ? Type.IndirectJump : Type.IndirectCall;
					Console.WriteLine ("BX/BLX: {0} {1:x}", rn, effective_address);
				}

				uint c = (get_status_reg () & 0x20000000) != 0 ? 1u : 0u;
				rn = bits (16, 19);
				operand1 = (ulong) (rn == 15 ? address.Address + 8 : get_reg (rn));

				if (bit (25)) {
					ulong immval = (ulong) bits (0, 7);
					ushort rotate = (ushort) (2 * bits (8, 11));
					operand2 = ((immval >> rotate) | (immval << (32 - rotate))) & 0xffffffff;
				} else {
					operand2 = shifted_reg_val (c == 1);
				}

				switch (bits (21, 24)) {
				case 0x0:	/*and */
					result = operand1 & operand2;
					break;

				case 0x1:	/*eor */
					result = operand1 ^ operand2;
					break;

				case 0x2:	/*sub */
					result = operand1 - operand2;
					break;

				case 0x3:	/*rsb */
					result = operand2 - operand1;
					break;

				case 0x4:	/*add */
					result = operand1 + operand2;
					break;

				case 0x5:	/*adc */
					result = operand1 + operand2 + c;
					break;

				case 0x6:	/*sbc */
					result = operand1 - operand2 + c;
					break;

				case 0x7:	/*rsc */
					result = operand2 - operand1 + c;
					break;

				case 0x8:
				case 0x9:
				case 0xa:
				case 0xb:	/* tst, teq, cmp, cmn */
					result = (ulong) (address.Address + 8);
					break;

				case 0xc:	/*orr */
					result = operand1 | operand2;
					break;

				case 0xd:	/*mov */
					/* Always step into a function.  */
					result = operand2;
					break;

				case 0xe:	/*bic */
					result = operand1 & ~operand2;
					break;

				case 0xf:	/*mvn */
					result = ~operand2;
					break;

				default:
					throw new ArgumentException ();
				}

				effective_address = make_address (result);
				type = Type.IndirectJump;

				Console.WriteLine ("INDIRECT JUMP: {0:x}", effective_address);
			} else if ((bits (24, 27) >= 4) && (bits (24, 27) < 8) && (bits (12, 15) == 0x0f)) { // data transfer
				/* byte write to PC */
				var rn = bits (16, 19);
				var base_addr = new TargetAddress (memory.AddressDomain, rn == 15 ? address.Address + 8 : get_reg (rn));

				if (bit (24)) {
					/* pre-indexed */
					uint c = (get_status_reg () & 0x20000000) != 0 ? 1u : 0u;
					long offset = bit (25) ? (long) shifted_reg_val (c == 1) : bits (0, 11);

					if (bit (23))
						base_addr += offset;
					else
						base_addr -= offset;
				}

				effective_address = make_address ((ulong) memory.ReadInteger (base_addr));
				type = Type.IndirectJump;
			} else {
				type = Type.Unknown;
			}
		}

		protected readonly Opcodes_ARM Opcodes;
		readonly TargetAddress address;
		readonly Type type = Type.Unknown;
		TargetAddress effective_address = TargetAddress.Null;
		Registers registers;
		bool is_conditional;
		bool is_ip_relative;
		byte[] code;

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
			Console.WriteLine ("GET EFFECTIVE ADDRESS: {0} {1} {2}", address, type, effective_address);

			return effective_address;
		}

		public override TrampolineType CheckTrampoline (TargetMemoryAccess memory,
								out TargetAddress trampoline)
		{
			trampoline = TargetAddress.Null;
			return TrampolineType.None;
		}

		public override bool CanInterpretInstruction {
			get { return false; }
		}

		public override bool InterpretInstruction (Inferior inferior)
		{
			return false;
		}
	}
}
