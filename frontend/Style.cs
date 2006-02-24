using System;
using System.Text;
using System.Collections;
using System.Globalization;
using Mono.Debugger;
using Mono.Debugger.Languages;

namespace Mono.Debugger.Frontend
{
	[Serializable]
	public class StyleEmacs : StyleCLI
	{
		public StyleEmacs (Interpreter interpreter)
			: base (interpreter)
		{ }

		public override string Name {
			get {
				return "emacs";
			}
		}

		public override void TargetStopped (ScriptingContext context, StackFrame frame,
						    AssemblerLine current_insn)
		{
			if (frame == null)
				return;

			if (frame != null && frame.SourceAddress != null)
				Console.WriteLine ("\x1A\x1A{0}:{1}:beg:{2}",
						   frame.SourceAddress.Name, "55" /* XXX */,
						   "0x80594d8" /* XXX */);
		}
	}

	[Serializable]
	public class StyleCLI : StyleBase
	{
		public StyleCLI (Interpreter interpreter)
			: base (interpreter)
		{ }

		bool native;

		public override string Name {
			get {
				return "native";
			}
		}

		public override bool IsNative {
			get { return native; }
			set { native = value; }
		}

		public override void Reset ()
		{
			IsNative = false;
		}

		public override void PrintFrame (ScriptingContext context, StackFrame frame)
		{
			context.Print (frame);
			bool native = false;
			if (!PrintSource (context, frame))
				native = true;
			if (native) {
				AssemblerLine insn = frame.TargetAccess.DisassembleInstruction (
					frame.Method, frame.TargetAddress);

				if (insn != null)
					context.PrintInstruction (insn);
				else
					context.Error ("Cannot disassemble instruction at address {0}.",
						       frame.TargetAddress);
			}
		}

		public override void TargetStopped (ScriptingContext context, StackFrame frame,
						    AssemblerLine current_insn)
		{
			if (frame != null) {
				if (!PrintSource (context, frame))
					native = true;
			}
			if (native && (current_insn != null))
				context.PrintInstruction (current_insn);
		}

		public override void UnhandledException (ScriptingContext context,
							 StackFrame frame, AssemblerLine insn)
		{
			TargetStopped (context, frame, insn);
		}

		protected bool PrintSource (ScriptingContext context, StackFrame frame)
		{
			SourceAddress location = frame.SourceAddress;
			if (location == null)
				return false;

			Method method = frame.Method;
			if ((method == null) || !method.HasSource || (method.Source == null))
				return false;

			MethodSource source = method.Source;
			if (source.SourceBuffer == null)
				return false;

			string line = source.SourceBuffer.Contents [location.Row - 1];
			context.Print (String.Format ("{0,4} {1}", location.Row, line));
			return true;
		}

		public override void TargetEvent (TargetAccess target, Thread thread,
						  TargetEventArgs args)
		{
			if (args.Frame != null)
				TargetEvent (target, args.Frame, args);

			switch (args.Type) {
			case TargetEventType.TargetExited:
				if (!thread.IsDaemon) {
					if ((int) args.Data == 0)
						interpreter.Print ("{0} terminated normally.",
								   thread.Name);
					else
						interpreter.Print ("{0} exited with exit code {1}.",
								   thread.Name, (int) args.Data);
				}
				break;

			case TargetEventType.TargetSignaled:
				if (!thread.IsDaemon) {
					interpreter.Print ("{0} died with fatal signal {1}.",
							   thread.Name, (int) args.Data);
				}
				break;
			}
		}

