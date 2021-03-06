using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeStringType : NativeFundamentalType
	{
		static int max_string_length = 100;

		public NativeStringType (Language language, int size)
			: base (language, "char *", FundamentalKind.String, size)
		{ }

		public override bool IsByRef {
			get {
				return true;
			}
		}

		public static int MaximumStringLength {
			get {
				return max_string_length;
			}

			set {
				max_string_length = value;
			}
		}

		protected override TargetObject DoGetObject (TargetMemoryAccess target, TargetLocation location)
		{
			return new NativeStringObject (this, location);
		}
	}
}
