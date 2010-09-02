namespace Mono.Debugger.Server
{
	internal enum ServerObjectKind {
		None = 0,
		Server,
		Inferior,
		Process,
		ExeReader,
		BreakpointManager,
		MonoRuntime
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
