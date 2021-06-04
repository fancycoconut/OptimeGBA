using System;
using static OptimeGBA.Bits;
using System.Runtime.CompilerServices;

namespace OptimeGBA
{
    public class MemoryControlNds
    {
        public byte SharedRamControl;

        public byte[] VRAMCNT = new byte[9];

        // EXMEMCNT 
        public byte Slot2SramWaitArm9;
        public byte Slot2Rom0WaitArm9;
        public byte Slot2Rom1WaitArm9;
        public byte Slot2RomPhiPinOutArm9;
        public byte Slot2SramWaitArm7;
        public byte Slot2Rom0WaitArm7;
        public byte Slot2Rom1WaitArm7;
        public byte Slot2RomPhiPinOutArm7;

        // Shared between 7/9 EXMEMCNT/EXMEMSTAT
        // true = ARM7
        public bool Slot2AccessRights;
        public bool Slot1AccessRights;
        public bool MainMemoryAccessPriority;

        public byte ReadHwio8Nds9(uint addr)
        {
            byte val = 0;

            switch (addr)
            {
                case 0x4000204:
                    // Console.WriteLine("read from exmemcnt b0");
                    val |= (byte)((Slot2SramWaitArm9 & 0b11) << 0);
                    val |= (byte)((Slot2Rom0WaitArm9 & 0b11) << 2);
                    val |= (byte)((Slot2Rom1WaitArm9 & 0b1) << 4);
                    val |= (byte)((Slot2RomPhiPinOutArm9 & 0b11) << 5);
                    if (Slot2AccessRights) val = BitSet(val, 7);
                    break;
                case 0x4000205:
                    // Console.WriteLine("read from exmemcnt b1");
                    if (Slot1AccessRights) val = BitSet(val, 3);
                    if (MainMemoryAccessPriority) val = BitSet(val, 7);
                    val = BitSet(val, 6);
                    break;
            }

            return val;
        }

        public void WriteHwio8Nds9(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x4000204:
                    // Console.WriteLine("write to exmemcnt b0");
                    Slot2SramWaitArm9 = (byte)BitRange(val, 0, 1);
                    Slot2Rom0WaitArm9 = (byte)BitRange(val, 2, 3);
                    Slot2Rom1WaitArm9 = (byte)BitRange(val, 4, 4);
                    Slot2RomPhiPinOutArm9 = (byte)BitRange(val, 5, 6);
                    Slot2AccessRights = BitTest(val, 7);
                    break;
                case 0x4000205:
                    // Console.WriteLine("write to exmemcnt b1");
                    Slot1AccessRights = BitTest(val, 3);
                    MainMemoryAccessPriority = BitTest(val, 7);
                    break;

                case 0x4000240: VRAMCNT[0] = val; break;
                case 0x4000241: VRAMCNT[1] = val; break;
                case 0x4000242: VRAMCNT[2] = val; break;
                case 0x4000243: VRAMCNT[3] = val; break;
                case 0x4000244: VRAMCNT[4] = val; break;
                case 0x4000245: VRAMCNT[5] = val; break;
                case 0x4000246: VRAMCNT[6] = val; break;
                case 0x4000248: VRAMCNT[7] = val; break;
                case 0x4000249: VRAMCNT[8] = val; break;

                case 0x4000247:
                    SharedRamControl = (byte)(val & 0b11);
                    break;
            }

            if (VramEnabledAndSet(2, 2) || VramEnabledAndSet(3, 2))
            {
                throw new NotImplementedException("Implement mapping VRAM banks C and D to ARM7");
            }
        }

        public byte ReadHwio8Nds7(uint addr)
        {
            byte val = 0;

            switch (addr)
            {
                case 0x4000204:
                    Console.WriteLine("read from exmemstat b0");
                    val |= (byte)((Slot2SramWaitArm7 & 0b11) << 0);
                    val |= (byte)((Slot2Rom0WaitArm7 & 0b11) << 2);
                    val |= (byte)((Slot2Rom1WaitArm7 & 0b1) << 4);
                    val |= (byte)((Slot2RomPhiPinOutArm7 & 0b11) << 5);
                    if (Slot2AccessRights) val = BitSet(val, 7);
                    break;
                case 0x4000205:
                    Console.WriteLine("read from exmemstat b1");
                    if (Slot1AccessRights) val = BitSet(val, 3);
                    if (MainMemoryAccessPriority) val = BitSet(val, 7);
                    val = BitSet(val, 6);
                    break;

                case 0x4000240:
                    if (VramEnabledAndSet(2, 2)) val = BitSet(val, 0);
                    if (VramEnabledAndSet(3, 2)) val = BitSet(val, 1);
                    break;
                case 0x4000241:
                    return SharedRamControl;
            }

            return val;
        }

        public void WriteHwio8Nds7(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x4000204:
                    Console.WriteLine("write to exmemstat b0");
                    Slot2SramWaitArm7 = (byte)BitRange(val, 0, 1);
                    Slot2Rom0WaitArm7 = (byte)BitRange(val, 2, 3);
                    Slot2Rom1WaitArm7 = (byte)BitRange(val, 4, 4);
                    Slot2RomPhiPinOutArm7 = (byte)BitRange(val, 5, 6);
                    break;
            }

            return;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool VramEnabledAndSet(uint bank, uint mst)
        {
            uint vramcntMst = VRAMCNT[bank] & 0b111U;
            bool vramcntEnable = BitTest(VRAMCNT[bank], 7);

            return vramcntEnable && vramcntMst == mst;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetOffset(uint bank)
        {
            return (uint)(VRAMCNT[bank] >> 3) & 0b11U;
        }
    }
}