		protected void TargetEvent (TargetAccess target, StackFrame frame,
					    TargetEventArgs args)
		{
			switch (args.Type) {
			case TargetEventType.TargetStopped: {
				if ((int) args.Data != 0)
					interpreter.Print ("{0} received signal {1} at {2}.",
							   target.Name, (int) args.Data, frame);
				else if (!interpreter.IsInteractive)
					break;
				else
					interpreter.Print ("{0} stopped at {1}.", target.Name, frame);

				if (interpreter.IsScript)
					break;

				AssemblerLine insn;
				try {
					insn = target.DisassembleInstruction (
						frame.Method, frame.TargetAddress);
				} catch {
					insn = null;
				}

				interpreter.Style.TargetStopped (
					interpreter.GlobalContext, frame, insn);

				break;
			}

			case TargetEventType.TargetHitBreakpoint: {
				if (!interpreter.IsInteractive)
					break;

				interpreter.Print ("{0} hit breakpoint {1} at {2}.",
						   target.Name, (int) args.Data, frame);

				if (interpreter.IsScript)
					break;

				AssemblerLine insn;
				try {
					insn = target.DisassembleInstruction (
						frame.Method, frame.TargetAddress);
				} catch {
					insn = null;
				}

				interpreter.Style.TargetStopped (
					interpreter.GlobalContext, frame, insn);

				break;
			}

			case TargetEventType.Exception:
			case TargetEventType.UnhandledException:
				interpreter.Print ("{0} caught {2}exception at {1}.", target.Name, frame,
						   args.Type == TargetEventType.Exception ?
						   "" : "unhandled ");

				if (interpreter.IsScript)
					break;

				AssemblerLine insn;
				try {
					insn = target.DisassembleInstruction (
						frame.Method, frame.TargetAddress);
				} catch {
					insn = null;
				}

				interpreter.Style.UnhandledException (
					interpreter.GlobalContext, frame, insn);

				break;
			}
		}

		public override string FormatObject (TargetAccess target, object obj,
						     DisplayFormat format)
		{
			if ((obj is byte) || (obj is sbyte) || (obj is char) ||
			    (obj is short) || (obj is ushort) || (obj is int) || (obj is uint) ||
			    (obj is long) || (obj is ulong)) {
				if (format == DisplayFormat.HexaDecimal)
					return String.Format ("0x{0:x}", obj);
				else
					return obj.ToString ();
			} else if (obj is bool) {
				return ((bool) obj) ? "true" : "false";
			} else if (obj is string) {
				return '"' + (string) obj + '"';
			} else if (obj is TargetClassType) {
				TargetClassType stype = (TargetClassType) obj;
				return FormatStructType (target, stype);
			}
			else if (obj is TargetEnumType) {
				TargetEnumType etype = (TargetEnumType) obj;
				return FormatEnumType (target, etype);
			}
			else if (obj is TargetType) {
				return ((TargetType) obj).Name;
			}
			else if (obj is TargetObject) {
				TargetObject tobj = (TargetObject) obj;
				return String.Format ("({0}) {1}", tobj.TypeName,
						      DoFormatObject (target, tobj, false, format));
			} else {
				return obj.ToString ();
			}
		}

		protected string FormatEnumMember (TargetAccess target, string prefix,
						   TargetMemberInfo member, bool is_static,
						   Hashtable hash)
		{
			TargetFieldInfo fi = member as TargetFieldInfo;
			string value = "";
			if (fi.HasConstValue)
				value = String.Format (" = {0}", fi.ConstValue);
			return String.Format ("{0}   {1}{2}", prefix, member.Name, value);
		}

		protected string FormatMember (string prefix, TargetMemberInfo member,
					       bool is_static, Hashtable hash)
		{
			string tname = member.Type.Name;
			if (is_static)
				return String.Format (
					"{0}   static {1} {2}", prefix, tname, member.Name);
			else
				return String.Format (
					"{0}   {1} {2}", prefix, tname, member.Name);
		}

		protected string FormatProperty (string prefix, TargetPropertyInfo prop,
						 bool is_static, Hashtable hash)
		{
			StringBuilder sb = new StringBuilder ();
			sb.Append (FormatMember (prefix, prop, is_static, hash));
			sb.Append (" {");
			if (prop.CanRead)
				sb.Append (" get;");
			if (prop.CanWrite)
				sb.Append (" set;");
			sb.Append (" };\n");
			return sb.ToString ();
		}

		protected string FormatEvent (string prefix, TargetEventInfo ev,
					      bool is_static, Hashtable hash)
		{
			string tname = ev.Type.Name;
			if (is_static)
				return String.Format (
					"{0}   static event {1} {2};\n", prefix, tname, ev.Name);
			else
				return String.Format (
					"{0}   event {1} {2};\n", prefix, tname, ev.Name);
		}

