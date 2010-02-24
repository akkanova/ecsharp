﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using Loyc.Runtime;
using Loyc.Utilities.CPTrie;

namespace Loyc.Utilities
{
	public class CPTrie<T>
	{
		public CPTrie() { }
		public CPTrie(CPTrie<T> copy)
		{
			_head = copy._head.CloneAndOptimize();
			_count = copy._count;
		}
		
		private CPNode<T> _head;
		internal CPNode<T> Head { get { return _head; } }
		private int _count;

		protected int Count { get { return _count; } }

		protected static Comparer<T> DefaultComparer = Comparer<T>.Default;
		private static ScratchBuffer<byte[]> _stringScratchBuffer;
		private const int StringScratchBufferLen = 48;
		
		/// <summary>Converts a string to a sequence of bytes suitable for use in 
		/// the trie. For speed, a simplified UTF-8 encoding is used, where 
		/// surrogate pairs are not specially handled.</summary>
		/// <param name="keyLength">Length of the output. The array length is not 
		/// relevant, as this method may store the key in a scratch buffer that is 
		/// longer than the key.</param>
		/// <returns>The key encoded in bytes.</returns>
		protected internal static KeyWalker StringToBytes(string key)
		{
			int outSize = key.Length;
			byte[] buf = _stringScratchBuffer.Value;

			if (outSize > StringScratchBufferLen/3) {
				// Need to compute exact length if the scratch buffer might be too small
				for (int i = 0; i < key.Length; i++) {
					int c = (int)key[i];
					if (c >= 0x80)
						outSize += (c >= (1 << 11) ? 2 : 1);
				}
				if (outSize > StringScratchBufferLen)
					buf = new byte[outSize];
			}
			if (buf == null)
				_stringScratchBuffer.Value = buf = new byte[StringScratchBufferLen];
			
			int B = 0;
			for (int i = 0; i < key.Length; i++) {
				int c = (int)key[i];
				if (c < 0x80) {
					buf[B++] = (byte)c;
				} else if (c < (1 << 11)) {
					buf[B++] = (byte)((c >> 6) | 0xC0);
					buf[B++] = (byte)((c & 0x3F) | 0x80);
				} else {
					buf[B++] = (byte)((c >> 12) | 0xE0);
					buf[B++] = (byte)(((c >> 6) & 0x3F) | 0x80);
					buf[B++] = (byte)((c & 0x3F) | 0x80);
				}
			}
			
			Debug.Assert(outSize <= StringScratchBufferLen/3 || outSize == B);
			return new KeyWalker(buf, B);
		}
		
		/// <summary>Converts a sequence of bytes (key[0..keyLength-1]) that was 
		/// previously encoded with StringToBytes to a string</summary>
		protected internal static string BytesToString(byte[] key, int keyLength)
		{
			if (keyLength <= 1) {
				if (keyLength == 0)
					return string.Empty;
				return ((char)key[0]).ToString();
			}
			return BytesToStringBuilder(key, keyLength).ToString();
		}

		protected static StringBuilder BytesToStringBuilder(byte[] key, int keyLength)
		{
			StringBuilder sb = new StringBuilder(keyLength);
			for (int B = 0; B < keyLength; B++)
			{
				byte k = key[B];
				if (k < 0x80) {
					sb.Append((char)k);
				} else if (k < 0xE0) {
					Debug.Assert(k >= 0xC2);
					byte k2 = key[++B];
					Debug.Assert(k2 >= 0x80 && k2 <= 0xBF);
					sb.Append((char)(((k & 0x1F) << 6) + (k2 & 0x3F)));
				} else {
					Debug.Assert(k < 0xF0);
					byte k2 = key[++B];
					byte k3 = key[++B];
					Debug.Assert(k2 >= 0x80 && k2 <= 0xBF);
					Debug.Assert(k3 >= 0x80 && k3 <= 0xBF);
					sb.Append((char)(((k & 0xF) << 12) + ((k2 & 0x3F) << 6) + (k2 & 0x3F)));
				}
			}
			return sb;
		}

		protected bool Find(ref KeyWalker key, CPEnumerator<T> e)
		{
			e.Reset();
			if (_head != null) {
				if (_head.Find(ref key, e))
					return true;
			}
			return false;
		}

