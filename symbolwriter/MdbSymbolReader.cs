using System;
using System.Text;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.CompilerServices.SymbolWriter;
using NDesk.Options;
using System.Linq;

namespace Mono.Debugger.SymbolWriter
{
	public class MdbSymbolReader
	{
		public readonly Cecil.AssemblyDefinition Assembly;
		public readonly MonoSymbolFile File;

		public static bool Verbose {
			get; set;
		}

		public static bool Verify {
			get; set;
		}

		public static bool ShowSources {
			get; set;
		}

		public static bool ShowGuid {
			get; set;
		}

		static Dictionary<int,bool> sources = new Dictionary<int,bool> ();
		public static Dictionary<int,bool> Sources {
			get { return sources; }
		}

		public static bool ShowCompileUnits {
			get; set;
		}

		static Dictionary<int,bool> comp_units = new Dictionary<int,bool> ();
		public static Dictionary<int,bool> CompileUnits {
			get { return comp_units; }
		}

		public static bool ShowMethods {
			get; set;
		}

		public static bool ShowLocals {
			get; set;
		}

		static Dictionary<int,bool> methods = new Dictionary<int,bool> ();
		public static Dictionary<int,bool> Methods {
			get { return methods; }
		}

		public static bool ShowLineNumberTables {
			get; set;
		}

		static Dictionary<int,bool> line_number_tables = new Dictionary<int,bool> ();
		public static Dictionary<int,bool> LineNumberTables {
			get { return line_number_tables; }
		}

		public MdbSymbolReader (Cecil.AssemblyDefinition asm, MonoSymbolFile file)
		{
			this.Assembly = asm;
			this.File = file;
		}

		protected static void Message (string format, params object[] args)
		{
			Console.WriteLine (format, args);
		}

		protected static void Debug (string format, params object[] args)
		{
			if (Verbose)
				Console.WriteLine (format, args);
		}

		public void Read ()
		{
			Debug ("Reading {0}, version {1}.{2}.", File.FileName, File.MajorVersion,
			       File.MinorVersion);

			try {
				ReadCompileUnits ();
			} catch (MonoSymbolFileException) {
				throw;
			} catch (Exception ex) {
				Message ("Failed to read compile units: {0}", ex);
				throw;
			}

			try {
				ReadSourceFiles ();
			} catch (MonoSymbolFileException) {
				throw;
			} catch (Exception ex) {
				Message ("Failed to read source files: {0}", ex);
				throw;
			}

			try {
				ReadAnonymousScopes ();
			} catch (MonoSymbolFileException) {
				throw;
			} catch (Exception ex) {
				Message ("Failed to read anonymous scopes: {0}", ex);
				throw;
			}

			try {
				ReadMethods ();
			} catch (MonoSymbolFileException) {
				throw;
			} catch (Exception ex) {
				Message ("Failed to read methods: {0}", ex);
				throw;
			}

			Message ("Symbol file {0} checked ok.", File.FileName);
		}

		public void PrintGuid ()
		{
			Message ("Reading {0}, version {1}.{2}, Guid {3}.", File.FileName, File.MajorVersion,
				 File.MinorVersion, File.Guid);
		}

		public void PrintSources ()
		{
			Message ("Reading {0}, version {1}.{2}, Guid {3}.", File.FileName, File.MajorVersion,
				 File.MinorVersion, File.Guid);

			foreach (SourceFileEntry file in File.Sources) {
				if (Sources.Count > 0) {
					if (!Sources.ContainsKey (file.Index))
						continue;
				}

				Message ("File {0}{2}: {1}", file.Index, file.FileName,
					 file.AutoGenerated ? " (auto-generated)" : "");
			}
		}

		public void PrintCompileUnits ()
		{
			Message ("Reading {0}, version {1}.{2}.", File.FileName, File.MajorVersion,
				 File.MinorVersion);

			for (int i = 0; i < File.CompileUnitCount; i++) {
				if (CompileUnits.Count > 0) {
					if (!CompileUnits.ContainsKey (i + 1))
						continue;
				}

				CompileUnitEntry unit = File.GetCompileUnit (i + 1);
				SourceFileEntry file = unit.SourceFile;

				Message ("Compile unit {0}: ({1}) {2}", unit.Index, file.Index, file.FileName);

				foreach (SourceFileEntry include in unit.IncludeFiles)
					Message ("  Include: ({0}) {1}", include.Index, include.FileName);
			}
		}

