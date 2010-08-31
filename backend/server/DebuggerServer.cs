using System;
using System.IO;
using System.Net;
using System.Text;
using ST = System.Threading;
using System.Configuration;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.InteropServices;

using Mono.Debugger.Backend;
using Mono.Debugger.MdbServer;

namespace Mono.Debugger.Server
{
	internal class DebuggerServer : DebuggerMarshalByRefObject, IDisposable
	{
		IDebuggerServer server;

		protected DebuggerServer (Debugger debugger, IDebuggerServer server)
		{
			this.server = server;

			Debugger = debugger;
			ThreadManager = new ThreadManager (debugger, this);
		}

		public static DebuggerServer Connect (Debugger debugger, IPEndPoint endpoint)
		{
			var server = Connection.Connect (endpoint);

			return new DebuggerServer (debugger, server);
		}

		public Debugger Debugger {
			get; private set;
		}

		public ThreadManager ThreadManager {
			get; private set;
		}

		public ServerType Type {
			get { return server.ServerType; }
		}

		public ServerCapabilities Capabilities {
			get { return server.Capabilities; }
		}

		public ArchType ArchType {
			get { return server.ArchType; }
		}

		public IBreakpointManager BreakpointManager {
			get { return server.BreakpointManager; }
		}

		public IInferior Spawn (SingleSteppingEngine sse, string cwd, string[] argv, string[] envp,
					out IProcess process)
		{
			return server.Spawn (sse, cwd, argv, envp, out process);
		}

		public IInferior Attach (SingleSteppingEngine sse, int pid, out IProcess process)
		{
			return server.Attach (sse, pid, out process);
		}

		public ExecutableReader GetExecutableReader (OperatingSystemBackend os, TargetMemoryInfo memory,
							     string filename, TargetAddress base_address, bool is_loaded)
		{
			throw new NotImplementedException ();
		}

		public TargetInfo GetTargetInfo ()
		{
			return server.TargetInfo;
		}

		public MonoRuntimeHandle InitializeMonoRuntime (
			int address_size, long notification_address,
			long executable_code_buffer, int executable_code_buffer_size,
			long breakpoint_info, long breakpoint_info_index,
			int breakpoint_table_size)
		{
			throw new NotImplementedException ();
		}

		public void InitializeCodeBuffer (MonoRuntimeHandle runtime, long executable_code_buffer,
						  int executable_code_buffer_size)
		{
			throw new NotImplementedException ();
		}

		public void FinalizeMonoRuntime (MonoRuntimeHandle runtime)
		{
			throw new NotImplementedException ();
		}

		public Disassembler GetDisassembler ()
		{
			return new ServerDisassembler (this);
		}

                protected string DisassembleInsn (Inferior inferior, long address, out int insn_size)
                {
                        var handle = (IInferior) inferior.InferiorHandle;
                        return handle.DisassembleInstruction (address, out insn_size);
                }

		class ServerDisassembler : Disassembler
		{
			public readonly DebuggerServer Server;

			public ServerDisassembler (DebuggerServer server)
			{
				this.Server = server;
			}

			public override int GetInstructionSize (TargetMemoryAccess memory, TargetAddress address)
			{
				int insn_size;
				Server.DisassembleInsn ((Inferior) memory, address.Address, out insn_size);
				return insn_size;
			}

			public override AssemblerMethod DisassembleMethod (TargetMemoryAccess memory, Method method)
			{
				throw new NotImplementedException ();
			}

			public override AssemblerLine DisassembleInstruction (TargetMemoryAccess memory,
									      Method method, TargetAddress address)
			{
				int insn_size;
				var insn = Server.DisassembleInsn ((Inferior) memory, address.Address, out insn_size);
				return new AssemblerLine (null, address, (byte) insn_size, insn);
			}
		}

		//
		// IDisposable
		//

		protected void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("DebuggerServer");
		}

		private bool disposed = false;

		protected virtual void DoDispose ()
		{ }

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (!this.disposed) {
				// If this is a call to Dispose,
				// dispose all managed resources.
				this.disposed = true;

				// Release unmanaged resources
				lock (this) {
					DoDispose ();
				}
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~DebuggerServer ()
		{
			Dispose (false);
		}
	}
}

