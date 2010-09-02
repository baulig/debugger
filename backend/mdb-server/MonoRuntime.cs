using System;

using Mono.Debugger.Server;
using Mono.Debugger.Backend;

namespace Mono.Debugger.MdbServer
{
	internal class MonoRuntime : ServerObject, IMonoRuntime
	{
		public MonoRuntime (Connection connection, int id)
			: base (connection, id, ServerObjectKind.MonoRuntime)
		{ }

		enum CmdMonoRuntime {
			GET_DEBUGGER_INFO = 1
		}

		public MonoDebuggerInfo GetDebuggerInfo ()
		{
			var reader = Connection.SendReceive (CommandSet.MONO_RUNTIME, (int) CmdMonoRuntime.GET_DEBUGGER_INFO, new Connection.PacketWriter ().WriteInt (ID));
			MonoDebuggerInfo info;

			info.MajorVersion = reader.ReadInt ();
			info.MinorVersion = reader.ReadInt ();

			info.InsertMethodBreakpoint = new TargetAddress (AddressDomain.Global, reader.ReadLong ());
			info.InsertSourceBreakpoint = new TargetAddress (AddressDomain.Global, reader.ReadLong ());
			info.RemoveBreakpoint = new TargetAddress (AddressDomain.Global, reader.ReadLong ());

			info.LookupClass = new TargetAddress (AddressDomain.Global, reader.ReadLong ());
			info.ClassGetStaticFieldData = new TargetAddress (AddressDomain.Global, reader.ReadLong ());
			info.GetMethodSignature = new TargetAddress (AddressDomain.Global, reader.ReadLong ());
			info.GetVirtualMethod = new TargetAddress (AddressDomain.Global, reader.ReadLong ());
			info.GetBoxedObjectMethod = new TargetAddress (AddressDomain.Global, reader.ReadLong ());

			info.CompileMethod = new TargetAddress (AddressDomain.Global, reader.ReadLong ());
			info.RuntimeInvoke = new TargetAddress (AddressDomain.Global, reader.ReadLong ());
			info.AbortRuntimeInvoke = new TargetAddress (AddressDomain.Global, reader.ReadLong ());
			info.RunFinally = new TargetAddress (AddressDomain.Global, reader.ReadLong ());

			info.InitCodeBuffer = new TargetAddress (AddressDomain.Global, reader.ReadLong ());
			info.CreateString = new TargetAddress (AddressDomain.Global, reader.ReadLong ());

			info.MonoTrampolineNum = reader.ReadInt ();
			info.MonoTrampolineCode = new TargetAddress (AddressDomain.Global, reader.ReadLong ());

			info.SymbolTable = new TargetAddress (AddressDomain.Global, reader.ReadLong ());
			info.SymbolTableSize = reader.ReadInt ();

			info.MonoMetadataInfo = new TargetAddress (AddressDomain.Global, reader.ReadLong ());

			info.GenericInvocationFunc = new TargetAddress (AddressDomain.Global, reader.ReadLong ());

			return info;
		}
	}
}
