using System;

namespace Mono.Debugger.Languages
{
	public abstract class TargetEnumType : TargetType
	{
		public TargetEnumType (Language language)
			: base (language, TargetObjectKind.Enum)
		{ }

		public abstract bool IsFlagsEnum {
			get;
		}

		public abstract TargetFieldInfo Value {
			get;
		}

		public abstract TargetFieldInfo[] Members {
			get;
		}

		internal TargetObject GetValue (TargetLocation location)
		{
			return Value.Type.GetObject (location);
		}

		protected override TargetObject DoGetObject (TargetLocation location)
		{
			return new TargetEnumObject (this, location);
		}
	}
}
