namespace Mono.Debugger.Server
{
	internal enum ServerObjectKind {
		Unknown = 0,
		Server,
		Inferior,
		Process,
		ExeReader,
		BreakpointManager
	};

	internal interface IServerObject
	{
		ServerObjectKind Kind {
			get;
		}

		int ID {
			get;
		}
	}
}
