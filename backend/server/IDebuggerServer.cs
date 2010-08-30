using System;

namespace Mono.Debugger.Server
{
	internal enum ServerType
	{
		Unknown = 0,
		LinuxPTrace = 1,
		Darwin = 2,
		Windows = 3
	}

	internal enum ArchType
	{
		Unknown = 0,
		I386 = 1,
		X86_64 = 2,
		ARM = 3
	}

	[Flags]
	internal enum ServerCapabilities
	{
		None = 0,
		ThreadEvents = 1,
		CanDetachAny = 2,
		HasSignals = 4
	}

	internal interface IDebuggerServer
	{
		TargetInfo GetTargetInfo ();

		ServerType ServerType {
			get;
		}

		ArchType ArchType {
			get;
		}

		ServerCapabilities Capabilities {
			get;
		}

		IInferior CreateInferior (IBreakpointManager bpm);

		IBreakpointManager CreateBreakpointManager ();

		IExecutableReader CreateExeReader (string filename);
	}
}
