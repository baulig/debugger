using System;
using System.Collections.Generic;

using Mono.Debugger.Server;

namespace Mono.Debugger.MdbServer
{
	internal abstract class ServerObject : IServerObject, IDisposable
	{
		public Connection Connection {
			get; private set;
		}

		public ServerObjectKind Kind {
			get; private set;
		}

		public int ID {
			get; private set;
		}

		internal abstract void HandleEvent (ServerEvent e);

		public static ServerObject GetObjectByID (int id)
		{
			lock (object_hash) {
				return object_hash [id];
			}
		}

		public static ServerObject GetObjectByID (int id, ServerObjectKind kind)
		{
			lock (object_hash) {
				var obj = object_hash [id];
				if (obj.Kind != kind)
					throw new ArgumentException ();
				return obj;
			}
		}

		static int next_id;
		static Dictionary<int, ServerObject> object_hash;

		static ServerObject ()
		{
			object_hash = new Dictionary<int, ServerObject> ();
		}

		protected ServerObject (Connection connection, int id, ServerObjectKind kind)
		{
			this.Connection = connection;
			this.Kind = kind;
			this.ID = id;

			lock (object_hash) {
				object_hash.Add (ID, this);
			}
		}

		void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			lock (object_hash) {
				object_hash.Remove (ID);
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			GC.SuppressFinalize (this);
		}

		~ServerObject ()
		{
			Dispose (false);
		}
	}
}
