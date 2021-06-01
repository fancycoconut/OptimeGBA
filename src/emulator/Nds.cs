using System;
using static OptimeGBA.MemoryUtil;
using static Util;

namespace OptimeGBA
{
    public unsafe sealed class Nds
    {
        public ProviderNds Provider;

        public Nds7 Nds7;
        public Nds9 Nds9;
        public Scheduler Scheduler;

        public Cp15 Cp15;

        public CartridgeNds Cartridge;

        public Ipc[] Ipcs; // 0: ARM7 to ARM9, 1: ARM9 to ARM7

        public PpuNds Ppu;

        public NdsAudio Audio;

        public MemoryControlNds MemoryControl;

        public Keypad Keypad = new Keypad();

        public RtcNds Rtc;

        public byte[] MainRam = new byte[4194304];
        public byte[] SharedRam = new byte[32768];

        public int Arm9PendingTicks;

        public Nds(ProviderNds provider)
        {
            Provider = provider;
            Scheduler = new Scheduler();
            // AudioCallback = provider.AudioCallback;

            Ipcs = new Ipc[] {
                new Ipc(this, 0),
                new Ipc(this, 1),
            };

            Cp15 = new Cp15(this);

            Cartridge = new CartridgeNds(this);

            Ppu = new PpuNds(this, Scheduler);
            Ppu.Renderers[0].DisableColorCorrection();
            Ppu.Renderers[1].DisableColorCorrection();

            Audio = new NdsAudio(this);

            MemoryControl = new MemoryControlNds();

            Rtc = new RtcNds();

            Nds7 = new Nds7(this) { Scheduler = Scheduler };
            Nds9 = new Nds9(this) { Scheduler = Scheduler };

#if UNSAFE
            Console.WriteLine("Starting in memory UNSAFE mode");
#else
            Console.WriteLine("Starting in memory SAFE mode");
#endif

            if (provider.DirectBoot)
            {
                // Firmware init
                MemoryControl.SharedRamControl = 3;
                Nds7.POSTFLG = 1;
                Nds9.POSTFLG = 1;

                var rom = provider.Rom;
                if (rom.Length >= 0x20)
                {
                    uint arm9RomOffset = GetUint(rom, 0x20);
                    uint arm9EntryAddr = GetUint(rom, 0x24);
                    uint arm9RamAddr = GetUint(rom, 0x28);
                    uint arm9Size = GetUint(rom, 0x2C);
                    uint arm7RomOffset = GetUint(rom, 0x30);
                    uint arm7EntryAddr = GetUint(rom, 0x34);
                    uint arm7RamAddr = GetUint(rom, 0x38);
                    uint arm7Size = GetUint(rom, 0x3C);

                    // ROM offset is aligned by 0x1000
                    Console.WriteLine("ARM7 ROM Offset: " + Hex(arm7RomOffset, 8));
                    Console.WriteLine("ARM7 RAM Address: " + Hex(arm7RamAddr, 8));
                    for (uint i = 0; i < arm7Size; i++)
                    {
                        Nds7.Mem.Write8(arm7RamAddr + i, rom[arm7RomOffset + i]);
                    }
                    Nds7.Cpu.R[13] = 0x3002F7C;
                    Nds7.Cpu.R13irq = 0x3003F80;
                    Nds7.Cpu.R13svc = 0x3003FC0;
                    Nds7.Cpu.R[12] = arm7EntryAddr;
                    Nds7.Cpu.R[14] = arm7EntryAddr;
                    Nds7.Cpu.R[15] = arm7EntryAddr;
                    Nds7.Cpu.FlushPipeline();

                    Console.WriteLine("ARM9 ROM Offset: " + Hex(arm9RomOffset, 8));
                    Console.WriteLine("ARM9 RAM Address: " + Hex(arm9RamAddr, 8));
                    for (uint i = 0; i < arm9Size; i++)
                    {
                        Nds9.Mem.Write8(arm9RamAddr + i, rom[arm9RomOffset + i]);
                    }
                    Nds9.Cpu.R[13] = 0x380FD80;
                    Nds9.Cpu.R13irq = 0x380FF80;
                    Nds9.Cpu.R13svc = 0x380FFC0;
                    Nds9.Cpu.R[12] = arm9EntryAddr;
                    Nds9.Cpu.R[14] = arm9EntryAddr;
                    Nds9.Cpu.R[15] = arm9EntryAddr;
                    Nds9.Cpu.FlushPipeline();

                    Cp15.TransferTo(0, 0x0005707D, 1, 0, 0); // CP15 Control
                    Cp15.TransferTo(0, 0x0300000A, 9, 1, 0); // Data TCM base/size
                    Cp15.TransferTo(0, 0x00000020, 9, 1, 1); // Instruction TCM size
                    Nds9.Mem.Write8(0x4000247,   0x03); // WRAMCNT
                    Nds9.Mem.Write16(0x4000304, 0x0001); // POWCNT1
                    Nds9.Mem.Write16(0x4000504, 0x0200); // SOUNDBIAS

                    Nds9.Mem.Write32(0x027FF800, 0x1FC2); // Chip ID 1
                    Nds9.Mem.Write32(0x027FF804, 0x1FC2); // Chip ID 2
                    Nds9.Mem.Write16(0x027FF850, 0x5835); // ARM7 BIOS CRC
                    Nds9.Mem.Write16(0x027FF880, 0x0007); // Message from ARM9 to ARM7
                    Nds9.Mem.Write16(0x027FF884, 0x0006); // ARM7 boot task
                    Nds9.Mem.Write32(0x027FFC00, 0x1FC2); // Copy of chip ID 1
                    Nds9.Mem.Write32(0x027FFC04, 0x1FC2); // Copy of chip ID 2
                    Nds9.Mem.Write16(0x027FFC10, 0x5835); // Copy of ARM7 BIOS CRC
                    Nds9.Mem.Write16(0x027FFC40, 0x0001); // Boot indicator
                }
            }
        }