		public void PrintMethods (bool include_locals)
		{
			Message ("Reading {0}, version {1}.{2}.", File.FileName, File.MajorVersion,
				 File.MinorVersion);

			for (int i = 0; i < File.MethodCount; i++) {
				if (Methods.Count > 0) {
					if (!Methods.ContainsKey (i + 1))
						continue;
				}

				MethodEntry method = File.GetMethod (i + 1);
				if (method == null)
					throw new MonoSymbolFileException ("Cannot get method {0}.", i+1);

				Cecil.MethodDefinition mdef = (Cecil.MethodDefinition) Assembly.MainModule.LookupByToken (
					Cecil.Metadata.TokenType.Method, method.Token & 0xffffff);
				if ((mdef == null) || (mdef.Body == null))
					throw new MonoSymbolFileException ("Method {0} (token {1:x}) not found in assembly.",
									   method.Index, method.Token);

				Message ("Method {0} ({1:x}) - {2}", method.Index, method.Token, GetMethodName (mdef));

				if (include_locals)
					PrintLocals (method);
			}
		}

		public void PrintLocals (MethodEntry method)
		{
			CodeBlockEntry[] blocks = method.GetCodeBlocks () ?? new CodeBlockEntry [0];
			foreach (CodeBlockEntry block in blocks) {
				Message ("  CodeBlock {0}", block);
			}

			LocalVariableEntry[] locals = method.GetLocals () ?? new LocalVariableEntry [0];
			foreach (LocalVariableEntry local in locals) {
				Message ("  Local {0}", local);
			}
		}

		public void PrintLineNumberTables ()
		{
			Message ("Reading {0}, version {1}.{2}.", File.FileName, File.MajorVersion,
				 File.MinorVersion);

			for (int i = 0; i < File.MethodCount; i++) {
				if (LineNumberTables.Count > 0) {
					if (!LineNumberTables.ContainsKey (i + 1))
						continue;
				}

				MethodEntry method = File.GetMethod (i + 1);
				if (method == null)
					throw new MonoSymbolFileException ("Cannot get method {0}.", i+1);

				Cecil.MethodDefinition mdef = (Cecil.MethodDefinition) Assembly.MainModule.LookupByToken (
					Cecil.Metadata.TokenType.Method, method.Token & 0xffffff);
				if ((mdef == null) || (mdef.Body == null))
					throw new MonoSymbolFileException ("Method {0} (token {1:x}) not found in assembly.",
									   method.Index, method.Token);

				string name = String.Format ("{0} ({1})", method.Index, GetMethodName (mdef));
				Message ("Method {0} - {1}", method.Index, GetMethodName (mdef));

				LineNumberTable lnt = method.GetLineNumberTable ();
				if (lnt == null)
					throw new MonoSymbolFileException ("Cannot get LNT from method {0}.", name);

				if (lnt.LineNumbers == null)
					throw new MonoSymbolFileException ("Cannot get LNT from method {0}.", name);

				Dictionary<int,bool> seen_files = new Dictionary<int,bool> ();

				LineNumberEntry start, end;
				if (lnt.GetMethodBounds (out start, out end))
					Message ("  Bounds: {0} {1}", start, end);
				foreach (LineNumberEntry line in lnt.LineNumbers) {
					if (!line.IsHidden && !seen_files.ContainsKey (line.File)) {
						SourceFileEntry file = File.GetSourceFile (line.File);
						Message ("  File {0}{2}: {1}", file.Index, file.FileName,
							 file.AutoGenerated ? " (auto-generated)" : "");
						seen_files.Add (line.File, true);
					}

					string range = "";
					if (line.SourceRange != null) {
						SourceRangeEntry sre = (SourceRangeEntry) line.SourceRange;
						range = String.Format (" - {0} {1} {2} {3}", sre.StartLine, sre.EndLine,
								       sre.StartColumn, sre.EndColumn);
					}

					Message ("    Line {0}:{1}:{2:x}{3}{4}", line.File, line.Row, line.Offset,
						 line.IsHidden ? " (hidden)" : "", range);
				}
			}
		}

