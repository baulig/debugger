using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeStructObject : TargetClassObject
	{
		public new NativeStructType type;

		public NativeStructObject (NativeStructType type, TargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		public override TargetClassObject GetParentObject (TargetMemoryAccess target)
		{
			return null;
		}

		public override TargetClassObject GetCurrentObject (TargetMemoryAccess target)
		{
			return null;
		}

		public override TargetObject GetField (TargetMemoryAccess target, TargetFieldInfo field)
		{
			return type.GetField (target, Location, (NativeFieldInfo) field);
		}

		public override void SetField (TargetAccess target, TargetFieldInfo field,
					       TargetObject obj)
		{
			type.SetField (target, Location, (NativeFieldInfo) field, obj);
		}

		internal override long GetDynamicSize (TargetMemoryAccess target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		public override string Print (Thread target)
		{
			if (Location.HasAddress)
				return String.Format ("{0}", Location.GetAddress (target));
			else
				return String.Format ("{0}", Location);
		}
	}
}

