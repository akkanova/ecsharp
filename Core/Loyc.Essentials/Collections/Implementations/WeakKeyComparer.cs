using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Loyc.Collections
{
	// Used by WeakKeyDictionary<K,V>
	/// <summary>
    /// Compares objects of the given type or WeakKeyReferences to them
    /// for equality based on the given comparer. Note that we can only
	/// implement <see cref="IEqualityComparer{T}"/> for T = object as there is no 
	/// other common base between T and <see cref="WeakKeyReference{T}"/>. We need a
    /// single comparer to handle both types because we don't want to
    /// allocate a new weak reference for every lookup.</summary>
	/// <remarks>Source: datavault project. License: Apache License 2.0</remarks>
    public sealed class WeakKeyComparer<T> : IEqualityComparer<object>
        where T : class
    {
        private IEqualityComparer<T> comparer;

        internal WeakKeyComparer(IEqualityComparer<T> comparer)
        {
            if (comparer == null)
                comparer = EqualityComparer<T>.Default;

            this.comparer = comparer;
        }

        public int GetHashCode(object obj)
        {
            WeakKeyReference<T>? weakKey = obj as WeakKeyReference<T>;
            if (weakKey != null) return weakKey.HashCode;
            return this.comparer.GetHashCode((T)obj);
        }

        // Note: There are actually 9 cases to handle here.
        //
        //  Let Wa = Alive Weak Reference
        //  Let Wd = Dead Weak Reference
        //  Let S  = Strong Reference
        //  
        //  x  | y  | Equals(x,y)
        // -------------------------------------------------
        //  Wa | Wa | comparer.Equals(x.Target, y.Target) 
        //  Wa | Wd | false
        //  Wa | S  | comparer.Equals(x.Target, y)
        //  Wd | Wa | false
        //  Wd | Wd | x == y
        //  Wd | S  | false
        //  S  | Wa | comparer.Equals(x, y.Target)
        //  S  | Wd | false
        //  S  | S  | comparer.Equals(x, y)
        // -------------------------------------------------
        public new bool Equals(object? x, object? y)
        {
            bool xIsDead, yIsDead;
            T? first = GetTarget(x, out xIsDead);
            T? second = GetTarget(y, out yIsDead);

            if (xIsDead)
                return yIsDead ? x == y : false;

            if (yIsDead)
                return false;

            return this.comparer.Equals(first, second);
        }

        private static T? GetTarget(object? obj, out bool isDead)
        {
            WeakKeyReference<T>? wref = obj as WeakKeyReference<T>;
            T? target;
            if (wref != null)
            {
                target = wref.Target;
                isDead = !wref.IsAlive;
            }
            else
            {
                target = (T?)obj;
                isDead = false;
            }
            return target;
        }
    }

	/// <summary>Provides a weak reference to an object of the given type to be used 
    /// in a WeakDictionary along with the given comparer.</summary>
	/// <remarks>Source: datavault project. License: Apache License 2.0
	/// <para/>
	/// DLP Updated 2014-May-21: WeakReference{T} in .NET 4.5 is sealed, but this
	/// code relied on the ability to derive WeakKeyReference{T} from WeakReference{T}.
	/// Workaround: derive WeakKeyReference{T} from WeakReference instead.
	/// </remarks>
    public sealed class WeakKeyReference<T> : WeakReference where T : class
    {
        public readonly int HashCode;

        public WeakKeyReference(T key, WeakKeyComparer<T> comparer)
            : base(key)
        {
            // retain the object's hash code immediately so that even
            // if the target is GC'ed we will be able to find and
            // remove the dead weak reference.
            this.HashCode = comparer.GetHashCode(key);
        }

		public new T? Target {
			get { return (T?)base.Target; }
			set { base.Target = value; }
		}
    }
}
