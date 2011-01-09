namespace Mono.Debugger.Server
{
	internal interface IProcess : IServerObject
	{
		IExecutableReader MainReader {
			get;
		}

		IMonoRuntime MonoRuntime {
			get;
		}

		void InitializeProcess (IInferior inferior);

		IInferior[] GetAllThreads ();

		IExecutableReader[] GetAllModules ();

		IInferior Spawn (string cwd, string[] argv, string[] envp);

		IInferior Attach (int pid);

		void Suspend (IInferior caller);

		void Resume (IInferior caller);
	}
}