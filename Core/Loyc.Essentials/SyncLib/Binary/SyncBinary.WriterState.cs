using Loyc.SyncLib.Impl;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Loyc.SyncLib;

partial class SyncBinary
{
	internal class WriterState : WriterStateBase
	{
		internal Options _opt;
		internal Options.ForWriter _optWrite;
		
		internal bool _isInsideList;

		internal int _depth;
		internal object _currentObject;

		// Number of bits in _buf[_i - 1] that have not yet been used, and which could be
		// used by a bitfield.
		private uint _bitfieldBitsLeftInByte = 0;

		public WriterState(IBufferWriter<byte> output, Options options) : base(output)
		{
			_opt = options;
			_optWrite = _opt.Write;
		}


		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		protected new Span<byte> GetOutSpan(int requiredBytes)
		{
			_bitfieldBitsLeftInByte = 0;
			return base.GetOutSpan(requiredBytes);
		}

		//[MethodImpl(MethodImplOptions.AggressiveInlining)]
		//protected Span<byte> GetBitfieldOutSpan(int requiredBytes)
		//{
		//	Debug.Assert(_i > 0);
		//	if (_i + requiredBytes < _buf.Length) {
		//		return _buf.Span;
		//	} else {
		//		return MostlyFlushAndGetOutSpan(requiredBytes);
		//	}
		//}
		//[MethodImpl(MethodImplOptions.NoInlining)]
		//protected Span<byte> MostlyFlushAndGetOutSpan(int requiredBytes)
		//{
		//	_output.Advance(_i - 1);
		//	_i = 1;
		//	_buf = _output.GetMemory(System.Math.Max(requiredBytes + 1, MinimumBufSize));
		//	return _buf.Span;
		//}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void WriteNull()
		{
			GetOutSpan(1)[_i++] = 255;
		}

		public void Write(bool value)
		{
			GetOutSpan(1)[_i++] = (byte)(value ? 1 : 0);
		}

