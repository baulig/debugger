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

		public static ServerObject GetObjectByID (int id)
		{
			lock (object_hash) {
				if (object_hash.ContainsKey (id))
					return object_hash [id];

				return null;
			}
		}

		public static ServerObject GetObjectByID (int id, ServerObjectKind kind)
		{
			lock (object_hash) {
				if (!object_hash.ContainsKey (id))
					return null;

				var obj = object_hash [id];
				if (obj.Kind != kind)
					throw new ArgumentException ();
				return obj;
			}
		}

		public static ServerObject GetOrCreateObject (Connection connection, int id, ServerObjectKind kind)
		{
			lock (object_hash) {
				ServerObject obj;

				if (object_hash.ContainsKey (id)) {
					obj = object_hash [id];
					if (obj.Kind != kind)
						throw new ArgumentException ();

					return obj;
				}

				switch (kind) {
				case ServerObjectKind.Inferior:
					obj = new MdbInferior (connection, id);
					break;
				case ServerObjectKind.Process:
					obj = new MdbProcess (connection, id);
					break;
				case ServerObjectKind.ExeReader:
					obj = new MdbExeReader (connection, id);
					break;
				case ServerObjectKind.BreakpointManager:
					obj = new MdbBreakpointManager (connection, id);
					break;
				case ServerObjectKind.MonoRuntime:
					obj = new MonoRuntime (connection, id);
					break;
				default:
					throw new InvalidOperationException ();
				}

				// The ctor already inserts it into the hash.
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
