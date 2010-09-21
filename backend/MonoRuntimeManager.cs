using System;
using System.Collections.Generic;

using Mono.Debugger.Server;
using Mono.Debugger.Backend.Mono;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Mono;

namespace Mono.Debugger.Backend
{
	internal delegate bool ManagedCallbackFunction (SingleSteppingEngine engine);

	internal class MonoRuntimeManager
	{
		Process process;
		IMonoRuntime runtime;

		MonoLanguageBackend csharp_language;
		MonoDebuggerInfo debugger_info;

		public MonoRuntimeManager (Process process, IMonoRuntime runtime)
		{
			this.process = process;
			this.runtime = runtime;

			debugger_info = runtime.GetDebuggerInfo ();
		}

		public void SetExtendedNotifications (Inferior inferior, NotificationType type, bool enable)
		{
			runtime.SetExtendedNotifications (inferior.InferiorHandle, type, enable);
		}

		public void ExecuteInstruction (Inferior inferior, byte[] instruction, bool update_ip)
		{
			runtime.ExecuteInstruction (inferior.InferiorHandle, instruction, update_ip);
		}

		public TargetAddress GetLMFAddress (Inferior inferior)
		{
			var lmf_addr = runtime.GetLMFAddress (inferior.InferiorHandle);
			if (lmf_addr == 0)
				return TargetAddress.Null;

			return new TargetAddress (inferior.AddressDomain, lmf_addr);
		}

		internal bool CanExecuteCode {
			get { return true; }
		}

		public MonoDebuggerInfo MonoDebuggerInfo {
			get { return debugger_info; }
		}

		internal void Detach (Inferior inferior)
		{
#if FIXME
			inferior.WriteAddress (debugger_info.ThreadVTablePtr, TargetAddress.Null);
			inferior.WriteAddress (debugger_info.EventHandler, TargetAddress.Null);
			inferior.WriteInteger (debugger_info.UsingMonoDebugger, 0);
#else
			throw new NotImplementedException ();
#endif
		}

		internal void AddManagedCallback (Inferior inferior, ManagedCallbackData data)
		{
#if FIXME
			inferior.WriteInteger (MonoDebuggerInfo.InterruptionRequest, 1);
			managed_callbacks.Enqueue (data);
#else
			throw new NotImplementedException ();
#endif
		}

		internal Queue<ManagedCallbackData> ClearManagedCallbacks (Inferior inferior)
		{
#if FIXME
			inferior.WriteInteger (MonoDebuggerInfo.InterruptionRequest, 0);
			Queue<ManagedCallbackData> retval = managed_callbacks;
			managed_callbacks = new Queue<ManagedCallbackData> ();
			return retval;
#else
			throw new NotImplementedException ();
#endif
		}

		internal bool HandleEvent (SingleSteppingEngine sse, Inferior inferior,
					   ref ServerEvent e, out bool resume_target)
		{
			if (e.Type != ServerEventType.Notification) {
				resume_target = false;
				return false;
			}

			NotificationType type = (NotificationType) e.Argument;

			Report.Debug (DebugFlags.EventLoop, "{0} received notification {1}: {2}",
				      sse, type, e);

			switch (type) {
			case NotificationType.InitializeThreadManager:
				csharp_language = inferior.Process.CreateMonoLanguage (debugger_info);
				if (sse.Process.IsAttached)
					csharp_language.InitializeAttach (inferior);
				else
					csharp_language.Initialize (inferior);
				resume_target = true;
				return true;

			case NotificationType.ReachedMain: {
				Inferior.StackFrame iframe = inferior.GetCurrentFrame (false);
				sse.SetMainReturnAddress (iframe.StackPointer);
				sse.Process.OnProcessReachedMainEvent ();
				resume_target = !sse.InitializeBreakpoints ();
				return true;
			}

			case NotificationType.MainExited:
				sse.SetMainReturnAddress (TargetAddress.Null);
				resume_target = true;
				return true;

			case NotificationType.UnhandledException:
				e = new ServerEvent (ServerEventType.UnhandledException, e.Sender, 0, e.Data1, e.Data2);
				resume_target = false;
				return false;

			case NotificationType.HandleException:
				e = new ServerEvent (ServerEventType.HandleException, e.Sender, 0, e.Data1, e.Data2);
				resume_target = false;
				return false;

			case NotificationType.ThrowException:
				e = new ServerEvent (ServerEventType.ThrowException, e.Sender, 0, e.Data1, e.Data2);
				resume_target = false;
				return false;

			case NotificationType.FinalizeManagedCode:
				csharp_language = null;
				resume_target = true;
				return true;

			case NotificationType.OldTrampoline:
			case NotificationType.Trampoline:
				resume_target = false;
				return false;

			default:
				TargetAddress data = new TargetAddress (
					inferior.AddressDomain, e.Data1);

				resume_target = csharp_language.Notification (
					sse, inferior, type, data, e.Data2);
				return true;
			}
		}
	}
}
