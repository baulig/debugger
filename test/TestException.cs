using System;

public class X
{
	public void Test ()
	{
		throw new InvalidOperationException ();
	}

	static void Main ()
	{
		X x = new X ();
		try {
			x.Test ();
		} catch (InvalidOperationException ex) {
			Console.WriteLine ("EXCEPTION: {0}", ex.GetType ());
		}

		Console.WriteLine ("Done");
	}
}