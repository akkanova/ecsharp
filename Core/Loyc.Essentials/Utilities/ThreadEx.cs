// Threading-related stuff.
//
// Note: this was originally designed to support Compact Framework, but that has
// since been dropped as a design goal.
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
#if !CompactFramework
using System.Runtime.Serialization;
#else
namespace System.Threading
{
	public delegate void ParameterizedThreadStart(object obj);
}
#endif

namespace Loyc.Threading
{
	/// <summary>Creates and controls a thread, and fills in a gap in the
	/// .NET framework by propagating thread-local variables from parent
	/// to child threads, and by providing a ThreadStarting event.</summary>
	/// <remarks>
	/// This class is a decorator for the Thread class and thus a 
	/// drop-in replacement, except that only the most common methods and
	/// properties (both static and non-static) are provided.
	/// <para/>
	/// .NET itself has no support whatsoever from inheriting thread-local 
	/// variables. Not only are thread locals not inherited from the parent
	/// thread, .NET fires no event when a thread starts and a child thread
	/// cannot get the thread ID of the thread that created it.
	/// <para/>
	/// ThreadEx helps work around this problem by automatically propagating
	/// <see cref="ThreadLocalVariable{T}"/> values, and providing the 
	/// <see cref="ThreadStarting"/> event, which blocks the parent thread but
	/// is called in the child thread. This only works if you use <see cref="ThreadEx"/>
	/// to start the child thread; when using other mechanisms such as
	/// <see cref="System.Threading.Tasks.Task"/>, it is possible to copy thread-
	/// local variables from the parent thread using code like this:
	/// <code>
	/// int parentThreadId = Thread.CurrentThread.ManagedThreadId;
	/// var task = System.Threading.Tasks.Task.Factory.StartNew(() => {
	///		using (ThreadEx.PropagateVariables(parentThreadId))
	///			DoSomethingOnChildThread();
	///	});
	///	task.Wait();
	/// </code>
	/// Be careful, however: you should guarantee that, while you copy the 
	/// variables, the parent thread is blocked, or that the parent thread will not 
	/// modify any of them (which may be difficult since variables might exist that
	/// you are unaware of, that you do not control).
	/// <para/>
	/// TLV (thread-local variable) inheritance is needed to use the 
	/// <a href="http://www.codeproject.com/Articles/101411/DI-and-Pervasive-services">
	/// Ambient Service Pattern</a>
	/// </remarks>
	public class ThreadEx
	{
		protected Thread? _parent; // set by Start()
		protected Thread _thread; // underlying thread
		protected ThreadStart? _ts1;
		protected ParameterizedThreadStart? _ts2;
		protected int _startState = 0;
		[ThreadStatic]
		static bool _areThreadVarsInitialized;

		protected internal static List<WeakReference<ThreadLocalVariableBase>> _TLVs = new List<WeakReference<ThreadLocalVariableBase>>();

		/// <summary>
		/// This event is called in the context of a newly-started thread, provided
		/// that the thread is started by the Start() method of this class (rather
		/// than Thread.Start()).
		/// </summary>
		/// <remarks>The Start() method blocks until this event completes.</remarks>
		public static event EventHandler<ThreadStartEventArgs>? ThreadStarting;

		/// <summary>
		/// This event is called when a thread is stopping, if the thread is stopping
		/// gracefully and provided that it was started by the Start() method of this 
		/// class (rather than Thread.Start()).
		/// </summary>
		public static event EventHandler<ThreadStartEventArgs>? ThreadStopping;

		public ThreadEx(ParameterizedThreadStart start)
			{ _thread = new Thread(ThreadStart); _ts2 = start; }
		public ThreadEx(ThreadStart start)
			{ _thread = new Thread(ThreadStart); _ts1 = start; }
		public ThreadEx(ParameterizedThreadStart start, int maxStackSize)
			{ _thread = new Thread(ThreadStart, maxStackSize); _ts2 = start; }
		public ThreadEx(ThreadStart start, int maxStackSize)
			{ _thread = new Thread(ThreadStart, maxStackSize); _ts1 = start; }		

