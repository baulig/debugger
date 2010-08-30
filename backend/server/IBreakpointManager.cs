namespace Mono.Debugger.Server
{
	internal interface IBreakpointManager
	{
		int LookupBreakpointByAddr (long address, out bool enabled);

		bool LookupBreakpointById (int id, out bool enabled);
	}
}