		protected void ReadCompileUnits ()
		{
			for (int i = 0; i < File.CompileUnitCount; i++) {
				CheckCompileUnit (File.GetCompileUnit (i + 1));
			}
			Debug ("Checked {0} compilation units.", File.CompileUnitCount);
		}

		protected void ReadSourceFiles ()
		{
			for (int i = 0; i < File.SourceCount; i++) {
				SourceFileEntry source = File.GetSourceFile (i + 1);
				if (source == null)
					throw new MonoSymbolFileException ("Cannot get source file {0}.", i+1);
				Debug ("  [{0}] - {1}", source, source.FileName);
			}
			Debug ("Checked {0} source files.", File.SourceCount);
		}

		protected void CheckCompileUnit (CompileUnitEntry unit)
		{
			SourceFileEntry file = unit.SourceFile;
			SourceFileEntry file2 = File.GetSourceFile (file.Index);
			if ((file2 == null) || (file != file2))
				throw new MonoSymbolFileException (
					"Invalid source file reference in compile unit {0}.", unit.Index);

			Debug ("  Compile unit {0}: {1} {2} {3}", unit.Index, file, file2, file == file2);

			if (unit.Namespaces == null)
				throw new MonoSymbolFileException (
					"Invalid name space table in compile unit {0}.", unit.Index);
		}

		protected void ReadAnonymousScopes ()
		{
			var seen_scopes = new Dictionary<int,AnonymousScopeEntry> ();

			foreach (AnonymousScopeEntry scope in File.AnonymousScopes) {
				if (seen_scopes.ContainsKey (scope.ID))
					throw new MonoSymbolFileException ("Duplicate anonymous scope {0}.", scope.ID);
				seen_scopes.Add (scope.ID, scope);
				CheckAnonymousScope (scope);
			}
			Debug ("Checked {0} anonymous scopes.", File.AnonymousScopeCount);
		}

		protected void CheckAnonymousScope (AnonymousScopeEntry scope)
		{
			Debug ("Anonymous scope: {0}", scope);
			foreach (CapturedScope captured in scope.CapturedScopes) {
				if (File.GetAnonymousScope (captured.Scope) == null)
					throw new MonoSymbolFileException ("Anonymous scope {0} has invalid captured scopes.", scope.ID);
			}
		}

		protected void ReadMethods ()
		{
			for (int i = 0; i < File.MethodCount; i++) {
				MethodEntry method = File.GetMethod (i + 1);
				if (method == null)
					throw new MonoSymbolFileException ("Cannot get method {0}.", i+1);
				CheckMethod (method);
			}
			Debug ("Checked {0} methods.", File.MethodCount);
		}

		#region Helper methods from the debugger

		protected static string GetTypeSignature (Cecil.TypeReference t)
		{
			Cecil.ReferenceType rtype = t as Cecil.ReferenceType;
			if (rtype != null)
				return GetTypeSignature (rtype.ElementType) + "&";

			Cecil.ArrayType atype = t as Cecil.ArrayType;
			if (atype != null) {
				string etype = GetTypeSignature (atype.ElementType);
				if (atype.Rank > 1)
					return String.Format ("{0}[{1}]", etype, atype.Rank);
				else
					return etype + "[]";
			}

			switch (t.FullName) {
			case "System.Char":	return "char";
			case "System.Boolean":	return "bool";
			case "System.Byte":	return "byte";
			case "System.SByte":	return "sbyte";
			case "System.Int16":	return "int16";
			case "System.UInt16":	return "uint16";
			case "System.Int32":	return "int";
			case "System.UInt32":	return "uint";
			case "System.Int64":	return "long";
			case "System.UInt64":	return "ulong";
			case "System.Single":	return "single";
			case "System.Double":	return "double";
			case "System.String":	return "string";
			case "System.Object":	return "object";
			default:		return RemoveGenericArity (t.FullName);
			}
		}

