using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Loyc.Collections.Impl
{
	/// <summary>A base class for user-defined dictionaries that want to implement 
	/// both <c>IDictionary(K,V)</c> and <c>IReadOnlyDictionary(K, V)</c>.</summary>
	/// <remarks>Modified version of source: datavault project. License: Apache License 2.0.</remarks>
    [DebuggerDisplay("Count = {Count}")]
    [DebuggerTypeProxy(PREFIX + "DictionaryDebugView`2" + SUFFIX)]
    public abstract class DictionaryBase<TKey, TValue> : IDictionaryAndReadOnly<TKey, TValue>
		where TKey: notnull
    {
        private const string PREFIX = "System.Collections.Generic.Mscorlib_";
        private const string SUFFIX = ",mscorlib,Version=2.0.0.0,Culture=neutral,PublicKeyToken=b77a5c561934e089";

        public abstract int Count { get; }
        public abstract void Clear();
        public abstract void Add(TKey key, TValue value);
        public abstract bool ContainsKey(TKey key);
        public abstract bool Remove(TKey key);
        public abstract bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value);
        public abstract IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator();
        /// <summary>Implementation of the setter for this[].</summary>
        /// <remarks>The setter alone (without the getter) is not allowed to be virtual 
        /// in C# so a separate method is required.</remarks>
        protected abstract void SetValue(TKey key, TValue value);

        public bool IsReadOnly => false;
        public bool IsEmpty => Count != 0;
        public TValue TryGet(TKey key, [MaybeNullWhen(false)] out bool fail)
        {
            fail = !TryGetValue(key, out TValue? value);
            return value!;
        }

        public ICollection<TKey> Keys
        {
            get { return new KeyCollection<TKey, TValue>(this); }
        }
        public ICollection<TValue> Values
        {
            get { return new ValueCollection<TKey, TValue>(this); }
        }
        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys
        {
            get { return new KeyCollection<TKey, TValue>(this); }
        }
        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values
        {
            get { return new ValueCollection<TKey, TValue>(this); }
        }

        public TValue this[TKey key]
        {
            get
            {
                TValue? value;
                if (!this.TryGetValue(key, out value))
                    throw new KeyNotFoundException();

                return value;
            }
            set
            {
                SetValue(key, value);
            }
        }

        public void Add(KeyValuePair<TKey, TValue> item)
        {
            this.Add(item.Key, item.Value);
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            TValue? value;
            if (!this.TryGetValue(item.Key, out value))
                return false;

            return EqualityComparer<TValue>.Default.Equals(value, item.Value);
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            Copy(this, array, arrayIndex);
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            if (!this.Contains(item))
                return false;

            return this.Remove(item.Key);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        private static void Copy<T>(ICollection<T> source, T[] array, int arrayIndex)
        {
            if (array == null)
                CheckParam.ThrowArgumentNull(nameof(array));
            if ((uint)arrayIndex > (uint)array.Length)
                CheckParam.ThrowOutOfRange(nameof(arrayIndex));
            if ((array.Length - arrayIndex) < source.Count)
                CheckParam.ThrowBadArgument("Destination array is not large enough. Check array.Length and arrayIndex.");

            foreach (T item in source)
                array[arrayIndex++] = item;
        }
    }
}