		/// <summary>
		/// Causes the operating system to change the state of the current instance to
		/// System.Threading.ThreadState.Running.
		/// </summary>
		public void Start() { Start(null); }

		/// <summary>
		/// Causes the operating system to change the state of the current instance to
		/// System.Threading.ThreadState.Running. Start() does not return until the
		/// ThreadStarted event is handled.
		/// </summary><remarks>
		/// Once the thread terminates, it CANNOT be restarted with another call to Start.
		/// </remarks>
		public virtual void Start(object? parameter)
		{
			if (Interlocked.CompareExchange(ref _startState, 1, 0) != 0)
				throw new ThreadStateException("The thread has already been started.");

			Debug.Assert(_parent == null);
			_parent = Thread.CurrentThread;

			// (In case this is Compact Framework, can't use ParameterizedThreadStart)
			_startParameter = parameter;
			_thread.Start();
				
			while(_startState == 1)
				Thread.Sleep(0);
		}

		private object? _startParameter;
		protected virtual void ThreadStart()
		{
			object? parameter = _startParameter;
			_startParameter = null;
			Debug.Assert(_thread == Thread.CurrentThread);

			try {
				// Inherit thread-local variables from parent
				InheritThreadLocalVars(_parent!.ManagedThreadId);

				// Note that Start() is still running in the parent thread
				if (ThreadStarting != null)
					ThreadStarting(this, new ThreadStartEventArgs(_parent, this));

				_startState = 2; // allow parent thread to continue

				if (_ts2 != null)
					_ts2(parameter);
				else
					_ts1!();
			} finally {
				_startState = 3; // ensure parent thread continues

				if (ThreadStopping != null)
					ThreadStopping(this, new ThreadStartEventArgs(_parent!, this));

				DeinitThreadLocalVars();
			}
		}

		static bool InheritThreadLocalVars(int parentThreadId)
		{
			int threadId;
			if (!_areThreadVarsInitialized && (threadId = Thread.CurrentThread.ManagedThreadId) != parentThreadId) {
				_areThreadVarsInitialized = true;
				for (int i = 0; i < _TLVs.Count; i++) {
					ThreadLocalVariableBase? v = _TLVs[i].Target();
					if (v != null)
						v.Propagate(parentThreadId, threadId);
				}
				return true;
			}
			return false;
		}
		static void DeinitThreadLocalVars()
		{
			// Notify thread-local variables of termination
			for (int i = 0; i < _TLVs.Count; i++) {
				ThreadLocalVariableBase? v = _TLVs[i].Target();
				if (v != null)
					v.Terminate(Thread.CurrentThread.ManagedThreadId);
			}
		}
		/// <summary>See <see cref="PropagateVariables"/> for more information.</summary>
		public struct ThreadDestructor : IDisposable
		{
			bool _destroyNeeded;
			public ThreadDestructor(bool destroyNeeded) { _destroyNeeded = destroyNeeded; }
			public void Dispose() { if (_destroyNeeded) { DeinitThreadLocalVars(); _destroyNeeded = false; } }
		}

		/// <summary>
		/// Manually initializes <see cref="ThreadLocalVariable{T}"/> objects in a
		/// thread that may not have been started via ThreadEx, propagating values
		/// from the parent thread. Returns an object for uninitializing the thread.
		/// </summary>
		/// <param name="parentThreadId">Id of parent thread. The .NET framework
		/// does not make this information available so you must somehow pass this 
		/// value manually from the parent thread to the child thread.</param>
		/// <returns>An object to be disposed at the end of the thread. This method
		/// can be called in a using statement so that this happens automatically:
		/// <c>using(ThreadEx.PropagateVariables(parentThreadId)) { ... }</c>. It is
		/// important to dispose the returned object so that thread-local values can
		/// be released to prevent a memory leak.
		/// </returns>
		/// <remarks>It is safe to call this method if the thread has already been
		/// initialized. In that case, the thread will not be initialized a second 
		/// time, and the returned value will do nothing when it is disposed.
		/// <para/>
		/// Be careful with this method: you should guarantee that, while you copy the 
		/// variables, the parent thread is blocked, or that the parent thread will not 
		/// modify any of them during the copying process (which may be difficult 
		/// since variables might exist that you are unaware of, that you do not 
		/// control).
		/// </remarks>
		public static ThreadDestructor PropagateVariables(int parentThreadId)
		{
			return new ThreadDestructor(InheritThreadLocalVars(parentThreadId));
		}

