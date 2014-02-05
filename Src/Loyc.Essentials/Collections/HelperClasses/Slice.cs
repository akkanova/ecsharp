﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Loyc.Collections
{
	/// <summary>A random-access range for a slice of an <see cref="IListSource{T}"/>.</summary>
	/// <typeparam name="T">Item type in the list</typeparam>
	/// <remarks>
	/// This type was supposed to be called simply <c>Slice</c>, but this was not
	/// allowed because in plain C#, "CS0542: member names cannot be the same as 
	/// their enclosing type" and of course, this type contains the Slice() method
	/// from IListSource.
	/// </remarks>
	public struct Slice_<T> : IRange<T>, ICloneable<Slice_<T>>, IIsEmpty
	{
		IListSource<T> _list;
		int _start, _count;
		
		/// <summary>Initializes a slice.</summary>
		/// <exception cref="ArgumentException">The start index was below zero.</exception>
		/// <remarks>The (start, count) range is allowed to be invalid, as long
		/// as 'start' and 'count' are zero or above. 
		/// <ul>
		/// <li>If 'start' is above the original Count, the Count of the new slice 
		/// is set to zero.</li>
		/// <li>if (start + count) is above the original Count, the Count of the new
		/// slice is reduced to <c>list.Count - start</c>.</li>
		/// </ul>
		/// </remarks>
		public Slice_(IListSource<T> list, int start, int count)
		{
			_list = list;
			_start = start;
			_count = count;
			if (start < 0) throw new ArgumentException("The start index was below zero.");
			if (count < 0) throw new ArgumentException("The count was below zero.");
			if (count > _list.Count - start)
				_count = System.Math.Max(_list.Count - start, 0);
		}

		public int Count
		{
			get { return _count; }
		}
		public bool IsEmpty
		{
			get { return _count == 0; }
		}
		public T Front
		{
			get { return this[0]; }
		}
		public T Back
		{
			get { return this[_count - 1]; }
		}

		public T PopFront(out bool empty)
		{
			if (_count != 0) {
				empty = false;
				_count--;
				return _list[_start++];
			}
			empty = true;
			return default(T);
		}
		public T PopBack(out bool empty)
		{
			if (_count != 0) {
				empty = false;
				_count--;
				return _list[_start + _count];
			}
			empty = true;
			return default(T);
		}

		IFRange<T> ICloneable<IFRange<T>>.Clone() { return Clone(); }
		IBRange<T> ICloneable<IBRange<T>>.Clone() { return Clone(); }
		IRange<T>  ICloneable<IRange<T>> .Clone() { return Clone(); }
		public Slice_<T> Clone() { return this; }

		IEnumerator<T> IEnumerable<T>.GetEnumerator() { return GetEnumerator(); }
		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return GetEnumerator(); }
		public RangeEnumerator<Slice_<T>,T> GetEnumerator()
		{
			return new RangeEnumerator<Slice_<T>, T>(this);
		}

		public T this[int index]
		{
			get { 
				if ((uint)index < (uint)_count)
					return _list[_start + index];
				throw new ArgumentOutOfRangeException("index");
			}
		}
		public T this[int index, T defaultValue]
		{
			get { 
				if ((uint)index < (uint)_count)
					return _list[_start + index];
				return defaultValue;
			}
		}
		public T TryGet(int index, out bool fail)
		{
			if ((uint)index < (uint)_count) {
				fail = false;
				return _list[_start + index];
			}
			fail = true;
			return default(T);
		}

		IRange<T> IListSource<T>.Slice(int start, int count) { return Slice(start, count); }
		public Slice_<T> Slice(int start, int count)
		{
			if (start < 0) throw new ArgumentException("The start index was below zero.");
			if (count < 0) throw new ArgumentException("The count was below zero.");
			var slice = new Slice_<T>();
			slice._list = this._list;
			slice._start = this._start + start;
			slice._count = count;
			if (slice._count > this._count - start)
				slice._count = System.Math.Max(this._count - start, 0);
			return slice;
		}
	}
}
