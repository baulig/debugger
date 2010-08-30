namespace Mono.Debugger.Server
{
	internal interface IExecutableReader : IServerObject
	{
		string FileName {
			get;
		}

		long StartAddress {
			get;
		}

		string TargetName {
			get;
		}

		long LookupSymbol (string name);

		bool HasSection (string name);

		long GetSectionAddress (string name);

		byte[] GetSectionContents (string name);
	}
}