		internal static string GetMethodSignature (Cecil.MethodDefinition mdef)
		{
			StringBuilder sb = new StringBuilder ("(");
			bool first = true;
			foreach (Cecil.ParameterDefinition p in mdef.Parameters) {
				if (first)
					first = false;
				else
					sb.Append (", ");
				sb.Append (GetTypeSignature (p.ParameterType).Replace ('+','/'));
			}
			sb.Append (")");
			return sb.ToString ();
		}

		internal static string RemoveGenericArity (string name)
		{
			int start = 0;
			StringBuilder sb = null;
			do {
				int pos = name.IndexOf ('`', start);
				if (pos < 0) {
					if (start == 0)
						return name;

					sb.Append (name.Substring (start));
					break;
				}

				if (sb == null)
					sb = new StringBuilder ();
				sb.Append (name.Substring (start, pos-start));

				pos++;
				while ((pos < name.Length) && Char.IsNumber (name [pos]))
					pos++;

				start = pos;
			} while (start < name.Length);

			return sb.ToString ();
		}

		internal static string GetMethodName (Cecil.MethodDefinition mdef)
		{
			StringBuilder sb = new StringBuilder (GetTypeSignature (mdef.DeclaringType));
			if (mdef.DeclaringType.GenericParameters.Count > 0) {
				sb.Append ('<');
				bool first = true;
				foreach (Cecil.GenericParameter p in mdef.DeclaringType.GenericParameters) {
					if (first)
						first = false;
					else
						sb.Append (',');
					sb.Append (p.Name);
				}
				sb.Append ('>');
			}
			sb.Append ('.');
			sb.Append (mdef.Name);
			if (mdef.GenericParameters.Count > 0) {
				sb.Append ('<');
				bool first = true;
				foreach (Cecil.GenericParameter p in mdef.GenericParameters) {
					if (first)
						first = false;
					else
						sb.Append (',');
					sb.Append (p.Name);
				}
				sb.Append ('>');
			}
			sb.Append (GetMethodSignature (mdef));
			return sb.ToString ();
		}

		#endregion

		protected void CheckMethod (MethodEntry method)
		{
			Cecil.MethodDefinition mdef = (Cecil.MethodDefinition) Assembly.MainModule.LookupByToken (
				Cecil.Metadata.TokenType.Method, method.Token & 0xffffff);
			if ((mdef == null) || (mdef.Body == null))
				throw new MonoSymbolFileException ("Method {0} (token {1:x}) not found in assembly.",
								   method.Index, method.Token);

			string name = String.Format ("{0} ({1})", method.Index, GetMethodName (mdef));
			Debug ("[Method {0} - {1} - {2}]", method.Index, method.CompileUnit.SourceFile.FileName,
			       GetMethodName (mdef));

			LineNumberTable lnt = method.GetLineNumberTable ();
			if (lnt == null)
				throw new MonoSymbolFileException ("Cannot get LNT from method {0}.", name);

			if (lnt.LineNumbers == null)
				throw new MonoSymbolFileException ("Cannot get LNT from method {0}.", name);
			LineNumberEntry start, end;
			if (lnt.GetMethodBounds (out start, out end))
				Debug ("  Bounds: {0} {1}", start, end);
			foreach (LineNumberEntry line in lnt.LineNumbers)
				Debug ("    Line: {0}", line);

			CodeBlockEntry[] blocks = method.GetCodeBlocks () ?? new CodeBlockEntry [0];
			foreach (CodeBlockEntry block in blocks) {
				if ((block.Parent >= 0) && (block.Parent >= blocks.Length))
					throw new MonoSymbolFileException (
						"Code block {0} in method {1} has invalid parent index {2} (valid is 0..{3}).",
						block, name, block.Parent, blocks.Length);
			}

			LocalVariableEntry[] locals = method.GetLocals () ?? new LocalVariableEntry [0];
			foreach (LocalVariableEntry local in locals) {
				if ((local.BlockIndex < 0) || ((local.BlockIndex > 0) && (local.BlockIndex > blocks.Length)))
					throw new MonoSymbolFileException (
						"Local variable {0} in method {1} has invalid block index {2} (valid is 0..{3}).",
						local, name, local.BlockIndex, blocks.Length);

				Debug ("  Local: {0}", local);
			}

			int num_locals = mdef.Body.Variables.Count;

			ScopeVariable[] scope_vars = method.GetScopeVariables () ?? new ScopeVariable [0];
			foreach (ScopeVariable var in scope_vars) {
				Debug ("  Scope var: {0}", var);
				if ((mdef.IsStatic) && (var.Index < 0))
					throw new MonoSymbolFileException (
						"Method {0} has invalid scope variable {1} (referencing `this' in static method).",
						name, var);
				if ((var.Index >= 0) && (var.Index >= num_locals))
					throw new MonoSymbolFileException (
						"Method {0} has invalid scope variable {1} (index out of bounds: {2} / {3}).",
						name, var, var.Index, num_locals);
				if ((var.Scope > 0) && (File.GetAnonymousScope (var.Scope) == null))
					throw new MonoSymbolFileException (
						"Method {0} has invalid scope variable {1} (can't find scope {2}).",
						name, var, var.Scope);
			}
		}