		/// <summary>
		/// Gets the currently running thread.
		/// </summary>
		public static Thread CurrentThread { get { return Thread.CurrentThread; } }
		/// <summary>
		/// Gets or sets a value indicating whether or not a thread is a background thread.
		/// </summary>
		public bool IsBackground { get { return _thread.IsBackground; } set { _thread.IsBackground = value; } }
		/// <summary>
		/// Gets a unique identifier for the current managed thread.
		/// </summary>
		public int ManagedThreadId { get { return _thread.ManagedThreadId; } }
		/// <summary>
		/// Gets or sets the name of the thread.
		/// </summary>
		public string? Name { get { return _thread.Name; } set { _thread.Name = value; } }
		/// <summary>
		/// Gets or sets a value indicating the scheduling priority of a thread.
		/// </summary>
		public ThreadPriority Priority { get { return _thread.Priority; } set { _thread.Priority = value; } }
		#if !CompactFramework
		/// <summary>
		/// Gets a value containing the states of the current thread.
		/// </summary>
		public System.Threading.ThreadState ThreadState { get { return _thread.ThreadState; } }
		#endif
		/// <summary>
		/// Raises a System.Threading.ThreadAbortException in the thread on which it
		/// is invoked, to begin the process of terminating the thread while also providing
		/// exception information about the thread termination. Calling this method usually
		/// terminates the thread.
		/// </summary>
		public void Abort(object stateInfo) { _thread.Abort(stateInfo); }
		/// <inheritdoc cref="Abort()"/>
		public void Abort() { _thread.Abort(); }
		/// <summary>
		/// Returns the current domain in which the current thread is running.
		/// </summary>
		public static AppDomain GetDomain() { return Thread.GetDomain(); }
		/// <summary>
		/// Returns a hash code for the current thread.
		/// </summary>
		public override int GetHashCode() { return _thread.GetHashCode(); }
		/// <summary>
		/// Blocks the calling thread until a thread terminates, while continuing to
		/// perform standard COM and SendMessage pumping.
		/// </summary>
		public void Join() { _thread.Join(); }
		/// <summary>
		/// Blocks the calling thread until a thread terminates or the specified time 
		/// elapses, while continuing to perform standard COM and SendMessage pumping. 
		/// </summary>
		public bool Join(int milliseconds) { return _thread.Join(milliseconds); }
		#if !CompactFramework
		public bool Join(TimeSpan timeout) { return _thread.Join(timeout); }
		#endif
		/// <summary>
		/// Suspends the current thread for a specified time.
		/// </summary>
		public static void Sleep(int millisecondsTimeout) { Thread.Sleep(millisecondsTimeout); }

		public Thread Thread { get { return _thread; } }
		public Thread? ParentThread { get { return _parent; } }

		#if !CompactFramework
		public bool IsAlive { 
			get { 
				System.Threading.ThreadState t = ThreadState;
				return t != System.Threading.ThreadState.Stopped &&
				       t != System.Threading.ThreadState.Unstarted &&
				       t != System.Threading.ThreadState.Aborted;
			}
		}
		#endif

		internal static void RegisterTLV(ThreadLocalVariableBase tlv)
		{
			lock(_TLVs) {
				for (int i = 0; i < _TLVs.Count; i++)
					if (!_TLVs[i].IsAlive()) {
						_TLVs[i].SetTarget(tlv);
						return;
					}
				_TLVs.Add(new WeakReference<ThreadLocalVariableBase>(tlv));
			}
		}
	}

