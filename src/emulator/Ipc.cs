using System;
using static OptimeGBA.Bits;

namespace OptimeGBA
{
    public sealed class Ipc
    {
        Nds Nds;
        byte Id;

        public Ipc(Nds nds, byte id)
        {
            Nds = nds;
            Id = id;
        }

        public CircularBuffer<uint> RecvFifo = new CircularBuffer<uint>(16, 0);
        public uint LastSendValue;
        public uint LastRecvValue;

        public bool SendFifoEmptyIrqLevel;
        public bool RecvFifoPendingIrqLevel;

        public byte IpcSyncDataOut;

        // IPCSYNC
        public bool EnableRemoteIrq;

        // IPCFIFOCNT
        public bool EnableSendFifoEmptyIrq;
        public bool EnableRecvFifoPendingIrq;

        public bool FifoError;
        public bool EnableFifos;

        public byte ReadHwio8(uint addr)
        {
            byte val = 0;
            switch (addr)
            {
                case 0x4000180: // IPCSYNC B0
                    val |= GetRemote().IpcSyncDataOut;
                    break;
                case 0x4000181: // IPCSYNC B1
                    val |= IpcSyncDataOut;

                    if (EnableRemoteIrq) val = BitSet(val, 14 - 8);
                    break;

                case 0x4000184: // IPCFIFOCNT B0
                    if (GetRemote().RecvFifo.Entries == 0) val = BitSet(val, 0); // Send FIFO empty
                    if (GetRemote().RecvFifo.Entries == 16) val = BitSet(val, 1); // Send FIFO full
                    if (EnableSendFifoEmptyIrq) val = BitSet(val, 2);
                    CheckSendFifoEmptyIrq();
                    break;
                case 0x4000185: // IPCFIFOCNT B1
                    if (RecvFifo.Entries == 0) val = BitSet(val, 0); // Receive FIFO empty
                    if (RecvFifo.Entries == 16) val = BitSet(val, 1); // Receive FIFO full
                    if (EnableRecvFifoPendingIrq) val = BitSet(val, 2);
                    CheckRecvFifoPendingIrq();

                    if (FifoError) val = BitSet(val, 6);
                    if (EnableFifos) val = BitSet(val, 7);
                    break;

                case 0x4100000: // IPCFIFORECV B0
                    if (RecvFifo.Entries > 0)
                    {
                        if (EnableFifos)
                        {
                            LastRecvValue = RecvFifo.Pop();
                            GetRemote().CheckSendFifoEmptyIrq();
                        }
                    }
                    else
                    {
                        FifoError = true;
                    }
                    val = (byte)(LastRecvValue >> 0);
                    break;
                case 0x4100001: // IPCFIFORECV B1
                    val = (byte)(LastRecvValue >> 8);
                    break;
                case 0x4100002: // IPCFIFORECV B2
                    val = (byte)(LastRecvValue >> 16);
                    break;
                case 0x4100003: // IPCFIFORECV B3
                    val = (byte)(LastRecvValue >> 24);
                    break;

            }

            return val;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x4000180: // IPCSYNC B0
                    break;
                case 0x4000181: // IPCSYNC B1
                    IpcSyncDataOut = (byte)(val & 0xF);

                    // send IRQ to remote 
                    if (BitTest(val, 13 - 8) && GetRemote().EnableRemoteIrq)
                    {
                        // Console.WriteLine($"[{Id}] Sending IRQ");
                        switch (Id)
                        {
                            case 0:
                                Nds.Nds9.HwControl.FlagInterrupt(InterruptNds.IpcSync);
                                break;
                            case 1:
                                Nds.Nds7.HwControl.FlagInterrupt(InterruptNds.IpcSync);
                                break;
                        }
                    }
                    EnableRemoteIrq = BitTest(val, 14 - 8);
                    break;

                case 0x4000184: // IPCFIFOCNT B0
                    EnableSendFifoEmptyIrq = BitTest(val, 2);
                    if (BitTest(val, 3))
                    {
                        GetRemote().RecvFifo.Reset();
                    }
                    break;
                case 0x4000185: // IPCFIFOCNT B1
                    EnableRecvFifoPendingIrq = BitTest(val, 2);

                    if (BitTest(val, 6))
                    {
                        FifoError = false;
                    }
                    EnableFifos = BitTest(val, 7);
                    break;

                case 0x4000188: // IPCFIFOSEND B0
                    LastSendValue &= 0xFFFFFF00;
                    LastSendValue |= (uint)(val << 0);
                    break;
                case 0x4000189: // IPCFIFOSEND B1
                    LastSendValue &= 0xFFFF00FF;
                    LastSendValue |= (uint)(val << 8);
                    break;
                case 0x400018A: // IPCFIFOSEND B2
                    LastSendValue &= 0xFF00FFFF;
                    LastSendValue |= (uint)(val << 16);
                    break;
                case 0x400018B: // IPCFIFOSEND B3
                    LastSendValue &= 0x00FFFFFF;
                    LastSendValue |= (uint)(val << 24);
                    if (EnableFifos)
                    {
                        GetRemote().RecvFifo.Insert(LastSendValue);
                        GetRemote().CheckRecvFifoPendingIrq();
                    }
                    break;
            }
        }

        public Ipc GetRemote()
        {
            return Nds.Ipcs[Id ^ 1];
        }

        public void CheckSendFifoEmptyIrq()
        {
            var prev = SendFifoEmptyIrqLevel;
            SendFifoEmptyIrqLevel = GetRemote().RecvFifo.Entries == 0 && EnableSendFifoEmptyIrq;
            if (!prev && SendFifoEmptyIrqLevel)
            {
                FlagSourceInterrupt(InterruptNds.IpcSendFifoEmpty);
            }
        }

        public void CheckRecvFifoPendingIrq()
        {
            var prev = RecvFifoPendingIrqLevel;
            RecvFifoPendingIrqLevel = RecvFifo.Entries > 0 && EnableRecvFifoPendingIrq;
            if (!prev && RecvFifoPendingIrqLevel)
            {
                FlagSourceInterrupt(InterruptNds.IpcRecvFifoPending);
            }
        }

        public void FlagSourceInterrupt(InterruptNds interrupt)
        {
            switch (Id)
            {
                case 0:
                    Nds.Nds7.HwControl.FlagInterrupt(interrupt);
                    break;
                case 1:
                    Nds.Nds9.HwControl.FlagInterrupt(interrupt);
                    break;
            }
        }
    }
}