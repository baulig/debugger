using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Native;
using Mono.Debugger.Languages.Mono;

namespace Mono.Debugger.Backend
{
	internal abstract class DebuggingFileReader : DebuggerMarshalByRefObject
	{
		protected OperatingSystemBackend OS {
			get;
			private set;
		}

		protected ExecutableReader NativeReader
		{
			get;
			private set;
		}

		protected Module Module
		{
			get;
			private set;
		}

		public string FileName
		{
			get { return NativeReader.FileName; }
		}

		protected DebuggingFileReader (OperatingSystemBackend os, ExecutableReader reader, Module module)
		{
			this.OS = os;
			this.NativeReader = reader;
			this.Module = module;
		}

		public abstract SourceFile[] Sources
		{
			get;
		}

		public abstract ISymbolTable SymbolTable
		{
			get;
		}

		public abstract MethodSource[] GetMethods (SourceFile file);

		public abstract MethodSource FindMethod (string name);
	}
}
