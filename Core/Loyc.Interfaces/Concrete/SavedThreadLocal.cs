using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Loyc.Threading
{
	/// <summary>
	/// Designed to be used in a "using" statement to temporarily alter a 
	/// <see cref="ThreadLocal{T}"/>.
	/// </summary>
	public struct SavedThreadLocal<T> : IDisposable
	{
		[AllowNull] T _oldValue;
		ThreadLocal<T> _variable;

		public SavedThreadLocal(ThreadLocal<T> variable, T newValue)
		{
			_variable = variable;
			_oldValue = variable.Value;
			variable.Value = newValue;
		}
		public void Dispose()
		{
			_variable.Value = _oldValue;
		}

		[MaybeNull] 
		public T OldValue { get { return _oldValue; } }
		public T Value { get { return _variable.Value!; } }
	}
}