		protected string FormatMethod (string prefix, TargetMethodInfo method,
					       bool is_static, bool is_ctor, Hashtable hash)
		{
			StringBuilder sb = new StringBuilder ();
			sb.Append (prefix);
			if (is_ctor)
				if (is_static)
					sb.Append ("   .cctor ");
				else
					sb.Append ("   .ctor ");
			else if (is_static)
				sb.Append ("   static ");
			else
				sb.Append ("   ");

			TargetFunctionType ftype = method.Type;
			if (!is_ctor) {
				if (ftype.HasReturnValue)
					sb.Append (ftype.ReturnType.Name);
				else
					sb.Append ("void");
				sb.Append (" ");
				sb.Append (method.Name);
				sb.Append (" ");
			}
			sb.Append ("(");
			bool first = true;
			foreach (TargetType ptype in ftype.ParameterTypes) {
				if (first)
					first = false;
				else
					sb.Append (", ");
				sb.Append (ptype.Name);
			}
			sb.Append (");\n");
			return sb.ToString ();
		}

		public override string FormatType (TargetAccess target, TargetType type)
		{
			return FormatType (target, "", type, null);
		}

		protected string FormatType (TargetAccess target, string prefix,
					     TargetType type, Hashtable hash)
		{
			string retval;

			if (hash == null)
				hash = new Hashtable ();

			if (hash.Contains (type))
				return type.Name;
			else
				hash.Add (type, true);

			switch (type.Kind) {
			case TargetObjectKind.Array: {
				TargetArrayType atype = (TargetArrayType) type;
				retval = atype.Name;
				break;
			}

			case TargetObjectKind.Enum: {
				StringBuilder sb = new StringBuilder ();
				TargetEnumType etype = type as TargetEnumType;
				sb.Append ("enum ");

				if (etype.Name != null)
					sb.Append (etype.Name);

				sb.Append ("\n" + prefix + "{\n");

				foreach (TargetFieldInfo field in etype.Members) {
					sb.Append (FormatEnumMember (target, prefix, field, false, hash));
					if (field != etype.Members[etype.Members.Length - 1])
						sb.Append (",");
					sb.Append ("\n");
				}
				

				sb.Append (prefix + "}");

				retval = sb.ToString ();
				break;
			}

			case TargetObjectKind.Class:
			case TargetObjectKind.Struct: {
				TargetClassType stype = (TargetClassType) type;
				StringBuilder sb = new StringBuilder ();
				TargetClassType ctype = type as TargetClassType;
				if (type.Kind == TargetObjectKind.Struct)
					sb.Append ("struct ");
				else
					sb.Append ("class ");
				if (ctype != null) {
					if (ctype.Name != null) {
						sb.Append (ctype.Name);
						sb.Append (" ");
					}
					if (ctype.HasParent) {
						sb.Append (": ");
						sb.Append (ctype.ParentType.Name);
					}
				} else {
					if (stype.Name != null) {
						sb.Append (stype.Name);
						sb.Append (" ");
					}
				}
				sb.Append ("\n" + prefix + "{\n");
				foreach (TargetFieldInfo field in stype.Fields)
					sb.Append (FormatMember (
							   prefix, field, false, hash) + ";\n");
				foreach (TargetFieldInfo field in stype.StaticFields)
					sb.Append (FormatMember (
							   prefix, field, true, hash) + ";\n");
				foreach (TargetPropertyInfo property in stype.Properties)
					sb.Append (FormatProperty (
							   prefix, property, false, hash));
				foreach (TargetPropertyInfo property in stype.StaticProperties)
					sb.Append (FormatProperty (
							   prefix, property, true, hash));
				foreach (TargetEventInfo ev in stype.Events)
					sb.Append (FormatEvent (
							   prefix, ev, false, hash));
				foreach (TargetEventInfo ev in stype.StaticEvents)
					sb.Append (FormatEvent (
							   prefix, ev, true, hash));
				foreach (TargetMethodInfo method in stype.Methods)
					sb.Append (FormatMethod (
							   prefix, method, false, false, hash));
				foreach (TargetMethodInfo method in stype.StaticMethods)
					sb.Append (FormatMethod (
							   prefix, method, true, false, hash));
				foreach (TargetMethodInfo method in stype.Constructors)
					sb.Append (FormatMethod (
							   prefix, method, false, true, hash));
				foreach (TargetMethodInfo method in stype.StaticConstructors)
					sb.Append (FormatMethod (
							   prefix, method, true, true, hash));

				sb.Append (prefix);
				sb.Append ("}");

				retval = sb.ToString ();
				break;
			}

#if FIXME
			case TargetObjectKind.Alias: {
				TargetTypeAlias alias = (TargetTypeAlias) type;
				string name;
				if (alias.TargetType != null)
					name = FormatType (target, prefix, alias.TargetType, hash);
				else
					name = "<unknown type>";
				retval = String.Format ("typedef {0} = {1}", alias.Name, name);
				break;
			}
#endif

			default:
				retval = type.Name;
				break;
			}

			hash.Remove (type);
			return retval;
		}

