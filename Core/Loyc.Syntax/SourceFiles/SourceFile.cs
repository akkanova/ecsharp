using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using Loyc.MiniTest;
using Loyc.Collections;

namespace Loyc.Syntax
{
	/// <summary>A default implementation of ISourceFile based on <see cref="IndexPositionMapper"/>.</summary>
	public class SourceFile<CharSource> : IndexPositionMapper<CharSource>, ISourceFile
		where CharSource : ICharSource
	{
		new protected CharSource _source;

		public SourceFile(CharSource source, ILineColumnFile startingPos = null) : base(source, startingPos) { _source = source; }
		public SourceFile(CharSource source, string fileName) : base(source, fileName) { _source = source; }

		public CharSource Text
		{
			get { return _source; }
		}
		ICharSource ISourceFile.Text
		{
			get { return Text; }
		}
	}

	[Obsolete("Please use SourceFile<ICharSource> or SourceFile<UString> instead.")]
	public class SourceFile : SourceFile<ICharSource>
	{
		public SourceFile(ICharSource source, ILineColumnFile startingPos = null) : base(source, startingPos) { }
		public SourceFile(ICharSource source, string fileName) : base(source, fileName) { }
	} 
}