        public uint Step()
        {
            long beforeTicks = Scheduler.CurrentTicks;

            // Running both CPUs at 1CPI at 32 MHz causes the firmware to loop the setup screen,
            // so don't do that when not debugging simple test ROMs
            Nds7.Cpu.Execute();
            Nds9.Cpu.Execute();
            Scheduler.CurrentTicks += 1;


            // TODO: Proper NDS timings
            // uint ticks7 = 0;
            // ticks7 += Nds7.Cpu.Execute(); // Don't really need to tightly
            // ticks7 += Nds7.Cpu.Execute(); // synchronize, run a few ARM7
            // ticks7 += Nds7.Cpu.Execute(); // instructions and then run the
            // ticks7 += Nds7.Cpu.Execute(); // ARM9. 
            // Arm9PendingTicks += (int)ticks7 * 2; // ARM9 runs at twice the speed of ARM7
            // while (Arm9PendingTicks > 0)
            // {
            //     Arm9PendingTicks -= (int)Nds9.Cpu.Execute();
            // }
            // Scheduler.CurrentTicks += ticks7;

            // while (Scheduler.CurrentTicks >= Scheduler.NextEventTicks)
            // {
            //     long current = Scheduler.CurrentTicks;
            //     long next = Scheduler.NextEventTicks;
            //     Scheduler.PopFirstEvent().Callback(current - next);
            // }

            return (uint)(Scheduler.CurrentTicks - beforeTicks);
        }

        public void DoNothing(long cyclesLate) { }

        public void Tick(uint cycles)
        {
            Scheduler.CurrentTicks += cycles;
        }

        public void StateChange()
        {
            Scheduler.AddEventRelative(SchedulerId.None, 0, DoNothing);
        }

        public void HaltSkip(long cyclesOffset) { }
    }


}