		protected bool Find(ref KeyWalker key, ref T value)
		{
			if (_head != null)
				return _head.Set(ref key, ref value, ref _head, CPMode.Find);
			return false;
		}

		
		/// <summary>
		/// Associates the specified value with the specified key.
		/// </summary>
		/// <param name="key">A key to find or create; if key.Offset > 0, bytes
		/// before that offset are ignored.</param>
		/// <param name="value">Value to assign to the node, depending on the value
		/// of mode. On return, value is set to the previous value for the given key.</param>
		/// <param name="mode">Specifies whether to create an entry if the key is
		/// not present, and whether to change an existing entry. If mode is Find,
		/// Set() only retrieves an existing value; it does not change the trie.</param>
		/// <returns>Returns true if the specified key already existed and false if 
		/// it did not.</returns>
		protected bool Set(ref KeyWalker key, ref T value, CPMode mode)
		{
			if (_head != null) {
				bool existed = _head.Set(ref key, ref value, ref _head, mode);
				if (!existed && (mode & CPMode.Create) != (CPMode)0)
					_count++;
				return existed;
			}
			else if ((mode & CPMode.Create) != (CPMode)0)
			{
				Debug.Assert(_count == 0);
				_head = new CPLinear<T>(ref key, value);
				_count = 1;
			}
			return false;
		}
		
		protected bool Remove(ref KeyWalker key, ref T value)
		{
			if (_head != null)
				if (_head.Remove(ref key, ref value, ref _head))
				{
					_count--;
					Debug.Assert((_count == 0) == (_head == null));
					return true;
				}
			return false;
		}

		protected void Clear()
		{
			_head = null;
			_count = 0;
		}

		/// <summary>Calculates the memory usage of this object, assuming a 32-bit
		/// architecture.</summary>
		/// <param name="sizeOfT">Size of data type T. CountMemoryUsage doesn't use
		/// sizeof(T), as it would force the code to be marked "unsafe".
		/// <returns>Estimated number of bytes used by this object</returns>
		protected int CountMemoryUsage(int sizeOfT)
		{
			int size = 16;
			if (_head != null)
				size += _head.CountMemoryUsage(sizeOfT);
			return size;
		}
	}

	public class CPStringTrie<TValue> : CPTrie<TValue>, IDictionary<string, TValue>
	{
		public CPStringTrie() { }
		public CPStringTrie(CPStringTrie<TValue> clone) : base(clone) { }

		public new int CountMemoryUsage(int sizeOfT) { return base.CountMemoryUsage(sizeOfT); }

		#region IDictionary<string,TValue> Members

		public void Add(string key, TValue value)
		{
			KeyWalker kw = StringToBytes(key);
			if (base.Set(ref kw, ref value, CPMode.Create))
				throw new ArgumentException(Localize.From("Key already exists: ") + key);
		}

		/// <summary>Adds the specified key-value pair only if the specified key is
		/// not already present in the trie.</summary>
		/// <returns>Returns true if the key-value pair was added or false if
		/// the key already existed. In the false case, the trie is not modified.</returns>
		public bool TryAdd(string key, TValue value)
		{
			KeyWalker kw = StringToBytes(key);
			return !base.Set(ref kw, ref value, CPMode.Set);
		}
		/// <summary>Adds the specified key-value pair only if the specified key is
		/// not already present in the trie.</summary>
		/// <param name="value">On entry, value specifies the value to associate
		/// with the specified key, but if the key already exists, value is changed
		/// to the value associated with the existing key.</param>
		/// <returns>Returns true if the key-value pair was added or false if
		/// the key already existed. In the false case, the trie is not modified.</returns>
		public bool TryAdd(string key, ref TValue value)
		{
			KeyWalker kw = StringToBytes(key);
			return !base.Set(ref kw, ref value, CPMode.Create);
		}

		public bool ContainsKey(string key)
		{
			KeyWalker kw = StringToBytes(key);
			TValue value = default(TValue);
			return base.Find(ref kw, ref value);
		}

		public bool Remove(string key)
		{
			KeyWalker kw = StringToBytes(key);
			TValue oldValue = default(TValue);
			return base.Remove(ref kw, ref oldValue);
		}
		public bool Remove(string key, ref TValue value)
		{
			KeyWalker kw = StringToBytes(key);
			return base.Remove(ref kw, ref value);
		}

		public bool TryGetValue(string key, out TValue value)
		{
			KeyWalker kw = StringToBytes(key);
			value = default(TValue);
			return base.Find(ref kw, ref value);
		}
		public TValue TryGetValue(string key, TValue defaultValue)
		{
			KeyWalker kw = StringToBytes(key);
			base.Find(ref kw, ref defaultValue);
			return defaultValue;
		}

		public TValue this[string key]
		{
			get {
				KeyWalker kw = StringToBytes(key);
				TValue value = default(TValue);
				if (!base.Find(ref kw, ref value))
					throw new KeyNotFoundException(Localize.From("Key not found: ") + key);
				return value;
			}
			set {
				KeyWalker kw = StringToBytes(key);
				base.Set(ref kw, ref value, CPMode.Set | CPMode.Create);
			}
		}

