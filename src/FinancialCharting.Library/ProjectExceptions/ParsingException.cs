﻿#region Usings

using System;

#endregion

namespace FinancialCharting.Library.ProjectExceptions
{
	[Serializable]
	public class ParsingException : Exception
	{
		public ParsingException(Exception inner)
			: base("Parsing failed", inner)
		{}
	}
}