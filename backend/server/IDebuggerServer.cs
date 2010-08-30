namespace Mono.Debugger.Server
{
	internal interface IDebuggerServer
	{
		TargetInfo GetTargetInfo ();

		DebuggerServer.ServerType ServerType {
			get;
		}

		DebuggerServer.ArchTypeEnum Architecture {
			get;
		}

		DebuggerServer.ServerCapabilities Capabilities {
			get;
		}

		IInferior CreateInferior (IBreakpointManager bpm);

		IBreakpointManager CreateBreakpointManager ();

		IExecutableReader CreateExeReader (string filename);
	}
}