		public ICollection<string> Keys
		{
			get { throw new NotImplementedException(); }
		}
		public ICollection<TValue> Values
		{
			get { throw new NotImplementedException(); }
		}

		public void Add(KeyValuePair<string, TValue> item)
		{
			Add(item.Key, item.Value);
		}

		public new void Clear()
		{
			base.Clear();
		}

		public bool Contains(KeyValuePair<string, TValue> item)
		{
			KeyWalker kw = StringToBytes(item.Key);
			TValue value = default(TValue);
			if (base.Find(ref kw, ref value))
				return DefaultComparer.Compare(value, item.Value) == 0;
			return false;
		}

		public void CopyTo(KeyValuePair<string, TValue>[] array, int arrayIndex)
		{
			foreach (KeyValuePair<string, TValue> pair in this)
				array[arrayIndex] = pair;
		}

		public new int Count
		{
			get { return base.Count; }
		}

		public bool IsReadOnly
		{
			get { return false; }
		}

		public bool Remove(KeyValuePair<string, TValue> item)
		{
			KeyWalker kw = StringToBytes(item.Key);
			KeyWalker kw2 = kw;
			TValue value = default(TValue);
			if (Find(ref kw, ref value) && DefaultComparer.Compare(value, item.Value) == 0)
				return Remove(ref kw2, ref value);
			return false;
		}

		#endregion

		#region IEnumerable<KeyValuePair<string,TValue>> Members

		public Enumerator GetEnumerator()
		{
			return new Enumerator(this);
		}
		IEnumerator<KeyValuePair<string, TValue>> IEnumerable<KeyValuePair<string, TValue>>.GetEnumerator()
		{
			return GetEnumerator();
		}
		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		#endregion

		public CPStringTrie<TValue> Clone()
			{ return new CPStringTrie<TValue>(this); }

		public Enumerator FindAtLeast(string key)
		{
			KeyWalker kw = StringToBytes(key);
			Enumerator e = new Enumerator(this);
			base.Find(ref kw, e);
			return e;
		}
		public Enumerator FindExact(string key)
		{
			KeyWalker kw = StringToBytes(key);
			Enumerator e = new Enumerator(this);
			if (!base.Find(ref kw, e))
				return null;
			Debug.Assert(e.IsValid);
			return e;
		}
		public bool Find(string key, out Enumerator e)
		{
			KeyWalker kw = StringToBytes(key);
			e = new Enumerator(this);
			return base.Find(ref kw, e);
		}

		public bool IsEmpty { get { return base.Count == 0; } }

		public class Enumerator : CPEnumerator<TValue>, IEnumerator<KeyValuePair<string, TValue>>
		{
			internal protected Enumerator(CPTrie<TValue> trie) : base(trie) {}

			public new KeyValuePair<string, TValue> Current
			{
				get {
					return new KeyValuePair<string, TValue>(CurrentKey, CurrentValue);
				}
			}
			object System.Collections.IEnumerator.Current
			{
				get { return Current; }
			}
			public new TValue CurrentValue
			{
				get { return base.CurrentValue; }
			}
			public new string CurrentKey
			{
				get {
					return CPTrie<TValue>.BytesToString(Key.Buffer, Key.Offset + Key.Left);
				}
			}
		}
	}

	public class CPByteTrie<TValue> : CPTrie<TValue>, IDictionary<byte[], TValue>
	{
		public CPByteTrie() { }
		public CPByteTrie(CPByteTrie<TValue> clone) : base(clone) { }

		public new int CountMemoryUsage(int sizeOfT) { return base.CountMemoryUsage(sizeOfT); }

		#region IDictionary<string,TValue> Members

		/// <summary>Adds the specified key-value pair to the trie, throwing an
		/// exception if the key is already present.</summary>
		public void Add(byte[] key, TValue value)
		{
			KeyWalker kw = new KeyWalker(key, key.Length);
			if (base.Set(ref kw, ref value, CPMode.Create))
				throw new ArgumentException(Localize.From("Key already exists: ") + key);
		}
		/// <summary>Adds the specified key-value pair to the trie, throwing an
		/// exception if the key is already present.</summary>
		/// <param name="key">An array that contains the key to add. The offset
		/// and length parameters specify a substring of this array to use as the key.</param>
		public void Add(byte[] key, int offset, int length, TValue value)
		{
			KeyWalker kw = new KeyWalker(key, offset, length);
			Check(ref kw, "Add");
			if (base.Set(ref kw, ref value, CPMode.Create))
				throw new ArgumentException(Localize.From("Key already exists: ") + key);
		}

