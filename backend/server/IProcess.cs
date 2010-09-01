namespace Mono.Debugger.Server
{
	internal interface IProcess : IServerObject
	{
		IExecutableReader MainReader {
			get;
		}

		void InitializeProcess (IInferior inferior);

		IInferior Spawn (string cwd, string[] argv, string[] envp);

		IInferior Attach (int pid);
	}
}