		public string FormatEnumType (TargetAccess target, TargetEnumType etype)
		{
			return String.Format ("enum {0}", etype.Name);
		}

		public string FormatStructType (TargetAccess target, TargetClassType stype)
		{
			string header = "";
			switch (stype.Kind) {
			case TargetObjectKind.Class:
				header = "class " + stype.Name + " ";
				break;
			case TargetObjectKind.Struct:
				header = "struct " + stype.Name + " ";
				break;
			}

			switch (stype.Kind) {
			case TargetObjectKind.Class:
			case TargetObjectKind.Struct:
				StructFormatter formatter = new StructFormatter (header);
				TargetFieldInfo[] fields = stype.StaticFields;
				foreach (TargetFieldInfo field in fields) {
					TargetObject fobj = stype.GetStaticField (target, field);
					string item;
					try {
						if (fobj == null)
							item = "null";
						else
							item = DoFormatObject (target, fobj, false,
									       DisplayFormat.Default);
					} catch {
						item = "<cannot display object>";
					}
					formatter.Add (String.Format ("{0} = {1}", field.Name, item));
				}
				return formatter.Format ();

			default:
				return stype.Name;
			}       
		}

		protected string PrintObject (TargetAccess target, TargetObject obj)
		{
			try {
				return obj.Print (target);
			} catch {
				return "<cannot display object>";
			}
		}

		protected string DoFormatObject (TargetAccess target, TargetObject obj,
						 bool recursed, DisplayFormat format)
		{
			try {
				if (format == DisplayFormat.Address) {
					if (obj.HasAddress)
						return obj.Address.ToString ();
					else
						return "<cannot get address>";
				} else if (recursed)
					return DoFormatObjectRecursed (target, obj, format);
				else
					return DoFormatObject (target, obj, format);
			} catch {
				return "<cannot display object>";
			}
		}

		protected string DoFormatObjectRecursed (TargetAccess target, TargetObject obj,
							 DisplayFormat format)
		{
			if (obj.IsNull)
				return "null";

			switch (obj.Kind) {
			case TargetObjectKind.Enum:
				return DoFormatObject (target, obj, format);

			case TargetObjectKind.Fundamental:
				object value = ((TargetFundamentalObject) obj).GetObject (target);
				return FormatObject (target, value, format);

			default:
				return PrintObject (target, obj);
			}
		}

		protected void DoFormatArray (TargetAccess target, TargetArrayObject aobj,
					      StringBuilder sb, int dimension, int rank,
					      int[] indices, DisplayFormat format)
		{
			sb.Append ("[ ");

			int[] new_indices = new int [dimension + 1];
			indices.CopyTo (new_indices, 0);

			int lower = aobj.GetLowerBound (target, dimension);
			int upper = aobj.GetUpperBound (target, dimension);

			for (int i = lower; i < upper; i++) {
				if (i > lower)
					sb.Append (", ");

				new_indices [dimension] = i;
				if (dimension + 1 < rank)
					DoFormatArray (target, aobj, sb, dimension + 1, rank, new_indices, format);
				else
					sb.Append (DoFormatObject (target, aobj.GetElement (target, new_indices), false, format));
			}

			sb.Append (" ]");
		}

		protected string DoFormatArray (TargetAccess target, TargetArrayObject aobj,
						DisplayFormat format)
		{
			int rank = aobj.Type.Rank;
			StringBuilder sb = new StringBuilder ();

			DoFormatArray (target, aobj, sb, 0, rank, new int [0], format);
			return sb.ToString ();
		}

