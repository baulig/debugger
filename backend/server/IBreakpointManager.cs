namespace Mono.Debugger.Server
{
	internal interface IBreakpointManager : IServerObject
	{
		int LookupBreakpointByAddr (long address, out bool enabled);

		bool LookupBreakpointById (int id, out bool enabled);
	}
}
