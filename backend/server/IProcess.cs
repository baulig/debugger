using System;

namespace Mono.Debugger.Server
{
	internal interface IProcess : IServerObject
	{
		IExecutableReader MainReader {
			get;
		}

		void InitializeProcess (IInferior inferior);
	}
}
