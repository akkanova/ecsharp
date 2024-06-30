using static System.Math;
using Loyc.Collections;
using Loyc.Collections.Impl;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Buffers.Binary;
using System.Numerics;

namespace Loyc.SyncLib;

partial class SyncBinary
{
	internal partial class ReaderState
	{
		const string UnexpectedDataStreamFormat = "Unexpected binary data format";

		public ReaderState(IScanner<byte> scanner, Options options)
		{
			_scanner = scanner;
			_opt = options;
		}

		public ReaderState(ReadOnlyMemory<byte> bytes, Options options)
		{
			_frame = new ReadingFrame { Buf = bytes };
			_opt = options;
		}

		private Options _opt;
		private IScanner<byte>? _scanner;
		private Memory<byte> _scannerBuf; // not used by ReaderState; it's passed to _scanner.Read()
		private ReadingFrame _frame;
		private InternalList<StackEntry> _stack = new InternalList<StackEntry>(4);
		public bool IsInsideList;
		public int Depth { get; internal set; }
		
		// This is only created if an object with an ID (marked with '#') is encountered.
		// It maps previously-encountered object IDs to objects.
		private Dictionary<int, object>? _objects { get; set; }

		// An error that left the stream unreadable
		protected Exception? _fatalError;

		#region Helper types

		private struct ReadingFrame
		{
			// The buffer being read from (something returned by _scanner if it isn't null)
			public ReadOnlyMemory<byte> Buf;
			// The currrent position as an index into Buf.
			public int Index;
			// Location within the JSON file of Buf.Span[0] (used for error reporting)
			public long PositionOfBuf0;
			// A location in Buf that shouldn't be unloaded when reading further into the
			// file (int.MaxValue if none)
			public int ObjectStartIndex; // = int.MaxValue;

			public ReadingPointer Pointer => new ReadingPointer {
				Buf = Buf.Span,
				Index = Index,
			};
	}

		protected ref struct ReadingPointer
		{
			/// <summary>The part of the file that is currently loaded.</summary>
			public ReadOnlySpan<byte> Buf;
			/// <summary>The currrent position as an index into Buf.</summary>
			public int Index;
			/// <summary>Current byte in data stream</summary>
			public byte Byte => Buf[Index];
			public int BytesLeft => Buf.Length - Index;

			public ReadOnlySpan<byte> Span => Buf.Slice(Index);
			public ReadOnlySpan<byte> Slice(int offset, int size) => Buf.Slice(Index + offset, size);
			//public ReadOnlySpan<byte> FromOffset(int offset) => Buf.Slice(Index + offset);
		}

		protected struct StackEntry
		{
			public int Id;
			public bool HasId;
			public bool IsList;
		}

		#endregion

		#region Input buffer management: AutoRead, ExpectBytes, Commit

		// The scanner could choose a much larger size, but this is the minimum we'll tolerate
		const int DefaultMinimumScanSize = 32;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void ExpectBytes(ref ReadingPointer cur, int requiredBytes)
		{
			if (!AutoRead(ref cur, requiredBytes))
				ThrowUnexpectedEOF(cur.Index);
		}

		// Ensures that at least `extraLookahead + 1` bytes are available in cur.Buf
		// starting at cur.Index. On return, _i < _buf.Length if it returns true.
		// Returns false if the request could not be satisfied.
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private bool AutoRead(ref ReadingPointer cur, int requiredBytes)
		{
			Debug.Assert(cur.Buf == _frame.Buf.Span);
			if ((uint)(cur.Index + requiredBytes) <= (uint)cur.Buf.Length)
				return true;

			return ReadMoreBytes(ref cur, (int)requiredBytes);
		}