	/// <summary>Used by the <see cref="ThreadEx.ThreadStarting"/> and <see cref="ThreadEx.ThreadStopping"/> events.</summary>
	public class ThreadStartEventArgs : EventArgs
	{
		public ThreadStartEventArgs(Thread parent, ThreadEx child) 
			{ ParentThread = parent; ChildThread = child; }
		public Thread ParentThread;
		public ThreadEx ChildThread;
	}

	
	/// <summary>
	/// A fast, tiny 4-byte lock to support multiple readers or a single writer.
	/// Designed for low-contention, high-performance scenarios where reading is 
	/// common and writing is rare.
	/// </summary>
	/// <remarks>
	/// Do not use the default constructor! Use TinyReaderWriterLock.New as the
	/// initial value of the lock.
	/// <para/>
	/// Recursive locking is not supported: the same lock cannot be acquired twice 
	/// for writing on the same thread, nor can a reader lock be acquired after 
	/// the writer lock was acquired on the same thread. If you make either of 
	/// these mistakes, the lock will throw an NotSupportedException.
	/// <para/>
	/// You also cannot acquire a read lock followed recursively by a write lock,
	/// either. Attempting to do so will self-deadlock the thread, bacause 
	/// TinyReaderWriterLock does not track the identity of each reader and is not
	/// aware that it is waiting for the current thread to finish reading.
	/// <para/>
	/// However, multiple reader locks can be acquired on the same thread, just as
	/// multiple reader locks can be acquired by different threads.
	/// <para/>
	/// Make sure you call ExitRead() or ExitWrite() in a finally block! When 
	/// compiled in debug mode, TinyReaderWriterLock will make sure you don't mix
	/// up ExitRead() and ExitWrite().
	/// <para/>
	/// The range of Thread.CurrentThread.ManagedThreadId is undocumented. I have
	/// assumed they don't use IDs close to int.MinValue, so I use values near
	/// int.MinValue to indicate the number of readers holding the lock.
	/// </remarks>
	public struct TinyReaderWriterLock
	{
		public static readonly TinyReaderWriterLock New = new TinyReaderWriterLock { _user = NoUser };

		internal const int NoUser = int.MinValue;
		internal const int MaxReader = NoUser + 65536;
		internal int _user;
		
		/// <summary>Acquires the lock to protect read access to a shared resource.</summary>
		public void EnterReadLock()
		{
			// Fast no-contention case that can probably be inlined
			if (Interlocked.CompareExchange(ref _user, NoUser + 1, NoUser) != NoUser)
				EnterReadLock2();
		}

		private void EnterReadLock2()
		{
			for (;;)
			{
				// Wait for the resource to become available
				int user;
				while ((user = _user) >= MaxReader)
				{
					if (user == Thread.CurrentThread.ManagedThreadId)
						throw new NotSupportedException("TinyReaderWriterLock does not support a reader and writer lock on the same thread");
					Thread.Sleep(0);
				}

				// Try to claim the resource for read access (increment _user)
				if (user == Interlocked.CompareExchange(ref _user, user + 1, user))
					break;
			}
		}

		/// <summary>Releases a read lock that was acquired with EnterRead().</summary>
		public void ExitReadLock()
		{
			Debug.Assert(_user > NoUser && _user <= MaxReader);
			Interlocked.Decrement(ref _user);
		}

		/// <summary>Acquires the lock to protect write access to a shared resource.</summary>
		public void EnterWriteLock()
		{
			EnterWriteLock(Thread.CurrentThread.ManagedThreadId);
		}

		/// <summary>Acquires the lock to protect write access to a shared resource.</summary>
		/// <param name="threadID">Reports the value of Thread.CurrentThread.ManagedThreadId</param>
		public void EnterWriteLock(int threadID)
		{
			// Fast no-contention case that can probably be inlined
			if (Interlocked.CompareExchange(ref _user, threadID, NoUser) != NoUser)
				EnterWriteLock2(threadID);
		}

