using System;
using System.Collections;

namespace Mono.Debugger
{
	public interface ISymbolLookup
	{
		Method Lookup (TargetAddress address);
	}

	public interface ISymbolContainer
	{
		// <summary>
		//   StartAddress and EndAddress are only valid if this is true.
		// </summary>
		bool IsContinuous {
			get;
		}

		TargetAddress StartAddress {
			get;
		}

		TargetAddress EndAddress {
			get;
		}
	}

	public interface ISymbolRange : IComparable
	{
		TargetAddress StartAddress {
			get;
		}

		TargetAddress EndAddress {
			get;
		}

		// <summary>
		//   If the address you're looking for is within the
		//   [StartAddress,EndAddress] interface, use this property
		//   to get an ISymbolLookup instance which you can use to
		//   search the symbol.  This'll automatically load the
		//   symbol table from disk if necessary.
		// </summary>
		ISymbolLookup SymbolLookup {
			get;
		}
	}

	public delegate void SymbolTableChangedHandler ();

	// <summary>
	//   This interface is used to find a method by an address.
	// </summary>
	public interface ISymbolTable : ISymbolLookup, ISymbolContainer
	{
		// <summary>
		//   Whether this symbol table has an address range table.
		// </summary>
		bool HasRanges {
			get;
		}

		// <summary>
		//   If HasRanges is true, this is a sorted (by start address)
		//   list of address ranges.  To lookup a symbol by its address,
		//   first search in this table to find the correct ISymbolRange,
		//   then use its SymbolLookup property to get an ISymbolLookup
		//   on which you can do a Lookup().
		// </summary>
		// <remarks>
		//   When searching in more than one ISymbolTable, consider using
		//   an ISymbolTableCollection instead since this will merge the
		//   address ranges from all its symbol tables into one big table
		//   and thus a lookup fill me faster.
		// </remarks>
		ISymbolRange[] SymbolRanges {
			get;
		}

		// <summary>
		//   If true, you may use the `Methods' property to get a list of all the
		//   methods in this symbol table.
		// </summary>
		bool HasMethods {
			get;
		}

		// <summary>
		//   Get a list of all methods in this symbol table.  May only be used if
		//   `HasMethods' is true.
		// </summary>
		Method[] Methods {
			get;
		}

		bool IsLoaded {
			get;
		}

		void UpdateSymbolTable ();

		event SymbolTableChangedHandler SymbolTableChanged;
	}

	[Serializable]
	public class Symbol : IComparable
	{
		public readonly string Name;
		public readonly TargetAddress Address;
		public readonly int Offset;

		public Symbol (string name, TargetAddress address, int offset)
		{
			this.Name = name;
			this.Address = address;
			this.Offset = offset;
		}

		public int CompareTo (object obj)
		{
			Symbol symbol = (Symbol) obj;

			if (symbol.Address < Address)
				return 1;
			else if (symbol.Address > Address)
				return -1;
			else
				return 0;
		}

		public override string ToString ()
		{
			if (Offset > 0)
				return String.Format ("{0}+0x{1:x}", Name, Offset);
			else if (Offset < 0)
				return String.Format ("{0}-0x{1:x}", Name, Offset);
			else
				return Name;
		}
	}
}
