using System;
using System.Reflection;
using System.Text.RegularExpressions;

namespace OrcaSql.Framework
{
	public static class TestHelper
	{
		public static void GetAllPublicProperties(object obj)
		{
			var props = obj.GetType().GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance);

			foreach(var prop in props)
				prop.GetValue(obj, null);
		}

		public static byte[] GetBytesFromByteString(string input)
		{
			// Remove anything but valid hex characters
			input = Regex.Replace(input, "[^0-9A-F]", "", RegexOptions.IgnoreCase);

			if(input.Length % 2 != 0)
				throw new FormatException("input");

#if NET8_0_OR_GREATER
			return Convert.FromHexString(input);
#else
			return System.Runtime.Remoting.Metadata.W3cXsd2001.SoapHexBinary.Parse(input).Value;
#endif
		}
	}
}