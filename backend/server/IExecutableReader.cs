namespace Mono.Debugger.Server
{
	internal interface IExecutableReader
	{
		long StartAddress {
			get;
		}

		long LookupSymbol (string name);

		string GetTargetName ();

		bool HasSection (string name);

		long GetSectionAddress (string name);

		byte[] GetSectionContents (string name);
	}
}
