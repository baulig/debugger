using System;

using Mono.Debugger.Backend;

namespace Mono.Debugger.Architectures
{
	internal abstract class Instruction : DebuggerMarshalByRefObject
	{
		public enum Type
		{
			Unknown,
			Interpretable,
			ConditionalJump,
			IndirectCall,
			Call,
			IndirectJump,
			Jump,
			Ret
		}

		public enum TrampolineType
		{
			None,
			NativeTrampolineStart,
			NativeTrampoline,
			MonoTrampoline,
			DelegateInvoke
		}

		public abstract Opcodes Opcodes {
			get;
		}

		public abstract TargetAddress Address {
			get;
		}

		public abstract Type InstructionType {
			get;
		}

		public abstract bool IsIpRelative {
			get;
		}

		public bool IsCall {
			get {
				return (InstructionType == Type.Call) ||
					(InstructionType == Type.IndirectCall);
			}
		}

		public abstract bool HasInstructionSize {
			get;
		}

		public abstract int InstructionSize {
			get;
		}

		public abstract byte[] Code {
			get;
		}

		protected Process Process {
			get { return Opcodes.Architecture.Process; }
		}

		public abstract TargetAddress GetEffectiveAddress (TargetMemoryAccess memory);

		public TrampolineType CheckTrampoline (TargetMemoryAccess memory,
						       out TargetAddress trampoline)
		{
			if (InstructionType == Type.Call) {
				TargetAddress target = GetEffectiveAddress (memory);
				if (target.IsNull) {
					trampoline = TargetAddress.Null;
					return TrampolineType.None;
				}

				bool is_start;

				if (Process.OperatingSystem.GetTrampoline (memory, target, out trampoline, out is_start)) {
					target = trampoline;
					return is_start ?
						TrampolineType.NativeTrampolineStart :
						TrampolineType.NativeTrampoline;
				}
			}

			if ((InstructionType != Type.Call) && (InstructionType != Type.IndirectCall)) {
				trampoline = TargetAddress.Null;
				return TrampolineType.None;
			}

			if (Process.IsManagedApplication) {
				TargetAddress target = GetEffectiveAddress (memory);
				if (target.IsNull) {
					trampoline = TargetAddress.Null;
					return TrampolineType.None;
				}

				if (Process.MonoLanguage.IsDelegateTrampoline (target)) {
					trampoline = target;
					return TrampolineType.DelegateInvoke;
				}

				trampoline = Opcodes.Architecture.GetMonoTrampoline (memory, target);
				if (!trampoline.IsNull)
					return TrampolineType.MonoTrampoline;
			}

			trampoline = TargetAddress.Null;
			return TrampolineType.None;
		}

		public abstract bool CanInterpretInstruction {
			get;
		}

		public abstract bool InterpretInstruction (Inferior inferior);
	}
}
