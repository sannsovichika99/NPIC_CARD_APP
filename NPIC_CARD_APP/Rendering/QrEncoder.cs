using System;
using System.Collections.Generic;
using System.Text;

namespace NPIC_CARD_APP.Rendering
{
    /// <summary>
    /// Minimal QR Code encoder (byte mode, UTF-8, versions 1-40, ECC level M).
    /// No NuGet package needed. Based on the public ISO/IEC 18004 algorithm.
    /// </summary>
    public sealed class QrEncoder
    {
        public int Size { get; private set; }
        private bool[,] _modules;     // [y, x]
        private bool[,] _isFunction;  // [y, x]
        private readonly int _version;

        // Error correction level M
        private const int EclFormatBits = 0;
        private static readonly int[] EccCodewordsPerBlock =
        {
            -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26,
            26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28
        };
        private static readonly int[] NumErrorCorrectionBlocks =
        {
            -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16,
            17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49
        };

        public bool IsDark(int x, int y) { return _modules[y, x]; }

        public static QrEncoder Encode(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text ?? "");
            for (int version = 1; version <= 40; version++)
            {
                int capacityBits = GetNumDataCodewords(version) * 8;
                int countBits = version <= 9 ? 8 : 16;
                if (4 + countBits + data.Length * 8 <= capacityBits)
                    return new QrEncoder(version, data);
            }
            throw new ArgumentException("Text is too long for a QR code.");
        }

        private QrEncoder(int version, byte[] data)
        {
            _version = version;
            Size = version * 4 + 17;
            _modules = new bool[Size, Size];
            _isFunction = new bool[Size, Size];

            DrawFunctionPatterns();
            byte[] allCodewords = AddEccAndInterleave(BuildDataCodewords(data));
            DrawCodewords(allCodewords);

            int bestMask = 0;
            long minPenalty = long.MaxValue;
            for (int mask = 0; mask < 8; mask++)
            {
                ApplyMask(mask);
                DrawFormatBits(mask);
                long penalty = GetPenaltyScore();
                if (penalty < minPenalty) { minPenalty = penalty; bestMask = mask; }
                ApplyMask(mask); // undo (XOR)
            }
            ApplyMask(bestMask);
            DrawFormatBits(bestMask);
            _isFunction = null;
        }