		/// <summary>Adds the specified key-value pair only if the specified key is
		/// not already present in the trie.</summary>
		/// <param name="value">A value to associate with the specified key if the
		/// key does not already exist.</param>
		/// <returns>Returns true if the key-value pair was added or false if
		/// the key already existed. In the false case, the trie is not modified.</returns>
		public bool TryAdd(byte[] key, TValue value)
		{
			return TryAdd(key, 0, key.Length, ref value);
		}
		public bool TryAdd(byte[] key, int offset, int length, TValue value)
		{
			return TryAdd(key, 0, key.Length, ref value);
		}
		
		/// <summary>Adds the specified key-value pair only if the specified key is
		/// not already present in the trie.</summary>
		/// <param name="key">An array that contains the key to find. The offset
		/// and length parameters specify a substring of this array to use as the key.</param>
		/// <param name="value">On entry, value specifies the value to associate
		/// with the specified key, but if the key already exists, value is changed
		/// to the value associated with the existing key.</param>
		/// <returns>Returns true if the key-value pair was added or false if
		/// the key already existed. In the false case, the trie is not modified.</returns>
		public bool TryAdd(byte[] key, int offset, int length, ref TValue value)
		{
			KeyWalker kw = new KeyWalker(key, offset, length);
			Check(ref kw, "TryAdd");
			return !base.Set(ref kw, ref value, CPMode.Create);
		}

		private void Check(ref KeyWalker kw, string operation)
		{
			if ((kw.Offset | kw.Left) < 0)
				throw new ArgumentException(operation + ": " + Localize.From("offset or length are negative"));
			if (kw.Offset + kw.Left > kw.Buffer.Length)
				throw new ArgumentException(operation + ": " + Localize.From("offset+length exceeds buffer length"));
		}

		/// <summary>Searches for the specified key, returning true if it is
		/// present in the trie.</summary>
		public bool ContainsKey(byte[] key)
		{
			KeyWalker kw = new KeyWalker(key, key.Length);
			TValue value = default(TValue);
			return base.Find(ref kw, ref value);
		}
		/// <summary>Searches for the specified key, returning true if it is
		/// present in the trie.</summary>
		/// <param name="key">An array that contains the key to find. The offset
		/// and length parameters specify a substring of this array to use as the key.</param>
		public bool ContainsKey(byte[] key, int offset, int length)
		{
			KeyWalker kw = new KeyWalker(key, offset, length);
			Check(ref kw, "ContainsKey");
			TValue value = default(TValue);
			return base.Find(ref kw, ref value);
		}

		/// <summary>Removes the specified key and associated value, returning true
		/// if the entry was found and removed.</summary>
		public bool Remove(byte[] key)
		{
			TValue dummy = default(TValue);
			return Remove(key, 0, key.Length, ref dummy);
		}
		public bool Remove(byte[] key, int offset, int length)
		{
			TValue dummy = default(TValue);
			return Remove(key, offset, length, ref dummy);
		}
		/// <summary>Removes the specified key and associated value, returning true
		/// if the entry was found and removed.</summary>
		/// <param name="key">An array that contains the key to find. The offset
		/// and length parameters specify a substring of this array to use as the key.</param>
		/// <param name="oldValue">If the key is found, the associated value is
		/// assigned to this parameter. Otherwise, this parameter is not changed.</param>
		public bool Remove(byte[] key, int offset, int length, ref TValue oldValue)
		{
			KeyWalker kw = new KeyWalker(key, offset, length);
			Check(ref kw, "Remove");
			return base.Remove(ref kw, ref oldValue);
		}

		/// <summary>Finds the specified key and gets its associated value,
		/// returning true if the key was found.</summary>
		public bool TryGetValue(byte[] key, out TValue value)
		{
			KeyWalker kw = new KeyWalker(key, 0, key.Length);
			value = default(TValue);
			return base.Find(ref kw, ref value);
		}
		public bool TryGetValue(byte[] key, int offset, int length, out TValue value)
		{
			KeyWalker kw = new KeyWalker(key, offset, length);
			Check(ref kw, "TryGetValue");
			value = default(TValue);
			return base.Find(ref kw, ref value);
		}
		/// <summary>Finds the specified key and returns its associated value. If 
		/// the key did not exist, TryGetValue returns defaultValue instead.</summary>
		public TValue TryGetValue(byte[] key, TValue defaultValue)
		{
			KeyWalker kw = new KeyWalker(key, 0, key.Length);
			base.Find(ref kw, ref defaultValue);
			return defaultValue;
		}
		public TValue TryGetValue(byte[] key, int offset, int length, TValue defaultValue)
		{
			KeyWalker kw = new KeyWalker(key, offset, length);
			Check(ref kw, "TryGetValue");
			base.Find(ref kw, ref defaultValue);
			return defaultValue;
		}

