using System;
using System.Runtime.InteropServices;

namespace Mono.Debugger.Languages
{
	public enum FundamentalKind
	{
		Unknown,
		Object,
		Boolean,
		Char,
		SByte,
		Byte,
		Int16,
		UInt16,
		Int32,
		UInt32,
		Int64,
		UInt64,
		Single,
		Double,
		String,
		IntPtr,
		UIntPtr,
		Decimal
	}

	public abstract class TargetFundamentalType : TargetType
	{
		protected readonly int size;
		protected readonly FundamentalKind fundamental_kind;
		protected readonly string name;

		public TargetFundamentalType (Language language, string name,
					      FundamentalKind kind, int size)
			: base (language, TargetObjectKind.Fundamental)
		{
			this.name = name;
			this.fundamental_kind = kind;
			this.size = size;
		}

		public override string Name {
			get { return name; }
		}

		public override bool ContainsGenericParameters {
			get { return false; }
		}

		public override bool IsByRef {
			get {
				switch (fundamental_kind) {
				case FundamentalKind.Object:
				case FundamentalKind.String:
				case FundamentalKind.IntPtr:
				case FundamentalKind.UIntPtr:
					return true;

				default:
					return false;
				}
			}
		}

		public FundamentalKind FundamentalKind {
			get {
				return fundamental_kind;
			}
		}

		public virtual byte[] CreateObject (object obj)
		{
			switch (fundamental_kind) {
			case FundamentalKind.Boolean:
				return BitConverter.GetBytes (Convert.ToBoolean (obj));

			case FundamentalKind.Char:
				return BitConverter.GetBytes (Convert.ToChar (obj));

			case FundamentalKind.SByte:
				return BitConverter.GetBytes (Convert.ToSByte (obj));

			case FundamentalKind.Byte:
				return BitConverter.GetBytes (Convert.ToByte (obj));

			case FundamentalKind.Int16:
				return BitConverter.GetBytes (Convert.ToInt16 (obj));

			case FundamentalKind.UInt16:
				return BitConverter.GetBytes (Convert.ToUInt16 (obj));

			case FundamentalKind.Int32:
				return BitConverter.GetBytes (Convert.ToInt32 (obj));

			case FundamentalKind.UInt32:
				return BitConverter.GetBytes (Convert.ToUInt32 (obj));

			case FundamentalKind.Int64:
				return BitConverter.GetBytes (Convert.ToInt64 (obj));

			case FundamentalKind.UInt64:
				return BitConverter.GetBytes (Convert.ToUInt64 (obj));

			case FundamentalKind.Single:
				return BitConverter.GetBytes (Convert.ToSingle (obj));

			case FundamentalKind.Double:
				return BitConverter.GetBytes (Convert.ToDouble (obj));

			case FundamentalKind.IntPtr:
			case FundamentalKind.UIntPtr: {
				IntPtr ptr = (IntPtr) obj;
				if (IntPtr.Size == 4)
					return BitConverter.GetBytes (ptr.ToInt32 ());
				else
					return BitConverter.GetBytes (ptr.ToInt64 ());
			}

			case FundamentalKind.Decimal: {
				IntPtr ptr = IntPtr.Zero;
				try {
					int size = Marshal.SizeOf (typeof (decimal));

					byte[] data = new byte [size];
					ptr = Marshal.AllocHGlobal (size);
					Marshal.StructureToPtr (obj, ptr, false);

					Marshal.Copy (ptr, data, 0, size);
					return data;
				} finally {
					if (ptr != IntPtr.Zero)
						Marshal.FreeHGlobal (ptr);
				}
			}

			default:
				throw new InvalidOperationException ();
			}
		}

		internal virtual TargetFundamentalObject CreateInstance (Thread target, object obj)
		{
			TargetBlob blob = new TargetBlob (CreateObject (obj), target.TargetMemoryInfo);
			TargetLocation location = new ClientSuppliedTargetLocation (blob);
			return new TargetFundamentalObject (this, location);
		}

		public override bool HasFixedSize {
			get { return FundamentalKind != FundamentalKind.String; }
		}

		public override int Size {
			get { return size; }
		}

		protected override TargetObject DoGetObject (TargetMemoryAccess target, TargetLocation location)
		{
			return new TargetFundamentalObject (this, location);
		}
	}
}