		// Reads new data into _frame.Buf if possible
		[MethodImpl(MethodImplOptions.NoInlining)]
		private bool ReadMoreBytes(ref ReadingPointer cur, int requiredBytes)
		{
			Debug.Assert(cur.BytesLeft < requiredBytes);
			if (_scanner == null)
				return false;

			int requestSize = Max(requiredBytes, DefaultMinimumScanSize);
			int skip = Min(cur.Index, _frame.ObjectStartIndex);

			_frame.Buf = _scanner.Read(skip, (cur.Index -= skip) + requestSize, ref _scannerBuf);

			if (_frame.ObjectStartIndex != int.MaxValue)
				_frame.ObjectStartIndex -= skip;

			cur.Buf = _frame.Buf.Span;
			return cur.BytesLeft >= requiredBytes;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void Commit(in ReadingPointer cur)
		{
			Debug.Assert(_frame.Buf.Span == cur.Buf);
			_frame.Index = cur.Index;
		}

		#endregion

		#region BeginSubObject/EndSubObject

		internal (bool Begun, int Length, object? Object) BeginSubObject(ObjectMode mode, int tupleLength)
		{
			//if (childKey == null && (mode & (ObjectMode.NotNull | ObjectMode.Deduplicate)) != ObjectMode.NotNull) {
			//	WriteNull();
			//	return (false, childKey);
			//}

			//if (listLength < 0 && (mode & ObjectMode.List) != 0) {
			//	throw new ArgumentException("No valid listLength was given to SyncBinary.Writer.BeginSubObject");
			//}

			//Span<byte> span;
			//if ((mode & ObjectMode.Deduplicate) != 0) {
			//	Debug.Assert(childKey != null);
			//	long id = _idGen.GetId(childKey, out bool firstTime);

			//	if (firstTime) {
			//		// Write object ID
			//		span = GetOutSpan(MaxSizeOfInt64 + 2);
			//		span[_i++] = (byte)'#';
			//		Write(id);
			//	} else {
			//		// Write backreference to object ID
			//		span = GetOutSpan(MaxSizeOfInt64 + 1);
			//		span[_i++] = (byte)'@';
			//		Write(id);

			//		return (false, childKey); // Skip object that is already written
			//	}
			//} else {
			//	span = GetOutSpan(1);
			//}

			//// Take note than an object has been started
			//_stack.Add(mode);

			//// Write start marker (if enabled in _opt) and list length (if applicable)
			//ObjectMode objectKind = mode & (ObjectMode.List | ObjectMode.Tuple);
			//if (objectKind == ObjectMode.Normal) {
			//	if ((_opt.Markers & Markers.ObjectStart) != 0)
			//		span[_i++] = (Depth & 1) != 0 ? (byte)'(' : (byte)'{';
			//} else if (objectKind == ObjectMode.List) {
			//	if ((_opt.Markers & Markers.ListStart) != 0)
			//		span[_i++] = (byte)'[';

			//	Write(listLength);
			//} else if (objectKind == ObjectMode.Tuple) {
			//	if ((_opt.Markers & Markers.TupleStart) != 0)
			//		span[_i++] = (byte)'[';
			//}

			//return (true, childKey);
			throw new NotImplementedException();
		}

		#endregion

		#region Variable-length integer readers
		// Here we assume the JIT optimizes away tests like `if (typeof(TInt) == typeof(int))`.

		// Reads an integer from the data stream. `TInt` MUST be int, uint, long or ulong.
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal TInt ReadNormalInt<TInt>() where TInt : struct
		{
			var cur = _frame.Pointer;
			TInt? value = DecodeIntOrNull<TInt>(ref cur);
			Commit(cur);
			return value.HasValue ? value.Value : UnexpectedNull<TInt>();
		}

		// Reads an integer/null from the data stream. `TInt` MUST be int, uint, long or ulong.
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal TInt? ReadNormalIntOrNull<TInt>() where TInt : struct
		{
			var cur = _frame.Pointer;
			TInt? value = DecodeIntOrNull<TInt>(ref cur);
			Commit(cur);
			return value;
		}

		// Reads a small integer/null from the data stream. `TInt` MUST be byte, sbyte, short or ushort.
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal TShort? ReadSmallIntOrNull<TShort>() where TShort : struct
		{
			var cur = _frame.Pointer;
			int? value = DecodeIntOrNull<int>(ref cur);

			if (value.HasValue) {
				MaybeThrowIntegerOverflowIf(
					typeof(TShort) == typeof(short) && (short)value.Value != value ||
					typeof(TShort) == typeof(ushort) && (ushort)value.Value != value ||
					typeof(TShort) == typeof(sbyte) && (sbyte)value.Value != value ||
					typeof(TShort) == typeof(byte) && (byte)value.Value != value,
					// TODO: check whether using typeof(TShort).Name harms performance
					typeof(TShort).Name, _frame.Index);
				Commit(cur);
				return ShortenInt<TShort>(value.Value);
			} else {
				Commit(cur);
				return null;
			}
		}

		// Reads a small integer from the data stream. `TInt` MUST be byte, sbyte, short or ushort.
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal TShort ReadSmallInt<TShort>() where TShort : struct
		{
			var cur = _frame.Pointer;
			int? value = DecodeIntOrNull<int>(ref cur);

			if (value.HasValue) {
				MaybeThrowIntegerOverflowIf(
					typeof(TShort) == typeof(short) && (short)value.Value != value ||
					typeof(TShort) == typeof(ushort) && (ushort)value.Value != value ||
					typeof(TShort) == typeof(sbyte) && (sbyte)value.Value != value ||
					typeof(TShort) == typeof(byte) && (byte)value.Value != value,
					typeof(TShort).Name, _frame.Index);
				Commit(cur);
				return ShortenInt<TShort>(value.Value);
			} else {
				Commit(cur);
				return UnexpectedNull<TShort>();
			}
		}

		// Decodes an integer from the data stream. `TInt` MUST be int, uint, long or ulong.
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		TInt? DecodeIntOrNull<TInt>(ref ReadingPointer cur) where TInt : struct
		{
			ExpectBytes(ref cur, 1);

			byte firstByte = cur.Byte;
			if (firstByte < 0xFE)
			{
				cur.Index++;
				if (firstByte < 0x80)
				{
					// Simply return firstByte if TInt is unsigned, otherwise sign-extend it
					if (typeof(TInt) == typeof(uint))
						return (uint)firstByte is TInt r ? r : default;
					else if (typeof(TInt) == typeof(ulong))
						return (ulong)firstByte is TInt r ? r : default;
					else if (typeof(TInt) == typeof(int))
						return ((int)firstByte << 25) >> 25 is TInt r ? r : default;
					else if (typeof(TInt) == typeof(long))
						return (long)(((int)firstByte << 25) >> 25) is TInt r ? r : default;
					Debug.Fail("unreachable");
					return default;
				}
				else
				{
					// Read small-format variable-length number (sizeOfRest <= 7). We
					// always read it as `long` because if sizeOfRest >= 4, a 32-bit
					// version of this would require more branches. Any speed advantage of
					// using 32-bit math would probably be negated by branch misprediction,
					// even on mobile processors.
					int sizeOfRest = LeadingOneCount(firstByte);
					
					ExpectBytes(ref cur, sizeOfRest);

					int extraBits = firstByte & (0x7F >> sizeOfRest);

					long highBitsOfNumber;
					if (typeof(TInt) == typeof(int) || typeof(TInt) == typeof(long)) {
						int shift = sizeOfRest + 57;
						highBitsOfNumber = (long)extraBits << shift >> (shift - sizeOfRest * 8);
					} else { // unsigned
						highBitsOfNumber = (long)extraBits << sizeOfRest * 8;
					}
						
					long number = highBitsOfNumber | (long)ReadRemainingBytesAsBigEndian(ref cur, sizeOfRest);

					if (typeof(TInt) == typeof(int))
						MaybeThrowIntegerOverflowIf((int)number != number, "Int32", cur.Index);
					if (typeof(TInt) == typeof(uint))
						MaybeThrowIntegerOverflowIf((uint)number != number, "UInt32", cur.Index);

					return FromLong<TInt>(number);
				}
			}
			else if (firstByte == 0xFF)
			{
				return null;
			}
			else
			{
				long number = DecodeLargeFormatInt64(ref cur, typeof(TInt) == typeof(int) || typeof(TInt) == typeof(long));
				return FromLong<TInt>(number);
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		long DecodeLargeFormatInt64(ref ReadingPointer cur, bool signed)
		{
			// Read length prefix
			Debug.Assert(cur.Byte == 0xFE);
			cur.Index++;

			if (!AutoRead(ref cur, 2)) {
				ThrowUnexpectedEOF(_frame.Index);
			}
			if (cur.Byte >= 0xFE) {
				ThrowError(cur.Index, cur.Byte == 0xFF
					? $"{UnexpectedDataStreamFormat}; number length is null"
					: $"{UnexpectedDataStreamFormat}; length prefix is itself length-prefixed");
			}

			int integerSize = (int) DecodeIntOrNull<uint>(ref cur)!.Value;
			if (integerSize > _opt.MaxNumberSize) {
				ThrowError(_frame.Pointer.Index, $"{UnexpectedDataStreamFormat}; length prefix is too large: {integerSize}");
			}

			// Read the integer itself
			ExpectBytes(ref cur, integerSize);

			if (integerSize > 8) {
				if (!_opt.Read.SilentlyTruncateLargeNumbers)
					ExpectZeroes(cur.Index, cur.Slice(0, integerSize - 8));
				
				cur.Index += integerSize - 8;
				integerSize = 8;
			}

			long number = (long) ReadRemainingBytesAsBigEndian(ref cur, integerSize);
			cur.Index += integerSize;

			if (signed) {
				// Sign-extend the number
				int shiftAmount = 64 - integerSize * 8;
				return number << shiftAmount >> shiftAmount;
			} else {
				return number;
			}
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static ulong ReadRemainingBytesAsBigEndian(ref ReadingPointer cur, int sizeOfRemaining)
		{
			Debug.Assert(sizeOfRemaining <= 8);
			Debug.Assert(cur.BytesLeft >= sizeOfRemaining);

			if (cur.BytesLeft >= 8)
			{
				// Fast branchless path: read 8 bytes, then discard any that
				// are not part of the number. However, special logic is needed when
				// reading a 32-bit number if the number size is 5 bytes or more...
				ulong number = BigEndianBytesToUInt64(cur.Span) >> ((8 - sizeOfRemaining) << 3);
				cur.Index += sizeOfRemaining;
				return number;
			}
			return ReadInLoop(ref cur, sizeOfRemaining);

			[MethodImpl(MethodImplOptions.NoInlining)]
			static ulong ReadInLoop(ref ReadingPointer cur, int sizeOfRemaining)
			{
				// Slow path for tiny buffer: read bytes in a loop
				ulong number = 0;
				for (int i = 0; i < sizeOfRemaining; i++) {
					number = (number << 8) + cur.Byte;
					cur.Index++;
				}
				return number;
			}
		}

		void ExpectZeroes(int index, ReadOnlySpan<byte> span)
		{
			for (int i = 0; i < span.Length; i++) {
				if (span[i] != 0)
					ThrowError(index + i, $"{UnexpectedDataStreamFormat}; integer is too large");
			}
		}

		TInt UnexpectedNull<TInt>(bool fatal = false)
		{
			if (!_opt.Read.ReadNullPrimitivesAsDefault)
				ThrowError(_frame.Index, $"{UnexpectedDataStreamFormat}; unexpected null", fatal);

			return default;
		}

		static uint BigEndianBytesToUInt32(ReadOnlySpan<byte> span)
		{
			#if NETSTANDARD2_0 || NET45 || NET46 || NET47 || NET48
			return (uint)(span[3] + (span[2] << 8) + (span[1] << 16) + (span[0] << 24));
			#else
			return (uint)BinaryPrimitives.ReadInt32BigEndian(span);
			#endif
		}

		static ulong BigEndianBytesToUInt64(ReadOnlySpan<byte> span)
		{
			#if NETSTANDARD2_0 || NET45 || NET46 || NET47 || NET48
			return unchecked((ulong)BigEndianBytesToUInt32(span) << 32) + BigEndianBytesToUInt32(span.Slice(4));
			#else
			return (ulong)BinaryPrimitives.ReadInt64BigEndian(span);
			#endif
		}

		//[MethodImpl(MethodImplOptions.AggressiveInlining)]
		//static TInt BigEndianBytesToUInt<TInt>(ReadOnlySpan<byte> span, int integerSize)
		//{
		//	if (typeof(TInt) == typeof(int)) {
		//		return (int)(BigEndianBytesToUInt32(span) >> (32 - integerSize * 8)) is TInt r ? r : default;
		//	} else if (typeof(TInt) == typeof(uint)) {
		//		return (uint)(BigEndianBytesToUInt32(span) >> (32 - integerSize * 8)) is TInt r ? r : default;
		//	} else if (typeof(TInt) == typeof(long)) {
		//		return (long)(BigEndianBytesToUInt64(span) >> (64 - integerSize * 8)) is TInt r ? r : default;
		//	} else if (typeof(TInt) == typeof(ulong)) {
		//		return (ulong)(BigEndianBytesToUInt64(span) >> (64 - integerSize * 8)) is TInt r ? r : default;
		//	}
		//	Debug.Fail("Unreachable");
		//	return default;
		//}

		#endregion

		#region Generic math functions
		// When increase the minimum .NET version to .NET 7, we can switch to generic
		// numerics instead (but will have to retest performance)

		//[MethodImpl(MethodImplOptions.AggressiveInlining)]
		//static int SizeOf<TInt>()
		//{
		//	if (typeof(TInt) == typeof(int) || typeof(TInt) == typeof(uint))
		//		return 4;
		//	else if (typeof(TInt) == typeof(long) || typeof(TInt) == typeof(ulong))
		//		return 8;
		//	else
		//		return System.Runtime.InteropServices.Marshal.SizeOf<TInt>();
		//}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static TInt FromLong<TInt>(long number) where TInt: struct
		{
			if (typeof(TInt) == typeof(int))
				return (int)number is TInt r ? r : default;
			else if (typeof(TInt) == typeof(uint))
				return (uint)number is TInt r ? r : default;
			else if (typeof(TInt) == typeof(long))
				return (long)number is TInt r ? r : default;
			else if (typeof(TInt) == typeof(ulong))
				return (ulong)number is TInt r ? r : default;
			Debug.Fail("Unreachable");
			return default;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static TInt ShortenInt<TInt>(int number) where TInt: struct
		{
			if (typeof(TInt) == typeof(short))
				return (short)number is TInt r ? r : default;
			else if (typeof(TInt) == typeof(ushort))
				return (ushort)number is TInt r ? r : default;
			else if (typeof(TInt) == typeof(sbyte))
				return (sbyte)number is TInt r ? r : default;
			else if (typeof(TInt) == typeof(byte))
				return (byte)number is TInt r ? r : default;
			Debug.Fail("Unreachable");
			return default;
		}

		//[MethodImpl(MethodImplOptions.AggressiveInlining)]
		//static TInt ShiftRight<TInt>(TInt num, int amount)
		//{
		//	if (num is int i32) {
		//		return (i32 >> amount) is TInt r ? r : default;
		//	} else if (num is uint u32) {
		//		return (u32 >> amount) is TInt r ? r : default;
		//	} else if (num is long i64) {
		//		return (i64 >> amount) is TInt r ? r : default;
		//	} else if (num is ulong u64) {
		//		return (u64 >> amount) is TInt r ? r : default;
		//	}
		//	Debug.Fail("Unreachable");
		//	return default;
		//}

		public static int LeadingOneCount(byte b)
		{
			#if NETSTANDARD2_0 || NETSTANDARD2_1 || NETCOREAPP2_0 || NETCOREAPP2_1 || NETCOREAPP2_2 || NET45 || NET46 || NET47 || NET48
				int result = 0;
				int i = b << 24;
				if (i >> 28 == 0b1111)
				{
					i <<= 4;
					result += 4;
				}
				if (i >> 30 == 0b11)
				{
					i <<= 2;
					result += 2;
				}
				if (i >> 31 == 1)
				{
					i <<= 1;
					result += 1;
					if (result == 7 && i == 1)
						return 8;
				}
				return result;
			#else
				return BitOperations.LeadingZeroCount((uint)~(b << 24));
			#endif
		}

		#endregion

		#region Error throwers

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void MaybeThrowIntegerOverflowIf(bool overflow, string expectedSize, int curIndex)
		{
			if (!_opt.Read.SilentlyTruncateLargeNumbers && overflow)
				ThrowIntegerOverflow(expectedSize, curIndex);
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		void ThrowIntegerOverflow(string expectedSize, int curIndex)
		{
			ThrowError(curIndex, $"{UnexpectedDataStreamFormat}; number is too large (expected {expectedSize})", fatal: false);
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private void ThrowUnexpectedEOF(int index)
			=> ThrowError(index, "Data stream ended unexpectedly");

		[MethodImpl(MethodImplOptions.NoInlining)]
		protected void ThrowError(int i, string msg, bool fatal = false)
			=> throw NewError(i, msg, fatal);

		[MethodImpl(MethodImplOptions.NoInlining)]
		protected void ThrowError(long position, string msg, bool fatal = false)
			=> throw NewError(position, msg, fatal);

		protected Exception NewError(int i, string msg, bool fatal = false)
			=> NewError(_frame.PositionOfBuf0 + i, msg, fatal);

		[MethodImpl(MethodImplOptions.NoInlining)]
		protected Exception NewError(long position, string msg, bool fatal = false)
		{
			if (_fatalError != null)
				return _fatalError; // New error is just a symptom of the old error; rethrow

			string msg2;
			int index = (int)(position - _frame.PositionOfBuf0);
			if ((uint)index >= (uint)_frame.Buf.Length) {
				msg2 = "{0} (at byte {1})".Localized(msg, position);
			} else {
				byte b = _frame.Buf.Span[index];
				msg2 = "{0} (at byte {1} '{2}')".Localized(msg, position, b < 32 || b >= 127 ? "0x" + b.ToString("X") : (char)b);
			}

			var exc = new FormatException(msg);
			exc.Data["position"] = position;
			exc.Data["recoverable"] = false;

			if (fatal)
				_fatalError = exc;
			return exc;
		}

		#endregion

		internal void SetCurrentObject(object value)
		{
			if (_stack.Count != 0) {
				var topOfStack = _stack.Last;
				if (topOfStack.HasId) {
					_objects ??= new Dictionary<int, object>();
					_objects[topOfStack.Id] = value;
				}
			}
		}
	}
}