		private void EnterWriteLock2(int threadID)
		{
			// Wait for the resource to become unused, and claim it
			while (Interlocked.CompareExchange(ref _user, threadID, NoUser) != NoUser)
			{
				if (_user == threadID)
					 throw new NotSupportedException("TinyReaderWriterLock does not support recursive write locks");
				Thread.Sleep(0);
			}
		}

		/// <summary>Releases a write lock that was acquired with EnterWrite().</summary>
		public void ExitWriteLock()
		{
			Debug.Assert(_user == Thread.CurrentThread.ManagedThreadId);
			_user = NoUser;
		}
	}

	/// <summary>When used with ThreadEx, implementing this base class allows you to 
	/// be notified when a child thread is created or terminates.</summary>
	public abstract class ThreadLocalVariableBase
	{
		internal abstract void Propagate(int parentThreadId, int childThreadId);
		internal abstract void Terminate(int threadId);
	}

	/// <summary>Provides access to a thread-local variable through a dictionary 
	/// that maps thread IDs to values.</summary>
	/// <typeparam name="T">Type of variable to wrap</typeparam>
	/// <remarks>
	/// Note: this was written before .NET 4 (which has ThreadLocal{T}). Unlike
	/// <see cref="ThreadLocal{T}"/>, this class supports propagation from parent
	/// to child threads when used with <see cref="ThreadEx"/>.
	/// <para/>
	/// This class exists to solve two problems. First, the [ThreadStatic] 
	/// attribute is not supported in the .NET Compact Framework. Second, and
	/// more importantly, .NET does not propagate thread-local variables when 
	/// creating new threads, which is a huge problem if you want to implement
	/// the <a href="http://loyc-etc.blogspot.com/2010/08/pervasive-services-and-di.html">
	/// Ambient Service Pattern</a>. This class copies the T value from a parent
	/// thread to a child thread, but because .NET provides no way to hook into
	/// thread creation, it only works if you use <see cref="ThreadEx"/> instead 
	/// of standard threads.
	/// <para/>
	/// TODO: figure out how to support .NET's ExecutionContext
	/// <para/>
	/// ThreadLocalVariable implements thread-local variables using a dictionary 
	/// that maps thread IDs to values.
	/// <para/>
	/// Variables of this type are typically static and they must NOT be marked
	/// with the [ThreadStatic] attribute.
	/// <para/>
	/// ThreadLocalVariable(of T) is less convenient than the [ThreadStatic]
	/// attribute, but ThreadLocalVariable works with ThreadEx to propagate the 
	/// value of the variable from parent threads to child threads, and you can
	/// install a propagator function to customize the way the variable is 
	/// copied (e.g. in case you need a deep copy).
	/// <para/>
	/// Despite my optimizations, ThreadLocalVariable is just over half as fast 
	/// as a ThreadStatic variable in CLR 2.0, in a test with no thread 
	/// contention. Access to the dictionary accounts for almost half of the 
	/// execution time; try-finally (needed in case of asyncronous exceptions) 
	/// blocks use up 11%; calling Thread.CurrentThread.ManagedThreadId takes 
	/// about 9%; and the rest, I presume, is used up by the TinyReaderWriterLock.
	/// <para/>
	/// TODO: consider switching from TinyReaderWriterLock+Dictionary to 
	/// ConcurrentDictionary which has fine-grained locking (.NET 4 only).
	/// </remarks>
	public class ThreadLocalVariable<T> : ThreadLocalVariableBase, IMValue<T>
	{
		protected Dictionary<int, T> _tls = new Dictionary<int,T>(5);
		protected TinyReaderWriterLock _lock = TinyReaderWriterLock.New;
		protected Func<T,T> _propagator = delegate(T v) { return v; };
		[AllowNull] // we would like to allow null if and only if T is nullable. There's no way to do it AFAIK
		protected T _fallbackValue;
		protected bool _autoFallback;

		public ThreadLocalVariable()
		{
			ThreadEx.RegisterTLV(this);
		}

