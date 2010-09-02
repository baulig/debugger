using System;
using System.Runtime.InteropServices;

namespace Mono.Debugger.Server
{
	internal enum NotificationType {
		InitializeManagedCode	= 1,
		InitializeCorlib,
		JitBreakpoint,
		InitializeThreadManager,
		AcquireGlobalThreadLock,
		ReleaseGlobalThreadLock,
		WrapperMain,
		MainExited,
		UnhandledException,
		ThrowException,
		HandleException,
		ThreadCreated,
		ThreadCleanup,
		GcThreadCreated,
		GcThreadExited,
		ReachedMain,
		FinalizeManagedCode,
		LoadModule,
		UnloadModule,
		DomainCreate,
		DomainUnload,
		ClassInitialized,
		InterruptionRequest,
		CreateAppDomain,
		UnloadAppDomain,

		OldTrampoline	= 256,
		Trampoline	= 512
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct MonoDebuggerInfo
	{
		// These constants must match up with those in mono/mono/metadata/mono-debug.h
		public const int  MinDynamicVersion = 80;
		public const int  MaxDynamicVersion = 81;
		public const long DynamicMagic      = 0x7aff65af4253d427;

		public int MajorVersion;
		public int MinorVersion;

		public TargetAddress InsertMethodBreakpoint;
		public TargetAddress InsertSourceBreakpoint;
		public TargetAddress RemoveBreakpoint;

		public TargetAddress LookupClass;
		public TargetAddress ClassGetStaticFieldData;
		public TargetAddress GetMethodSignature;
		public TargetAddress GetVirtualMethod;
		public TargetAddress GetBoxedObjectMethod;

		public TargetAddress CompileMethod;
		public TargetAddress RuntimeInvoke;
		public TargetAddress AbortRuntimeInvoke;

		public TargetAddress RunFinally;

		public TargetAddress InitCodeBuffer;
		public TargetAddress CreateString;

		public int MonoTrampolineNum;
		public TargetAddress MonoTrampolineCode;

		public TargetAddress SymbolTable;
		public int SymbolTableSize;

		public TargetAddress MonoMetadataInfo;

		public TargetAddress GenericInvocationFunc;

		public bool CheckRuntimeVersion (int major, int minor)
		{
			if (MajorVersion < major)
				return false;
			if (MajorVersion > major)
				return true;
			return MinorVersion >= minor;
		}

		public bool HasNewTrampolineNotification {
			get { return CheckRuntimeVersion (80, 2) || CheckRuntimeVersion (81, 4); }
		}

		public bool HasAbortRuntimeInvoke {
			get { return CheckRuntimeVersion (81, 5); }
		}

		public bool HasThreadAbortSignal {
			get { return CheckRuntimeVersion (81, 6); }
		}
	}

	internal interface IMonoRuntime : IServerObject
	{
		MonoDebuggerInfo GetDebuggerInfo ();
	}
}
