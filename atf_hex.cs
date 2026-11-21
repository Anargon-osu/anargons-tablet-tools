// Shared Intel HEX input validation for the firmware builder and its digital twin.
using System;
using Gd32Emu;

namespace AnargonsTabletTools
{
    public static class IntelHex
    {
        public static byte[] Read(string path, int[] firstAddress)
        {
            return Parse(P.Ascii(P.ReadAllBytes(path)), path, firstAddress);
        }

        public static byte[] Parse(string text, string label, int[] firstAddress)
        {
            byte[] memory = new byte[0x10000];
            bool[] present = new bool[memory.Length];
            int low = memory.Length, high = 0;
            long segment = 0;
            bool ended = false;
            string[] lines = P.Lines(text);
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = P.Trim(lines[lineIndex]);
                if (P.Len(line) == 0) continue;
                string where = label + ":" + F.Str(lineIndex + 1) + ": ";
                if (ended) throw new Stop(where + "Intel HEX contains data or a second end record after its end marker.");
                if (P.Len(line) < 11 || P.CharAt(line, 0) != ':' || (P.Len(line) & 1) == 0)
                    throw new Stop(where + "invalid Intel HEX record length or prefix.");
                int count = (P.Len(line) - 1) / 2;
                int[] record = new int[count];
                int checksum = 0;
                for (int i = 0; i < count; i++)
                {
                    int highDigit = Digit(P.CharAt(line, 1 + 2 * i));
                    int lowDigit = Digit(P.CharAt(line, 2 + 2 * i));
                    if (highDigit < 0 || lowDigit < 0) throw new Stop(where + "invalid hexadecimal digit.");
                    record[i] = highDigit * 16 + lowDigit;
                    checksum += record[i];
                }
                int length = record[0], address = (record[1] << 8) | record[2], type = record[3];
                if (count != length + 5) throw new Stop(where + "Intel HEX byte count does not match the record length.");
                if ((checksum & 255) != 0) throw new Stop(where + "bad Intel HEX checksum.");
                if (type == 0)
                {
                    long start = segment + address - 0x08000000L;
                    if (start < 0 || start + length > memory.Length)
                        throw new Stop(where + "Intel HEX data is outside the tablet's flash (0x08000000-0x0800FFFF).");
                    for (int i = 0; i < length; i++)
                    {
                        int offset = (int)start + i;
                        byte value = (byte)record[4 + i];
                        if (present[offset] && memory[offset] != value)
                            throw new Stop(where + "conflicting Intel HEX data at " + F.Hx(0x08000000 + offset, 8) + ".");
                        memory[offset] = value; present[offset] = true;
                        if (offset < low) low = offset;
                        if (offset + 1 > high) high = offset + 1;
                    }
                }
                else if (type == 1 && length == 0 && address == 0) ended = true;
                else if (type == 2 && length == 2 && address == 0) segment = (long)((record[4] << 8) | record[5]) << 4;
                else if (type == 4 && length == 2 && address == 0) segment = (long)((record[4] << 8) | record[5]) << 16;
                else if ((type == 3 || type == 5) && length == 4 && address == 0) { } // entry comes from the vector table
                else throw new Stop(where + "unsupported or malformed Intel HEX record type " + F.Str(type) + ".");
            }
            if (!ended) throw new Stop(label + ": Intel HEX has no end marker.");
            if (high == 0) throw new Stop(label + ": Intel HEX contains no flash data.");
            byte[] image = new byte[high - low];
            for (int i = 0; i < image.Length; i++) image[i] = present[low + i] ? memory[low + i] : (byte)255;
            firstAddress[0] = 0x08000000 + low;
            return image;
        }

        static int Digit(int c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            return -1;
        }
    }
}
