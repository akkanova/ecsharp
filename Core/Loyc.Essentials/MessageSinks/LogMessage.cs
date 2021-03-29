using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Loyc.Collections;

namespace Loyc
{
	/// <summary>Holds an argument list compatible with 
	/// <see cref="IMessageSink{TContext}.Write(Severity,TContext,string)"/>.
	/// Typically used with <see cref="MessageHolder"/>.</summary>
	public struct LogMessage : ILocation, ILogMessage
	{
		public LogMessage(Severity type, object? context, string format, object? arg0, object? arg1 = null)
			: this (type, context, format, new object?[2] { arg0, arg1 }) {}
		public LogMessage(Severity type, object? context, string format)
			: this (type, context, format, EmptyArray<object>.Value) {}
		public LogMessage(Severity type, object? context, string format, params object?[] args)
		{
			Severity = type;
			Context = context;
			Format = format;
			_args = args;
		}

		public Severity Severity { get; set; }
		public object? Context { get; set; }
		public string Format { get; set; }
		object?[] _args;
		public object?[] Args { get => _args; set => _args = value; }
		public string Formatted
		{
			get {
				try {
					return Localize.Localized(Format, _args);
				} catch {
					return Format;
				}
			}
		}

		public override string ToString()
		{
			return MessageSink.FormatMessage(Severity, Context, Format, _args);
		}

		public object? Location
		{
			get { return MessageSink.LocationOf(Context); }
		}
		public void WriteTo(IMessageSink<object?> sink)
		{
			if (_args.Length == 0)
				sink.Write(Severity, Context, Format);
			else
				sink.Write(Severity, Context, Format, _args);
		}
	}
}
