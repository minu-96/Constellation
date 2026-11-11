using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// 작은 QR 코드 생성기(외부 라이브러리 없음).
    /// 바이트 모드, 오류 정정 수준 M, 버전 1~10(최대 213바이트)만 지원한다. 카드 주소를 담기에 충분하다.
    /// </summary>
    public static class QrEncoder
    {
        const int k_MaxVersion = 10;

        // 버전별 값(오류 정정 수준 M). 인덱스 0은 쓰지 않는다.
        static readonly int[] k_TotalCodewords = { 0, 26, 44, 70, 100, 134, 172, 196, 242, 292, 346 };
        static readonly int[] k_EccPerBlock = { 0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26 };
        static readonly int[] k_BlockCount = { 0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5 };

        /// <summary>
        /// 문자열을 QR 모듈 행렬로 바꾼다. [y, x]가 true면 검은 칸이다. 너무 길면 null.
        /// </summary>
        public static bool[,] Encode(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;
            byte[] data = Encoding.UTF8.GetBytes(text);

            int version = 0;
            for (int v = 1; v <= k_MaxVersion; v++)
            {
                int capacityBits = DataCodewords(v) * 8;
                int neededBits = 4 + (v < 10 ? 8 : 16) + data.Length * 8;
                if (neededBits <= capacityBits)
                {
                    version = v;
                    break;
                }
            }
            if (version == 0)
                return null;

            byte[] codewords = AddErrorCorrection(BuildDataCodewords(data, version), version);

            int size = version * 4 + 17;
            bool[,] modules = new bool[size, size];
            bool[,] isFunction = new bool[size, size];
            DrawFunctionPatterns(modules, isFunction, version, size);
            DrawCodewords(modules, isFunction, codewords, size);

            // 여덟 가지 마스크 중 벌점이 가장 낮은 것을 고른다.
            int bestMask = 0;
            int bestPenalty = int.MaxValue;
            for (int mask = 0; mask < 8; mask++)
            {
                ApplyMask(modules, isFunction, mask, size);
                DrawFormatBits(modules, isFunction, mask, size);
                int penalty = Penalty(modules, size);
                if (penalty < bestPenalty)
                {
                    bestPenalty = penalty;
                    bestMask = mask;
                }
                ApplyMask(modules, isFunction, mask, size); // XOR이라 한 번 더 하면 원래대로 돌아온다.
            }
            ApplyMask(modules, isFunction, bestMask, size);
            DrawFormatBits(modules, isFunction, bestMask, size);
            return modules;
        }

        /// <summary>QR 행렬을 흑백 텍스처로 만든다. 가장자리에 흰 여백(quiet zone)을 둔다.</summary>
        public static Texture2D ToTexture(bool[,] modules, int pixelsPerModule, int quietZoneModules)
        {
            if (modules == null)
                return null;
            int count = modules.GetLength(0);
            int scale = Mathf.Max(1, pixelsPerModule);
            int total = (count + quietZoneModules * 2) * scale;

            Color32 white = new Color32(255, 255, 255, 255);
            Color32 black = new Color32(0, 0, 0, 255);
            Color32[] pixels = new Color32[total * total];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = white;

            for (int y = 0; y < count; y++)
            {
                for (int x = 0; x < count; x++)
                {
                    if (!modules[y, x])
                        continue;
                    // 텍스처는 아래에서 위로 쌓이므로 세로를 뒤집는다.
                    int px = (x + quietZoneModules) * scale;
                    int py = (count - 1 - y + quietZoneModules) * scale;
                    for (int dy = 0; dy < scale; dy++)
                    {
                        int row = (py + dy) * total;
                        for (int dx = 0; dx < scale; dx++)
                            pixels[row + px + dx] = black;
                    }
                }
            }

            Texture2D texture = new Texture2D(total, total, TextureFormat.RGBA32, false);
            texture.name = "QR Code";
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        static int DataCodewords(int version)
        {
            return k_TotalCodewords[version] - k_EccPerBlock[version] * k_BlockCount[version];
        }

        // 모드(바이트) + 길이 + 데이터 + 종료 표시 + 채움 바이트
        static byte[] BuildDataCodewords(byte[] data, int version)
        {
            int capacity = DataCodewords(version);
            List<bool> bits = new List<bool>(capacity * 8);
            AppendBits(bits, 0x4, 4);
            AppendBits(bits, data.Length, version < 10 ? 8 : 16);
            for (int i = 0; i < data.Length; i++)
                AppendBits(bits, data[i], 8);

            int capacityBits = capacity * 8;
            AppendBits(bits, 0, Math.Min(4, capacityBits - bits.Count));
            AppendBits(bits, 0, (8 - bits.Count % 8) % 8);
            for (int pad = 0xEC; bits.Count < capacityBits; pad ^= 0xEC ^ 0x11)
                AppendBits(bits, pad, 8);

            byte[] result = new byte[capacity];
            for (int i = 0; i < bits.Count; i++)
            {
                if (bits[i])
                    result[i >> 3] |= (byte)(1 << (7 - (i & 7)));
            }
            return result;
        }

        static void AppendBits(List<bool> bits, int value, int length)
        {
            for (int i = length - 1; i >= 0; i--)
                bits.Add(((value >> i) & 1) != 0);
        }

        // 데이터를 블록으로 나눠 리드-솔로몬 오류 정정 코드를 붙이고, 블록을 번갈아 섞는다.
        static byte[] AddErrorCorrection(byte[] data, int version)
        {
            int blockCount = k_BlockCount[version];
            int eccLength = k_EccPerBlock[version];
            int total = k_TotalCodewords[version];
            int shortBlocks = blockCount - total % blockCount;
            int shortBlockLength = total / blockCount;

            byte[] divisor = ReedSolomonDivisor(eccLength);
            byte[][] blocks = new byte[blockCount][];
            int offset = 0;
            for (int i = 0; i < blockCount; i++)
            {
                int dataLength = shortBlockLength - eccLength + (i < shortBlocks ? 0 : 1);
                byte[] block = new byte[shortBlockLength + 1];
                Array.Copy(data, offset, block, 0, dataLength);
                offset += dataLength;
                byte[] ecc = ReedSolomonRemainder(block, dataLength, divisor);
                // 짧은 블록은 데이터 끝에 빈칸 하나를 두어 길이를 맞춘다(섞을 때 건너뛴다).
                Array.Copy(ecc, 0, block, shortBlockLength + 1 - eccLength, eccLength);
                blocks[i] = block;
            }

            byte[] result = new byte[total];
            int k = 0;
            for (int i = 0; i < shortBlockLength + 1; i++)
            {
                for (int j = 0; j < blockCount; j++)
                {
                    if (i != shortBlockLength - eccLength || j >= shortBlocks)
                        result[k++] = blocks[j][i];
                }
            }
            return result;
        }

        static byte[] ReedSolomonDivisor(int degree)
        {
            byte[] result = new byte[degree];
            result[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    result[j] = Multiply(result[j], root);
                    if (j + 1 < degree)
                        result[j] ^= result[j + 1];
                }
                root = Multiply(root, 0x02);
            }
            return result;
        }

        static byte[] ReedSolomonRemainder(byte[] data, int length, byte[] divisor)
        {
            byte[] result = new byte[divisor.Length];
            for (int n = 0; n < length; n++)
            {
                int factor = data[n] ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (int i = 0; i < result.Length; i++)
                    result[i] ^= Multiply(divisor[i], factor);
            }
            return result;
        }

        // GF(2^8) 곱셈, 기약 다항식 0x11D
        static byte Multiply(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return (byte)z;
        }

        static void DrawFunctionPatterns(bool[,] modules, bool[,] isFunction, int version, int size)
        {
            // 타이밍 패턴
            for (int i = 0; i < size; i++)
            {
                SetFunction(modules, isFunction, 6, i, i % 2 == 0);
                SetFunction(modules, isFunction, i, 6, i % 2 == 0);
            }

            // 위치 찾기 패턴 세 개
            DrawFinder(modules, isFunction, 3, 3, size);
            DrawFinder(modules, isFunction, size - 4, 3, size);
            DrawFinder(modules, isFunction, 3, size - 4, size);

            // 정렬 패턴
            int[] positions = AlignmentPositions(version, size);
            for (int i = 0; i < positions.Length; i++)
            {
                for (int j = 0; j < positions.Length; j++)
                {
                    bool nearFinder = (i == 0 && j == 0) || (i == 0 && j == positions.Length - 1) || (i == positions.Length - 1 && j == 0);
                    if (!nearFinder)
                        DrawAlignment(modules, isFunction, positions[i], positions[j]);
                }
            }

            // 형식 정보 자리를 먼저 잡아 둔다(값은 마스크를 고른 뒤에 쓴다).
            DrawFormatBits(modules, isFunction, 0, size);
            DrawVersion(modules, isFunction, version, size);
        }

        static void SetFunction(bool[,] modules, bool[,] isFunction, int x, int y, bool dark)
        {
            modules[y, x] = dark;
            isFunction[y, x] = true;
        }

        static void DrawFinder(bool[,] modules, bool[,] isFunction, int cx, int cy, int size)
        {
            for (int dy = -4; dy <= 4; dy++)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = cx + dx;
                    int y = cy + dy;
                    if (x < 0 || x >= size || y < 0 || y >= size)
                        continue;
                    int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    SetFunction(modules, isFunction, x, y, distance != 2 && distance != 4);
                }
            }
        }

        static void DrawAlignment(bool[,] modules, bool[,] isFunction, int cx, int cy)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                    SetFunction(modules, isFunction, cx + dx, cy + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
        }

        static int[] AlignmentPositions(int version, int size)
        {
            if (version == 1)
                return new int[0];
            int count = version / 7 + 2;
            int step = (version * 4 + count * 2 + 1) / (count * 2 - 2) * 2;
            int[] result = new int[count];
            result[0] = 6;
            int position = size - 7;
            for (int i = count - 1; i >= 1; i--)
            {
                result[i] = position;
                position -= step;
            }
            return result;
        }

        // 형식 정보 15비트(오류 정정 수준 M = 00, 마스크 번호)를 두 군데에 쓴다.
        static void DrawFormatBits(bool[,] modules, bool[,] isFunction, int mask, int size)
        {
            int data = (0 << 3) | mask;
            int rem = data;
            for (int i = 0; i < 10; i++)
                rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = ((data << 10) | rem) ^ 0x5412;

            for (int i = 0; i <= 5; i++)
                SetFunction(modules, isFunction, 8, i, Bit(bits, i));
            SetFunction(modules, isFunction, 8, 7, Bit(bits, 6));
            SetFunction(modules, isFunction, 8, 8, Bit(bits, 7));
            SetFunction(modules, isFunction, 7, 8, Bit(bits, 8));
            for (int i = 9; i < 15; i++)
                SetFunction(modules, isFunction, 14 - i, 8, Bit(bits, i));

            for (int i = 0; i < 8; i++)
                SetFunction(modules, isFunction, size - 1 - i, 8, Bit(bits, i));
            for (int i = 8; i < 15; i++)
                SetFunction(modules, isFunction, 8, size - 15 + i, Bit(bits, i));
            SetFunction(modules, isFunction, 8, size - 8, true); // 항상 검은 칸
        }

        // 버전 7 이상은 버전 정보 18비트를 두 군데에 쓴다.
        static void DrawVersion(bool[,] modules, bool[,] isFunction, int version, int size)
        {
            if (version < 7)
                return;
            int rem = version;
            for (int i = 0; i < 12; i++)
                rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int bits = (version << 12) | rem;
            for (int i = 0; i < 18; i++)
            {
                bool bit = Bit(bits, i);
                int a = size - 11 + i % 3;
                int b = i / 3;
                SetFunction(modules, isFunction, a, b, bit);
                SetFunction(modules, isFunction, b, a, bit);
            }
        }

        static bool Bit(int value, int index)
        {
            return ((value >> index) & 1) != 0;
        }

        // 오른쪽 아래에서 시작해 두 칸 폭으로 지그재그로 올라가며 데이터를 채운다.
        static void DrawCodewords(bool[,] modules, bool[,] isFunction, byte[] codewords, int size)
        {
            int i = 0;
            int totalBits = codewords.Length * 8;
            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6)
                    right = 5;
                for (int vertical = 0; vertical < size; vertical++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? size - 1 - vertical : vertical;
                        if (!isFunction[y, x] && i < totalBits)
                        {
                            modules[y, x] = Bit(codewords[i >> 3], 7 - (i & 7));
                            i++;
                        }
                    }
                }
            }
        }

        static void ApplyMask(bool[,] modules, bool[,] isFunction, int mask, int size)
        {
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (isFunction[y, x])
                        continue;
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
                    if (invert)
                        modules[y, x] = !modules[y, x];
                }
            }
        }

        // 읽기 어려운 무늬에 벌점을 준다(같은 색이 길게 이어짐, 2×2 덩어리, 흑백 비율 치우침).
        static int Penalty(bool[,] modules, int size)
        {
            int result = 0;

            for (int y = 0; y < size; y++)
            {
                int runRow = 1;
                int runColumn = 1;
                for (int x = 1; x < size; x++)
                {
                    if (modules[y, x] == modules[y, x - 1])
                    {
                        runRow++;
                        if (runRow == 5) result += 3;
                        else if (runRow > 5) result++;
                    }
                    else
                    {
                        runRow = 1;
                    }

                    if (modules[x, y] == modules[x - 1, y])
                    {
                        runColumn++;
                        if (runColumn == 5) result += 3;
                        else if (runColumn > 5) result++;
                    }
                    else
                    {
                        runColumn = 1;
                    }
                }
            }

            int dark = 0;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (modules[y, x])
                        dark++;
                    if (x + 1 < size && y + 1 < size)
                    {
                        bool c = modules[y, x];
                        if (c == modules[y, x + 1] && c == modules[y + 1, x] && c == modules[y + 1, x + 1])
                            result += 3;
                    }
                }
            }

            int total = size * size;
            int deviation = Math.Abs(dark * 20 - total * 10) / total; // 50%에서 5%p 벗어날 때마다 1
            result += deviation * 10;
            return result;
        }
    }
}
