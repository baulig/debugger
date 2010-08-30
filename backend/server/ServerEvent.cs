using System;

namespace Mono.Debugger.Server
{
	internal enum ServerEventType
	{
		None = 0,
		Exited,
		Stopped,
		Signaled,
		Callback,
		CallbackCompleted,
		Breakpoint,
		MemoryChanged,
		ThreadCreated,
		Forked,
		Execd,
		CalledExit,
		Notification,
		Interrupted,
		RuntimeInvokeDone,

		UnknownError = 0x40,
		InternalError,

		DllLoaded = 0x50,

		UnhandledException = 0x70,
		ThrowException,
		HandleException
	}

	internal sealed class ServerEvent
	{
		public ServerEventType Type {
			get; private set;
		}

		public IServerObject Sender {
			get; private set;
		}

		public long Argument {
			get; private set;
		}

		public long Data1 {
			get; private set;
		}

		public long Data2 {
			get; private set;
		}

		public byte[] CallbackData {
			get; private set;
		}

		public ServerEvent (ServerEventType type, IServerObject sender, long arg, long data1, long data2)
		{
			this.Type = type;
			this.Sender = sender;
			this.Argument = arg;
			this.Data1 = data1;
			this.Data2 = data2;
		}

		public ServerEvent (ServerEventType type, IServerObject sender, long arg, long data1, long data2,
				    byte[] callback_data)
			: this (type, sender, arg, data1, data2)
		{
			this.CallbackData = callback_data;
		}

		public override string ToString ()
		{
			return String.Format ("ServerEvent ({0}:{1}:{2:x}:{3:x})",
					      Type, Argument, Data1, Data2);
		}
	}
}