        // ---------------- data ----------------
        private byte[] BuildDataCodewords(byte[] data)
        {
            var bits = new List<bool>();
            AppendBits(bits, 0x4, 4);                                   // byte mode
            AppendBits(bits, data.Length, _version <= 9 ? 8 : 16);      // character count
            foreach (byte b in data) AppendBits(bits, b, 8);

            int capacityBits = GetNumDataCodewords(_version) * 8;
            AppendBits(bits, 0, Math.Min(4, capacityBits - bits.Count)); // terminator
            AppendBits(bits, 0, (8 - bits.Count % 8) % 8);               // byte align
            for (int pad = 0xEC; bits.Count < capacityBits; pad ^= 0xEC ^ 0x11)
                AppendBits(bits, pad, 8);

            var result = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++)
                if (bits[i]) result[i >> 3] |= (byte)(1 << (7 - (i & 7)));
            return result;
        }

        private static void AppendBits(List<bool> bits, int value, int length)
        {
            for (int i = length - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
        }

        private byte[] AddEccAndInterleave(byte[] data)
        {
            int numBlocks = NumErrorCorrectionBlocks[_version];
            int blockEccLen = EccCodewordsPerBlock[_version];
            int rawCodewords = GetNumRawDataModules(_version) / 8;
            int numShortBlocks = numBlocks - rawCodewords % numBlocks;
            int shortBlockLen = rawCodewords / numBlocks;

            byte[] divisor = ReedSolomonComputeDivisor(blockEccLen);
            var blocks = new List<byte[]>();
            for (int i = 0, k = 0; i < numBlocks; i++)
            {
                int datLen = shortBlockLen - blockEccLen + (i < numShortBlocks ? 0 : 1);
                byte[] dat = new byte[datLen];
                Array.Copy(data, k, dat, 0, datLen);
                k += datLen;
                byte[] ecc = ReedSolomonComputeRemainder(dat, divisor);

                byte[] block = new byte[shortBlockLen + 1];
                Array.Copy(dat, 0, block, 0, datLen);
                // short blocks keep one padding byte (skipped below) before the ECC bytes
                Array.Copy(ecc, 0, block, block.Length - blockEccLen, blockEccLen);
                blocks.Add(block);
            }

            var result = new List<byte>(rawCodewords);
            for (int i = 0; i < blocks[0].Length; i++)
                for (int j = 0; j < blocks.Count; j++)
                    if (i != shortBlockLen - blockEccLen || j >= numShortBlocks)
                        result.Add(blocks[j][i]);
            return result.ToArray();
        }

        private static byte[] ReedSolomonComputeDivisor(int degree)
        {
            byte[] result = new byte[degree];
            result[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < result.Length; j++)
                {
                    result[j] = (byte)Multiply(result[j], root);
                    if (j + 1 < result.Length) result[j] ^= result[j + 1];
                }
                root = Multiply(root, 0x02);
            }
            return result;
        }

        private static byte[] ReedSolomonComputeRemainder(byte[] data, byte[] divisor)
        {
            byte[] result = new byte[divisor.Length];
            foreach (byte b in data)
            {
                int factor = b ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (int i = 0; i < result.Length; i++)
                    result[i] ^= (byte)Multiply(divisor[i], factor);
            }
            return result;
        }

        private static int Multiply(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return z & 0xFF;
        }

        private static int GetNumRawDataModules(int ver)
        {
            int result = (16 * ver + 128) * ver + 64;
            if (ver >= 2)
            {
                int numAlign = ver / 7 + 2;
                result -= (25 * numAlign - 10) * numAlign - 55;
                if (ver >= 7) result -= 36;
            }
            return result;
        }

        private static int GetNumDataCodewords(int ver)
        {
            return GetNumRawDataModules(ver) / 8 - EccCodewordsPerBlock[ver] * NumErrorCorrectionBlocks[ver];
        }

        // ---------------- function patterns ----------------
        private void DrawFunctionPatterns()
        {
            for (int i = 0; i < Size; i++)
            {
                SetFunction(6, i, i % 2 == 0);
                SetFunction(i, 6, i % 2 == 0);
            }
            DrawFinderPattern(3, 3);
            DrawFinderPattern(Size - 4, 3);
            DrawFinderPattern(3, Size - 4);

            int[] pos = GetAlignmentPatternPositions();
            int n = pos.Length;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    if (!(i == 0 && j == 0 || i == 0 && j == n - 1 || i == n - 1 && j == 0))
                        DrawAlignmentPattern(pos[i], pos[j]);

            DrawFormatBits(0);
            DrawVersion();
        }

        private void DrawFormatBits(int mask)
        {
            int data = EclFormatBits << 3 | mask;
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = (data << 10 | rem) ^ 0x5412;

            for (int i = 0; i <= 5; i++) SetFunction(8, i, GetBit(bits, i));
            SetFunction(8, 7, GetBit(bits, 6));
            SetFunction(8, 8, GetBit(bits, 7));
            SetFunction(7, 8, GetBit(bits, 8));
            for (int i = 9; i < 15; i++) SetFunction(14 - i, 8, GetBit(bits, i));

            for (int i = 0; i < 8; i++) SetFunction(Size - 1 - i, 8, GetBit(bits, i));
            for (int i = 8; i < 15; i++) SetFunction(8, Size - 15 + i, GetBit(bits, i));
            SetFunction(8, Size - 8, true);
        }

        private void DrawVersion()
        {
            if (_version < 7) return;
            int rem = _version;
            for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int bits = _version << 12 | rem;
            for (int i = 0; i < 18; i++)
            {
                bool bit = GetBit(bits, i);
                int a = Size - 11 + i % 3, b = i / 3;
                SetFunction(a, b, bit);
                SetFunction(b, a, bit);
            }
        }

        private void DrawFinderPattern(int x, int y)
        {
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    int xx = x + dx, yy = y + dy;
                    if (0 <= xx && xx < Size && 0 <= yy && yy < Size)
                        SetFunction(xx, yy, dist != 2 && dist != 4);
                }
        }

        private void DrawAlignmentPattern(int x, int y)
        {
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    SetFunction(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
        }

        private int[] GetAlignmentPatternPositions()
        {
            if (_version == 1) return new int[0];
            int numAlign = _version / 7 + 2;
            int step = (_version * 8 + numAlign * 3 + 5) / (numAlign * 4 - 4) * 2;
            int[] result = new int[numAlign];
            result[0] = 6;
            for (int i = result.Length - 1, pos = Size - 7; i >= 1; i--, pos -= step) result[i] = pos;
            return result;
        }

        private void SetFunction(int x, int y, bool dark)
        {
            _modules[y, x] = dark;
            _isFunction[y, x] = true;
        }

        // ---------------- codewords & masks ----------------
        private void DrawCodewords(byte[] data)
        {
            int i = 0;
            for (int right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < Size; vert++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? Size - 1 - vert : vert;
                        if (!_isFunction[y, x] && i < data.Length * 8)
                        {
                            _modules[y, x] = GetBit(data[i >> 3], 7 - (i & 7));
                            i++;
                        }
                    }
            }
        }

        private void ApplyMask(int mask)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    bool invert;
                    switch (mask)
                    {
                        case 0: invert = (x + y) % 2 == 0; break;
                        case 1: invert = y % 2 == 0; break;
                        case 2: invert = x % 3 == 0; break;
                        case 3: invert = (x + y) % 3 == 0; break;
                        case 4: invert = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: invert = x * y % 2 + x * y % 3 == 0; break;
                        case 6: invert = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: invert = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }
                    if (invert && !_isFunction[y, x]) _modules[y, x] = !_modules[y, x];
                }
        }

        /// <summary>Penalty rules N1, N2 and N4 (N3 omitted; any mask still decodes correctly).</summary>
        private long GetPenaltyScore()
        {
            long result = 0;
            for (int y = 0; y < Size; y++)
            {
                int runX = 1, runY = 1;
                for (int x = 1; x < Size; x++)
                {
                    if (_modules[y, x] == _modules[y, x - 1]) { runX++; if (runX == 5) result += 3; else if (runX > 5) result++; }
                    else runX = 1;
                    if (_modules[x, y] == _modules[x - 1, y]) { runY++; if (runY == 5) result += 3; else if (runY > 5) result++; }
                    else runY = 1;
                }
            }
            for (int y = 0; y < Size - 1; y++)
                for (int x = 0; x < Size - 1; x++)
                {
                    bool c = _modules[y, x];
                    if (c == _modules[y, x + 1] && c == _modules[y + 1, x] && c == _modules[y + 1, x + 1]) result += 3;
                }
            int dark = 0;
            foreach (bool m in _modules) if (m) dark++;
            int total = Size * Size;
            int k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
            result += Math.Max(0, k) * 10;
            return result;
        }

        private static bool GetBit(int x, int i) { return ((x >> i) & 1) != 0; }
    }
}