		/// <summary>Constructs a ThreadLocalVariable.</summary>
		/// <param name="initialValue">Initial value on the current thread;
		/// also used as the FallbackValue in threads that are not created
		/// via ThreadEx and in other threads that are already running.</param>
		/// <param name="autoFallback">Sets the <see cref="AutoFallbackMode"/> property</param>
		public ThreadLocalVariable(T initialValue, bool autoFallback = false)
			: this(initialValue, initialValue, null) { _autoFallback = autoFallback; }

		/// <summary>Constructs a ThreadLocalVariable.</summary>
		/// <param name="initialValue">Initial value on the current thread. 
		/// Does not affect other threads that are already running.</param>
		/// <param name="fallbackValue">Value to use when a given thread 
		/// doesn't have an associated value.</param>
		/// <param name="propagator">A function that copies (and possibly 
		/// modifies) the Value from a parent thread when starting a new 
		/// thread.</param>
		public ThreadLocalVariable(T initialValue, T fallbackValue, Func<T, T>? propagator)
		{
			_fallbackValue = fallbackValue;
			Value = initialValue;
			if (propagator != null)
				_propagator = propagator;
			ThreadEx.RegisterTLV(this);
		}

		internal override void Propagate(int parentThreadId, int childThreadId)
		{
			T? value;

			_lock.EnterWriteLock();
			try {
				if (_tls.TryGetValue(parentThreadId, out value))
					_tls[childThreadId] = _propagator(value);
			} finally {
				_lock.ExitWriteLock();
			}
		}
		internal override void Terminate(int threadId)
		{
			_lock.EnterWriteLock();
			try {
				_tls.Remove(CurrentThreadId);
			} finally {
				_lock.ExitWriteLock();
			}
		}

		internal int CurrentThreadId 
		{
			get { return Thread.CurrentThread.ManagedThreadId; } 
		}

		public bool HasValue
		{
			get {
				_lock.EnterReadLock();
				try {
					return _tls.ContainsKey(CurrentThreadId);
				} finally {
					_lock.ExitReadLock();
				}
			}
		}

		/// <summary>Value of the thread-local variable.</summary>
		/// <remarks>
		/// This property returns FallbackValue if no value exists for this thread.
		/// </remarks>
		public T Value { 
			get {
				_lock.EnterReadLock();
				T? value;
				// Wrapping in a try-finally hurts performance by about 11% in a 
				// Release build. Even though TryGetValue doesn't throw, an 
				// asynchronous thread abort is theoretically possible :(
				try {
					if (!_tls.TryGetValue(CurrentThreadId, out value))
						return _fallbackValue;
				} finally {
					_lock.ExitReadLock();
				}
				return value;
			}
			set {
				int threadID = Thread.CurrentThread.ManagedThreadId;
				_lock.EnterWriteLock(threadID);
				try {
					_tls[threadID] = value;
					if (_autoFallback)
						_fallbackValue = value;
				} finally {
					_lock.ExitWriteLock();
				}
			}
		}

		/// <summary>
		/// When a thread is not created using ThreadEx, the value of your
		/// ThreadLocalVariable fails to propagate from the parent thread to the 
		/// child thread. In that case, Value takes on the value of FallbackValue
		/// the first time it is called.
		/// </summary>
		/// <remarks>
		/// By default, the FallbackValue is the initialValue passed to the 
		/// constructor.
		/// </remarks>
		public T FallbackValue
		{
			get {
				_lock.EnterReadLock();
				try {
					return _fallbackValue;
				} finally {
					_lock.ExitReadLock();
				}
			}
			set {
				_lock.EnterWriteLock();
				try {
					_fallbackValue = value;
				} finally {
					_lock.ExitWriteLock();
				}
			}
		}
		
		/// <summary>Returns true if this variable was created in "auto-fallback" 
		/// mode, which means that setting the <see cref="Value"/> property will 
		/// simultaneously set the <see cref="FallbackValue"/> to the same value 
		/// at the same time.</summary>
		public bool AutoFallbackMode
		{
			get { return _autoFallback; }
		}
	}


}