		static int Main (string[] args)
		{
			Verify = true;
			var p = new OptionSet () {
				{ "verbose", v => Verbose = v != null },
				{ "guid", v => ShowGuid = v != null },
				{ "sources:", v => {
					if (v != null)
						sources.Add (Int32.Parse (v), true);
					ShowSources = true;
				} },
				{ "compile-units:", v => {
					if (v != null)
						comp_units.Add (Int32.Parse (v), true);
					ShowCompileUnits = true;
				} },
				{ "methods:", v => {
					if (v != null)
						methods.Add (Int32.Parse (v), true);
					ShowMethods = true;
				} },
				{ "lnt:", v => {
					if (v != null)
						line_number_tables.Add (Int32.Parse (v), true);
					ShowLineNumberTables = true;
				} },
				{ "locals:", v => {
					if (v != null)
						methods.Add (Int32.Parse (v), true);
					ShowLocals = true;
				} },
			};
			List<string> extra = p.Parse (args);

			if (ShowGuid || ShowSources || ShowCompileUnits || ShowMethods || ShowLineNumberTables || ShowLocals)
				Verify = false;

			bool fail = false;
			foreach (string file in extra) {
				int ret = ProcessFile (file);
				if (ret < 0)
					fail = true;
			}
			return fail ? -1 : 0;
		}

		static int ProcessFile (string filename)
		{
			MonoSymbolFile file;
			Mono.Cecil.AssemblyDefinition asm;

			try {
				if (filename.EndsWith (".mdb")) {
					if (ShowGuid) {
						file = MonoSymbolFile.ReadSymbolFile (filename);
						Message ("Reading {0}, version {1}.{2}, guid {3}.", file.FileName, file.MajorVersion,
							 file.MinorVersion, file.Guid);
						return 0;
					}

					filename = filename.Substring (0, filename.Length-4);
				}

				asm = Mono.Cecil.AssemblyFactory.GetAssembly (filename);
				file = MonoSymbolFile.ReadSymbolFile (asm, filename + ".mdb");
			} catch (Exception ex) {
				Console.WriteLine ("Can't read {0}: {1}", filename, ex);
				return -1;
			}

			MdbSymbolReader reader = new MdbSymbolReader (asm, file);

			try {
				if (ShowGuid)
					reader.PrintGuid ();
				if (ShowSources)
					reader.PrintSources ();
				if (ShowCompileUnits)
					reader.PrintCompileUnits ();
				if (ShowMethods)
					reader.PrintMethods (false);
				if (ShowLineNumberTables)
					reader.PrintLineNumberTables ();
				if (ShowLocals)
					reader.PrintMethods (true);
				if (Verify)
					reader.Read ();
			} catch (MonoSymbolFileException ex) {
				Console.WriteLine ("Can't read {0}: {1}", filename, ex.Message);
				return -1;
			} catch (Exception ex) {
				Console.WriteLine ("Can't read {0}: {1}", filename, ex);
				return -1;
			}

			return 0;
		}
	}
}
