// atf_stockref.cs - GENERATED from the two verified stock dumps; do not edit by hand (see atf_repair.cs).
// Per verified build (same order as Layout.KNOWN): checksums only. The CRC-32 of every 1 KB page of the code before
// 0x0800C188 (where xppen-tools' fixed IC-902 size stops writing), so a damaged dump can be checked page by page, and the
// CRC-32 of the firmware end (0x0800C188 to the end: the last tables, the start-up table and .data).
// R42: no firmware bytes are stored here. Up to R42 Part 3 this file held the stock end itself (TAIL, 3220 bytes of
// XP-Pen firmware); the recovery tool now restores an erased end from the user's own copy (Repair.FindEnd), checked
// against END_CRC.
namespace AnargonsTabletTools
{
    public static class StockRef
    {
        public const int CUT = 0x0800C188;
        public static readonly long[] END_CRC = new long[] { 0xDEB66FA2L, 0xD49B3513L };
        public static readonly long[][] PAGE_CRC = new long[][] {
            new long[] { 0xE8F266E9L,0x7FDE8BC7L,0xDAAA4E1FL,0x11A1D597L,0x54337B9FL,0xD5C61577L,0xDF053877L,0x8F60C0BEL,0xA7798054L,0x2459BB49L,0x244C3FD1L,0x97F98EB7L,0x10A2116CL,0x879505E2L,0x5261C6F1L,0xFF5E6699L,0x5EAE77ECL,0x4BE4536CL,0x2CFB0DD8L,0x40A488FEL,0xA558C68DL,0x392BDB7DL,0x9A612026L,0xA7DD052CL,0x43E56177L,0x6615D229L,0x6DF2AA8FL,0x9720689FL,0x79B17674L,0x46D5F999L,0xDEC3315BL,0xC2B8C912L,0x05310F3EL,0xE8983809L,0xB4AF0F7CL },
            new long[] { 0xBD8D3CFDL,0x7FDE8BC7L,0xCE28A5D1L,0x7D44906FL,0xFFE058CEL,0x3E3DEE7EL,0x7E1CE87FL,0xCF19F821L,0xFFC8F30EL,0xFB345D53L,0x4E136067L,0x5E67C0B6L,0x0267F027L,0x488A7EC5L,0x164BE32EL,0x7B6800B7L,0xAB123761L,0x9CCDB18EL,0x66E5EBA9L,0x6E859AA2L,0xAF9B58DDL,0x6747DD8DL,0xFC4F892AL,0x7CEEE2A9L,0x4C0A8347L,0x7A7DE82FL,0xD1EAEFFAL,0x2F7A8666L,0x7D3FAAD4L,0x83BFA068L,0xF662E5A8L,0x991A37ECL,0x89485351L,0x96D4BD16L,0x99A07C7CL },
        };
    }
}