		public void WriteNullable(bool? value)
		{
			GetOutSpan(1)[_i++] = (byte)(value == null ? 255 : value.Value ? 1 : 0);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void WriteNullable(sbyte? num)
		{
			if (num == null)
				WriteNull();
			else
				Write(num.Value);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void WriteNullable(short? num)
		{
			if (num == null)
				WriteNull();
			else
				Write(num.Value);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void WriteNullable(int? num)
		{
			if (num == null)
				WriteNull();
			else
				Write(num.Value);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void WriteNullable(long? num)
		{
			if (num == null)
				WriteNull();
			else
				Write(num.Value);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void WriteNullable(byte? num)
		{
			if (num == null)
				WriteNull();
			else
				Write(num.Value);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void WriteNullable(ushort? num)
		{
			if (num == null)
				WriteNull();
			else
				Write(num.Value);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void WriteNullable(uint? num)
		{
			if (num == null)
				WriteNull();
			else
				Write(num.Value);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void WriteNullable(ulong? num)
		{
			if (num == null)
				WriteNull();
			else
				Write(num.Value);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void WriteNullable(BigInteger? num)
		{
			if (num == null)
				WriteNull();
			else
				Write(num.Value);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void WriteNullable(float? num)
		{
			if (num == null)
				WriteNull();
			else
				Write(num.Value);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void WriteNullable(double? num)
		{
			if (num == null)
				WriteNull();
			else
				Write(num.Value);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void WriteNullable(decimal? num)
		{
			if (num == null)
				WriteNull();
			else
				Write(num.Value);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Write(int num)
		{
			// Fast path for small non-negative numbers
			if ((uint)num < 64)
				GetOutSpan(1)[_i++] = (byte)num;
			else
				WriteSignedOrUnsigned((uint)num, (uint)(num >= 0 ? num : ~num));
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Write(uint num)
		{
			// Fast path for small numbers
			if (num < 128)
				GetOutSpan(1)[_i++] = (byte)num;
			else
				WriteSignedOrUnsigned(num, num);
		}

		public void WriteSignedOrUnsigned(uint num, uint nonNegative)
		{
			Debug.Assert(num == nonNegative || ~num == nonNegative);

			Span<byte> span;
			unchecked {
				switch (G.PositionOfMostSignificantOne(nonNegative)) {
				// 29 to 32 significant bits
				case 31: case 30: case 29: case 28:
					span = GetOutSpan(5);
					span[_i    ] = (byte)(num == nonNegative ? 0b1111_0000 : 0b1111_1111);
					span[_i + 1] = (byte)(num >> 24);
					span[_i + 2] = (byte)(num >> 16);
					span[_i + 3] = (byte)(num >> 8);
					span[_i + 4] = (byte)num;
					_i += 5;
					break;
				// 22 to 28 significant bits
				case 27: case 26: case 25: case 24: case 23: case 22: case 21:
					span = GetOutSpan(4);
					span[_i    ] = (byte)(0b1110_0000 | ((num >> 24) & 0b0000_1111));
					span[_i + 1] = (byte)(num >> 16);
					span[_i + 2] = (byte)(num >> 8);
					span[_i + 3] = (byte)num;
					_i += 4;
					break;
				// 15 to 21 significant bits
				case 20: case 19: case 18: case 17: case 16: case 15: case 14:
					span = GetOutSpan(3);
					span[_i    ] = (byte)(0b1100_0000 | ((num >> 16) & 0b0001_1111));
					span[_i + 1] = (byte)(num >> 8);
					span[_i + 2] = (byte)num;
					_i += 3;
					break;
				// 8 to 14 significant bits
				case 13: case 12: case 11: case 10: case 9: case 8: case 7:
					span = GetOutSpan(2);
					span[_i    ] = (byte)(0b1000_0000 | ((num >> 8) & 0b0011_1111));
					span[_i + 1] = (byte)num;
					_i += 2;
					break;
				// 0 to 7 significant bits
				default:
					GetOutSpan(1)[_i++] = (byte)(num & 0x7F);
					break;
				}
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Write(long num)
		{
			// Fast path for small non-negative numbers
			if ((uint)num < 64)
				GetOutSpan(1)[_i++] = (byte)num;
			else
				WriteSignedOrUnsigned((uint)num, (uint)(num >= 0 ? num : ~num));
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Write(ulong num)
		{
			// Fast path for small numbers
			if (num < 128)
				GetOutSpan(1)[_i++] = (byte)num;
			else
				WriteSignedOrUnsigned(num, num);
		}

		public void WriteSignedOrUnsigned(ulong num, ulong nonNegative)
		{
			Debug.Assert(num == nonNegative || ~num == nonNegative);
			if ((uint)num == num) {
				WriteSignedOrUnsigned((uint)num, (uint)nonNegative);
			} else {
				Span<byte> span;
				unchecked {
					var positionOfMostSignificantOne = G.PositionOfMostSignificantOne(nonNegative);
					switch (positionOfMostSignificantOne) {
					// 50 to 64 significant bits
					default:
						int numberSize = positionOfMostSignificantOne >= 56 ? 10 : 9;
						span = GetOutSpan(numberSize);
						span[_i    ] = (byte)0b1111_1110;
						span[_i + 1] = (byte)(numberSize - 1);
						if (positionOfMostSignificantOne >= 56) {
							span[_i + 2] = (byte)(num >> 56);
							_i++;
						}
						span[_i + 2] = (byte)(num >> 48);
						span[_i + 3] = (byte)(num >> 40);
						span[_i + 4] = (byte)(num >> 32);
						span[_i + 5] = (byte)(num >> 24);
						span[_i + 6] = (byte)(num >> 16);
						span[_i + 7] = (byte)(num >> 8);
						span[_i + 8] = (byte)num;
						break;
					// 43 to 49 significant bits
					case 48: case 47: case 46: case 45: case 44: case 43: case 42:
						span = GetOutSpan(7);
						span[_i    ] = (byte)(0b1111_1100 | ((num >> 48) & 1));
						span[_i + 1] = (byte)(num >> 40);
						span[_i + 2] = (byte)(num >> 32);
						span[_i + 3] = (byte)(num >> 24);
						span[_i + 4] = (byte)(num >> 16);
						span[_i + 5] = (byte)(num >> 8);
						span[_i + 6] = (byte)num;
						_i += 7;
						break;
					// 36 to 42 significant bits
					case 41: case 40: case 39: case 38: case 37: case 36: case 35:
						span = GetOutSpan(6);
						span[_i    ] = (byte)(0b1111_1000 | ((num >> 40) & 0b0011));
						span[_i + 1] = (byte)(num >> 32);
						span[_i + 2] = (byte)(num >> 24);
						span[_i + 3] = (byte)(num >> 16);
						span[_i + 4] = (byte)(num >> 8);
						span[_i + 5] = (byte)num;
						_i += 6;
						break;
					// 29 to 35 significant bits
					case 34: case 33: case 32: case 31: case 30: case 29: case 28:
						span = GetOutSpan(5);
						span[_i    ] = (byte)(0b1111_0000 | ((num >> 32) & 0b0111));
						span[_i + 1] = (byte)(num >> 24);
						span[_i + 2] = (byte)(num >> 16);
						span[_i + 3] = (byte)(num >> 8);
						span[_i + 4] = (byte)num;
						_i += 5;
						break;
					}
				}
			}
		}

		public void Write(BigInteger num)
		{
			if (num <= long.MaxValue && num >= long.MinValue) {
				Write((long)num);
			} else {
				#if NETSTANDARD2_0 || NET45 || NET451 || NET452 || NET46 || NET461 || NET462
				var numberBytes = num.ToByteArray();
				int numNumberBytes = numberBytes.Length;
				#else
				int numNumberBytes = num.GetByteCount(isUnsigned: false);
				#endif

				if (numNumberBytes > _opt.MaxNumberSize)
					throw new ArgumentOutOfRangeException(
						"The BigInteger cannot be stored because it is larger than the current MaxNumberSize.");

				// Usually 2 bytes are needed for the number's length-prefix header,
				// but it could be up to 6 bytes in extreme cases.
				var span = GetOutSpan(6 + numNumberBytes);
				
				span[_i++] = 0b1111_1110;
				Write((uint) numNumberBytes);
				
				Debug.Assert(span == GetOutSpan(numNumberBytes));
				span = span.Slice(_i);
				Debug.Assert(span.Length >= numNumberBytes);

				#if NETSTANDARD2_0 || NET45 || NET451 || NET452 || NET46 || NET461 || NET462
				Array.Reverse(numberBytes);
				numberBytes.CopyTo(span);
				#else
				G.Verify(num.TryWriteBytes(span, out int bytesWritten, false, isBigEndian: true));
				#endif
			}
		}

		public void WriteLittleEndianBytes(uint num, int numBytes = 4) 
			=> WriteLittleEndianBytes(num, numBytes, GetOutSpan(numBytes));
		public void WriteLittleEndianBytes(uint num, int numBytes, Span<byte> outBuf)
		{
			for (; numBytes > 0; numBytes--) {
				outBuf[_i++] = (byte)num;
				num >>= 8;
			}
		}
		public void WriteLittleEndianBytes(ulong num, int numBytes = 8)
			=> WriteLittleEndianBytes(num, numBytes, GetOutSpan(numBytes));
		public void WriteLittleEndianBytes(ulong num, int numBytes, Span<byte> outBuf)
		{
			for (; numBytes > 0; numBytes--) {
				outBuf[_i++] = (byte)num;
				num >>= 8;
			}
		}

		public void WriteLittleEndianBytes(BigInteger num, int numBytes, Span<byte> outBuf)
		{
			Debug.Assert(outBuf.Length - _i >= numBytes);

			#if !(NETSTANDARD2_0 || NET45 || NET451 || NET452 || NET46 || NET461 || NET462)
			if (num.TryWriteBytes(outBuf.Slice(_i), out int bytesWritten, false, isBigEndian: false)) {
				Debug.Assert(bytesWritten == numBytes);
				return;
			}
			#endif

			if (numBytes <= 8) {
				// Truncate BigInteger: https://stackoverflow.com/questions/74989790/how-to-truncate-a-biginteger-to-int-long-uint-ulong
				WriteLittleEndianBytes((ulong)(num & ulong.MaxValue), numBytes, outBuf);
			} else {
				// Allocating an array here isn't efficient, but I don't know a better way
				var numSpan = num.ToByteArray().AsSpan();
				if (numBytes < numSpan.Length)
					numSpan = numSpan.Slice(0, numBytes);
				numSpan.CopyTo(outBuf.Slice(_i));
			}
		}

		public void Write(float num)
		{
			// TODO: what about endianness?
			#if NETSTANDARD2_0 || NET45 || NET451 || NET452 || NET46 || NET461 || NET462
			// inefficient
			uint bytes = BitConverter.ToUInt32(BitConverter.GetBytes(num), 0);
			#else
			uint bytes = (uint) BitConverter.SingleToInt32Bits(num);
			#endif
			WriteLittleEndianBytes(bytes);
		}

		public void Write(double num)
		{
			// TODO: MAY BE WRONG: "The order of bits in the integer returned by the DoubleToInt64Bits method depends on whether the computer architecture is little-endian or big-endian."
			ulong bytes = (ulong) BitConverter.DoubleToInt64Bits(num);
			WriteLittleEndianBytes(bytes);
		}

		public void Write(decimal num)
		{
			// TODO: what about endianness?
			int[] arrayOf4 = Decimal.GetBits(num); // little-endian
			Span<byte> outBuf = GetOutSpan(16);
			WriteLittleEndianBytes(unchecked((uint)arrayOf4[0]), 4, outBuf);
			WriteLittleEndianBytes(unchecked((uint)arrayOf4[1]), 4, outBuf);
			WriteLittleEndianBytes(unchecked((uint)arrayOf4[2]), 4, outBuf);
			WriteLittleEndianBytes(unchecked((uint)arrayOf4[3]), 4, outBuf);
		}

		public void Write(string str) => Write(str.AsSpan());
		public void Write(ReadOnlySpan<char> str)
		{
			// Encoding.UTF8 allows unpaired surrogates. Technically this is "WTF-8"
			int wtf8size = Encoding.UTF8.GetByteCount(str);
			// worst-case overhead: 2 bytes for start/end markers + 5 for string length
			int requiredBytes = 7 + wtf8size;

			var outSpan = GetOutSpan(requiredBytes);

			if ((_opt.Markers & SyncBinary.Markers.ListStart) != 0)
				outSpan[_i++] = (byte)'[';

			WriteSignedOrUnsigned((uint)wtf8size, (uint)wtf8size); // length prefix

			int wtf8size2 = Encoding.UTF8.GetBytes(str, outSpan.Slice(_i));
			Debug.Assert(wtf8size == wtf8size2);
			_i += wtf8size;

			if ((_opt.Markers & SyncBinary.Markers.ListEnd) != 0)
				outSpan[_i++] = (byte)']';
		}

		public void WriteBitfield(uint value, uint bitfieldSize)
		{
			// Write the beginning bits of the bitfield
			uint bitsLeft = _bitfieldBitsLeftInByte;
			if (bitsLeft != 0) {
				Debug.Assert(_i > 0);
				if (bitsLeft >= bitfieldSize) {
					_bitfieldBitsLeftInByte = bitsLeft - bitfieldSize;
					_buf.Span[_i - 1] |= (byte)( (value & ((1u << (int)bitfieldSize) - 1)) << (8 - (int)bitsLeft) );
					return;
				} else {
					//_bitfieldBitsLeftInByte = 0 is redundant, as GetOutSpan() does this below
					_buf.Span[_i - 1] |= (byte)( value << (8 - (int)bitsLeft) );
					value >>= (int)bitsLeft;
					bitfieldSize -= bitsLeft;
				}
			}

			// Write the middle bytes of the bitfield
			int minNumBytesLeft = (int)bitfieldSize >> 3;
			var span = GetOutSpan(minNumBytesLeft + 1);
			if (bitfieldSize >= 8) {
				WriteLittleEndianBytes(value, minNumBytesLeft, span);
				bitfieldSize &= 7;
			}

			// Write the ending bits of the bitfield
			if (bitfieldSize != 0) {
				Debug.Assert(bitfieldSize < 8);
				span[_i++] = (byte)( value & ((1u << (int)bitfieldSize) - 1) );
				_bitfieldBitsLeftInByte = 8 - bitfieldSize;
			}
		}

		public void WriteBitfield(ulong value, uint bitfieldSize)
		{
			// Write the beginning bits of the bitfield
			uint bitsLeft = _bitfieldBitsLeftInByte;
			if (bitsLeft != 0) {
				Debug.Assert(_i > 0);
				if (bitsLeft >= bitfieldSize) {
					_bitfieldBitsLeftInByte = bitsLeft - bitfieldSize;
					_buf.Span[_i - 1] |= (byte)( ((uint)value & ((1u << (int)bitfieldSize) - 1)) << (8 - (int)bitsLeft) );
					return;
				} else {
					//_bitfieldBitsLeftInByte = 0 is redundant, as GetOutSpan() does this below
					_buf.Span[_i - 1] |= (byte)( (uint)value << (8 - (int)bitsLeft));
					value >>= (int)bitsLeft;
					bitfieldSize -= bitsLeft;
				}
			}

			// Write the middle bytes of the bitfield
			int minNumBytesLeft = (int)bitfieldSize >> 3;
			var span = GetOutSpan(minNumBytesLeft + 1);
			if (bitfieldSize >= 8) {
				WriteLittleEndianBytes(value, minNumBytesLeft, span);
				bitfieldSize &= 7;
			}

			// Write the ending bits of the bitfield
			if (bitfieldSize != 0) {
				Debug.Assert(bitfieldSize < 8);
				span[_i++] = (byte)( (uint)value & ((1u << (int)bitfieldSize) - 1));
				_bitfieldBitsLeftInByte = 8 - bitfieldSize;
			}
		}

		public void WriteBitfield(BigInteger value, uint bitfieldSize)
		{
			// Write the beginning bits of the bitfield
			uint bitsLeft = _bitfieldBitsLeftInByte;
			if (bitsLeft != 0) {
				Debug.Assert(_i > 0);
				if (bitsLeft >= bitfieldSize) {
					_bitfieldBitsLeftInByte = bitsLeft - bitfieldSize;
					_buf.Span[_i - 1] |= (byte)( (uint)(value & ((1u << (int)bitfieldSize) - 1)) << (8 - (int)bitsLeft) );
					return;
				} else {
					//_bitfieldBitsLeftInByte = 0 is redundant, as GetOutSpan() does this below
					// Use `(uint)(value & 0xFF)` instead of `(uint)value` to avoid OverflowException
					_buf.Span[_i - 1] |= (byte)( (uint)(value & 0xFF) << (8 - (int)bitsLeft));
					value >>= (int)bitsLeft;
					bitfieldSize -= bitsLeft;
				}
			}

			// Write the middle bytes of the bitfield
			int minNumBytesLeft = (int)bitfieldSize >> 3;
			var span = GetOutSpan(minNumBytesLeft + 1);
			if (bitfieldSize >= 8) {
				WriteLittleEndianBytes(value, minNumBytesLeft, span);
				bitfieldSize &= 7;
			}

			// Write the ending bits of the bitfield
			if (bitfieldSize != 0) {
				Debug.Assert(bitfieldSize < 8);
				span[_i++] = (byte)( value & ((1u << (int)bitfieldSize) - 1) );
				_bitfieldBitsLeftInByte = 8 - bitfieldSize;
			}
		}

		internal void WriteTypeTag(string? tag)
		{
			if ((_opt.Markers & SyncBinary.Markers.TypeTag) != 0) {
				GetOutSpan(1)[_i++] = (byte)'T';
			}
			Write(tag);
		}
	}
}