		protected string DoFormatEnum (TargetAccess target, TargetEnumObject eobj,
					       DisplayFormat format)
		{
			TargetFundamentalObject fobj = eobj.Value as TargetFundamentalObject;

			if ((format == DisplayFormat.HexaDecimal) || (fobj == null))
				return DoFormatObject (target, eobj.Value, true, format);

			object value = fobj.GetObject (target);

			if (!eobj.Type.IsFlagsEnum) {
				foreach (TargetFieldInfo field in eobj.Type.Members) {
					if (field.ConstValue.Equals (value))
						return field.Name;
				}
			} else if (value is ulong) {
				StringBuilder sb = null;
				ulong the_value = (ulong) value;
				foreach (TargetFieldInfo field in eobj.Type.Members) {
					ulong fvalue = System.Convert.ToUInt64 (field.ConstValue);
					if ((the_value & fvalue) != fvalue)
						continue;
					if (sb == null)
						sb = new StringBuilder (field.Name);
					else
						sb.Append (" | " + field.Name);
				}
				if (sb != null)
					return sb.ToString ();
			} else {
				StringBuilder sb = null;
				long the_value = System.Convert.ToInt64 (value);
				foreach (TargetFieldInfo field in eobj.Type.Members) {
					long fvalue = System.Convert.ToInt64 (field.ConstValue);
					if ((the_value & fvalue) != fvalue)
						continue;
					if (sb == null)
						sb = new StringBuilder (field.Name);
					else
						sb.Append (" | " + field.Name);
				}
				if (sb != null)
					return sb.ToString ();
			}

			return DoFormatObject (target, fobj, true, format);
		}

		protected string DoFormatObject (TargetAccess target, TargetObject obj,
						 DisplayFormat format)
		{
			if (obj.IsNull)
				return "null";

			switch (obj.Kind) {
			case TargetObjectKind.Array:
				return DoFormatArray (target, (TargetArrayObject) obj, format);

			case TargetObjectKind.Pointer: {
				TargetPointerObject pobj = (TargetPointerObject) obj;
				if (pobj.Type.IsTypesafe) {
					try {
						TargetObject deref = pobj.GetDereferencedObject (target);
						return String.Format (
							"&({0}) {1}", deref.TypeName,
							DoFormatObject (target, deref, true, format));
					} catch {
						return DoFormatObject (target, pobj, true, format);
					}
				} else
					return DoFormatObject (target, pobj, true, format);
			}

			case TargetObjectKind.Object: {
				TargetObjectObject oobj = (TargetObjectObject) obj;
				try {
					TargetObject deref = oobj.GetDereferencedObject (target);
					return String.Format (
						"&({0}) {1}", deref.TypeName,
						DoFormatObject (target, deref, false, format));
				} catch {
					return DoFormatObject (target, oobj, true, format);
				}
			}

			case TargetObjectKind.Class:
			case TargetObjectKind.Struct: {
				TargetClassObject sobj = (TargetClassObject) obj;
				StructFormatter formatter = new StructFormatter ("");
				TargetFieldInfo[] fields = sobj.Type.Fields;
				foreach (TargetFieldInfo field in fields) {
					string item;
					try {
						TargetObject fobj = sobj.GetField (target, field);
						if (fobj == null)
							item = "null";
						else
							item = DoFormatObject (target, fobj, true, format);
					} catch {
						item = "<cannot display object>";
					}
					formatter.Add (field.Name + " = " + item);
				}
				return formatter.Format ();
			}

			case TargetObjectKind.Fundamental: {
				object value = ((TargetFundamentalObject) obj).GetObject (target);
				return FormatObject (target, value, format);
			}

			case TargetObjectKind.Enum:
				return DoFormatEnum (target, (TargetEnumObject) obj, format);

			default:
				return PrintObject (target, obj);
			}
		}

		public override string PrintVariable (TargetVariable variable, StackFrame frame)
		{
			string contents;
			TargetObject obj = null;

			try {
				obj = variable.GetObject (frame);
				if (obj != null)
					contents = DoFormatObject (
						frame.TargetAccess, obj, false, DisplayFormat.Default);
				else
					contents = "<cannot display object>";
			} catch {
				contents = "<cannot display object>";
			}

			return String.Format (
				"{0} = ({1}) {2}", variable.Name, variable.Type.Name, contents);
		}

		public override string ShowVariableType (TargetType type, string name)
		{
			return type.Name;
		}
	}

	[Serializable]
	public abstract class StyleBase : Style
	{
		protected Interpreter interpreter;

		protected StyleBase (Interpreter interpreter)
		{
			this.interpreter = interpreter;
		}

		public abstract bool IsNative {
			get; set;
		}

		public abstract void Reset ();

		public abstract void PrintFrame (ScriptingContext context, StackFrame frame);

		public abstract void TargetStopped (ScriptingContext context, StackFrame frame,
						    AssemblerLine current_insn);

		public abstract void UnhandledException (ScriptingContext context, StackFrame frame,
							 AssemblerLine current_insn);

		public abstract void TargetEvent (TargetAccess target, Thread thread,
						  TargetEventArgs args);
	}
}