		public TValue this[byte[] key]
		{
			get {
				KeyWalker kw = new KeyWalker(key, key.Length);
				TValue value = default(TValue);
				if (!base.Find(ref kw, ref value))
					throw new KeyNotFoundException(Localize.From("Key not found: ") + key);
				return value;
			}
			set {
				KeyWalker kw = new KeyWalker(key, key.Length);
				base.Set(ref kw, ref value, CPMode.Set | CPMode.Create);
			}
		}

		public ICollection<byte[]> Keys
		{
			get { throw new NotImplementedException(); }
		}
		public ICollection<TValue> Values
		{
			get { throw new NotImplementedException(); }
		}

		public void Add(KeyValuePair<byte[], TValue> item)
		{
			Add(item.Key, item.Value);
		}

		public new void Clear()
		{
			base.Clear();
		}

		public bool Contains(KeyValuePair<byte[], TValue> item)
		{
			KeyWalker kw = new KeyWalker(item.Key, item.Key.Length);
			TValue value = default(TValue);
			if (base.Find(ref kw, ref value))
				return DefaultComparer.Compare(value, item.Value) == 0;
			return false;
		}

		public void CopyTo(KeyValuePair<byte[], TValue>[] array, int arrayIndex)
		{
			foreach (KeyValuePair<byte[], TValue> pair in this)
				array[arrayIndex] = pair;
		}

		public new int Count
		{
			get { return base.Count; }
		}

		public bool IsReadOnly
		{
			get { return false; }
		}

		public bool Remove(KeyValuePair<byte[], TValue> item)
		{
			KeyWalker kw = new KeyWalker(item.Key, item.Key.Length);
			KeyWalker kw2 = kw;
			TValue value = default(TValue);
			if (Find(ref kw, ref value) && DefaultComparer.Compare(value, item.Value) == 0)
				return Remove(ref kw2, ref value);
			return false;
		}

		#endregion

		#region IEnumerable<KeyValuePair<byte[],TValue>> Members

		public Enumerator GetEnumerator()
		{
			return new Enumerator(this);
		}
		IEnumerator<KeyValuePair<byte[], TValue>> IEnumerable<KeyValuePair<byte[], TValue>>.GetEnumerator()
		{
			return GetEnumerator();
		}
		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		#endregion

		public CPByteTrie<TValue> Clone()
			{ return new CPByteTrie<TValue>(this); }

		public Enumerator FindAtLeast(byte[] key)
		{
			KeyWalker kw = new KeyWalker(key, key.Length);
			Enumerator e = new Enumerator(this);
			base.Find(ref kw, e);
			return e;
		}
		public Enumerator FindExact(byte[] key)
		{
			KeyWalker kw = new KeyWalker(key, key.Length);
			Enumerator e = new Enumerator(this);
			if (!base.Find(ref kw, e))
				return null;
			Debug.Assert(e.IsValid);
			return e;
		}
		public bool Find(byte[] key, out Enumerator e)
		{
			KeyWalker kw = new KeyWalker(key, key.Length);
			e = new Enumerator(this);
			return base.Find(ref kw, e);
		}
		public bool Find(byte[] key, int offset, int length, out Enumerator e)
		{
			KeyWalker kw = new KeyWalker(key, offset, length);
			Check(ref kw, "Find");
			e = new Enumerator(this);
			return base.Find(ref kw, e);
		}

		public bool IsEmpty { get { return base.Count == 0; } }

		public class Enumerator : CPEnumerator<TValue>, IEnumerator<KeyValuePair<byte[], TValue>>
		{
			internal protected Enumerator(CPTrie<TValue> trie) : base(trie) {}

			public new KeyValuePair<byte[], TValue> Current
			{
				get {
					return new KeyValuePair<byte[], TValue>(CurrentKey, CurrentValue);
				}
			}
			object System.Collections.IEnumerator.Current
			{
				get { return Current; }
			}
			public new TValue CurrentValue
			{
				get { return base.CurrentValue; }
			}
			public new byte[] CurrentKey
			{
				get {
					int len = Key.Offset + Key.Left;
					return InternalList<byte>.CopyToNewArray(Key.Buffer, len, len);
				}
			}
		}
	}
}
