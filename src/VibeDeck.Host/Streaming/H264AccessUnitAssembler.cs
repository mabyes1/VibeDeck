using System;
using System.Collections.Generic;

namespace VibeDeck.Host.Streaming
{
    /// <summary>
    /// Reassembles an Annex-B byte stream into access units using H.264 AUD
    /// boundaries. FFmpeg is configured to emit an AUD before every frame.
    /// </summary>
    internal sealed class H264AccessUnitAssembler
    {
        private readonly List<byte> buffer = new List<byte>(128 * 1024);

        public IEnumerable<byte[]> Append(byte[] source, int count)
        {
            for (var index = 0; index < count; index++)
            {
                buffer.Add(source[index]);
            }

            var firstAud = FindAudStart(0);
            if (firstAud < 0)
            {
                if (buffer.Count > 2 * 1024 * 1024)
                {
                    buffer.RemoveRange(0, buffer.Count - 4);
                }
                yield break;
            }

            if (firstAud > 0)
            {
                buffer.RemoveRange(0, firstAud);
            }

            while (true)
            {
                var nextAud = FindAudStart(3);
                if (nextAud <= 0)
                {
                    yield break;
                }

                var accessUnit = buffer.GetRange(0, nextAud).ToArray();
                buffer.RemoveRange(0, nextAud);
                yield return accessUnit;
            }
        }

        private int FindAudStart(int offset)
        {
            for (var index = Math.Max(0, offset); index + 3 < buffer.Count; index++)
            {
                if (buffer[index] != 0 || buffer[index + 1] != 0)
                {
                    continue;
                }

                if (buffer[index + 2] == 1 && (buffer[index + 3] & 0x1F) == 9)
                {
                    return index;
                }

                if (index + 4 < buffer.Count &&
                    buffer[index + 2] == 0 &&
                    buffer[index + 3] == 1 &&
                    (buffer[index + 4] & 0x1F) == 9)
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
