using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoClassObject : TargetObject, ITargetClassObject
	{
		new MonoClassInfo type;

		public MonoClassObject (MonoClassInfo type, TargetLocation location)
			: base (type.Type, location)
		{
			this.type = type;
		}

		ITargetStructType ITargetStructObject.Type {
			get { return type.Type; }
		}

		ITargetClassType ITargetClassObject.Type {
			get { return type.Type; }
		}

		public ITargetClassObject Parent {
			get {
				if (!type.Type.HasParent)
					return null;

				return type.GetParentObject (Location);
			}
		}

		[Command]
		public ITargetObject GetField (int index)
		{
			return type.GetField (Location, index);
		}

		[Command]
		public void SetField (int index, ITargetObject obj)
		{
			type.SetField (Location, index, (TargetObject) obj);
		}

		internal override long GetDynamicSize (TargetBlob blob, TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
