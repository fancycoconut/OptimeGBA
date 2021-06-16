using static OptimeGBA.Bits;
using static OptimeGBA.MemoryUtil;
using System;
using static Util;

namespace OptimeGBA
{
    public enum SpiDevice : byte
    {
        PowerManager = 0,
        Firmware = 1,
        Touchscreen = 2
    }

    public enum SpiFlashState
    {
        Ready,
        Identification,
        ReceiveAddress,
        Reading,
        Status,
        TakePrefix, // For cartridges with IR and Flash
    }

    public enum SpiTouchscreenState
    {
        Ready,
        Command,
    }

    public unsafe sealed class Spi
    {
        public Nds7 Nds7;
        public byte[] Firmware;

        public Spi(Nds7 nds7)
        {
            Nds7 = nds7;
            Firmware = nds7.Nds.Provider.Firmware;
        }

        // From Nocash's original DS 
        byte[] Id = new byte[] { 0x20, 0x40, 0x12 };

        // SPICNT
        byte BaudRate;
        SpiDevice DeviceSelect;
        bool TransferSize;
        bool ChipSelHold;
        bool EnableIrq;
        bool EnableSpi;
        bool Busy;

        // Firmware flash state
        public SpiFlashState FlashState;
        public bool EnableWrite;
        public byte IdIndex;
        public uint Address;
        public byte AddressByteNum = 0;

        // Touchscreen state
        public SpiTouchscreenState TouchscreenState;
        public byte TouchscreenCommand;
        public byte TouchscreenDataByte;

        public ushort TouchAdcX;
        public ushort TouchAdcY;

        public void SetTouchPos(uint x, uint y)
        {
            ushort adcX1 = GetUshort(Firmware, 0x3FF58);
            ushort adcY1 = GetUshort(Firmware, 0x3FF5A);
            byte scrX1 = Firmware[0x3FF5C];
            byte scrY1 = Firmware[0x3FF5D];
            ushort adcX2 = GetUshort(Firmware, 0x3FF5E);
            ushort adcY2 = GetUshort(Firmware, 0x3FF60);
            byte scrX2 = Firmware[0x3FF62];
            byte scrY2 = Firmware[0x3FF63];

            // Convert screen coords to calibrated ADC touchscreen coords
            TouchAdcX = (ushort)((x - (scrX1 - 1)) * (adcX2 - adcX1) / (scrX2 - scrX1) + adcX1);
            TouchAdcY = (ushort)((y - (scrY1 - 1)) * (adcY2 - adcY1) / (scrY2 - scrY1) + adcY1);
        }

        public void ClearTouchPos()
        {
            TouchAdcX = 0;
            TouchAdcY = 0xFFF;
        }

        public byte OutData;

        public byte ReadHwio8(uint addr)
        {
            byte val = 0;
            switch (addr)
            {
                case 0x40001C0: // SPICNT B0
                    val |= BaudRate;
                    if (Busy) val = BitSet(val, 7);
                    break;
                case 0x40001C1: // SPICNT B1
                    val |= (byte)DeviceSelect;
                    if (TransferSize) val = BitSet(val, 2);
                    if (ChipSelHold) val = BitSet(val, 3);
                    if (EnableIrq) val = BitSet(val, 6);
                    if (EnableSpi) val = BitSet(val, 7);
                    break;

                case 0x40001C2: // SPIDATA
                    // Console.WriteLine("SPI: Read! " + Hex(InData, 2));
                    if (!EnableSpi) return 0;
                    return OutData;
            }

            return val;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x40001C0: // SPICNT B0
                    BaudRate = (byte)(val & 0b11);
                    break;
                case 0x40001C1: // SPICNT B1
                    DeviceSelect = (SpiDevice)(val & 0b11);
                    TransferSize = BitTest(val, 2);
                    bool oldChipSelHold = ChipSelHold;
                    ChipSelHold = BitTest(val, 3);
                    EnableIrq = BitTest(val, 6);
                    EnableSpi = BitTest(val, 7);

                    if (!EnableSpi)
                    {
                        ChipSelHold = false;
                    }
                    break;

                case 0x40001C2: // SPIDATA
                    TransferTo(val);
                    break;
            }
        }

        public void TransferTo(byte val)
        {
            if (EnableSpi)
            {
                switch (DeviceSelect)
                {
                    case SpiDevice.Firmware:
                        TransferToSpiFlash(val);
                        break;
                    case SpiDevice.Touchscreen:
                        TransferToTouchscreen(val);
                        break;
                    case SpiDevice.PowerManager:
                        // Console.WriteLine("Power manager access");
                        break;

                }
            }

            if (!ChipSelHold)
            {
                FlashState = SpiFlashState.Ready;
                TouchscreenState = SpiTouchscreenState.Ready;
            }
        }

        public void TransferToSpiFlash(byte val)
        {
            switch (FlashState)
            {
                case SpiFlashState.Ready:
                    // Console.WriteLine("SPI: Receive command! " + Hex(val, 2));
                    OutData = 0x00;
                    switch (val)
                    {
                        case 0x06:
                            EnableWrite = true;
                            break;
                        case 0x04:
                            EnableWrite = false;
                            break;
                        case 0x9F:
                            FlashState = SpiFlashState.Identification;
                            IdIndex = 0;
                            break;
                        case 0x03:
                            FlashState = SpiFlashState.ReceiveAddress;
                            Address = 0;
                            AddressByteNum = 0;
                            break;
                        case 0x05: // Identification
                            // Console.WriteLine("SPI ID");
                            OutData = 0x00;
                            break;
                        case 0x00:
                            break;
                        default:
                            throw new NotImplementedException("SPI: Unimplemented command: " + Hex(val, 2));
                    }
                    break;
                case SpiFlashState.ReceiveAddress:
                    // Console.WriteLine("SPI: Address byte write: " + Hex(val, 2));
                    Address |= (uint)(val << ((2 - AddressByteNum) * 8));
                    AddressByteNum++;
                    if (AddressByteNum > 2)
                    {
                        AddressByteNum = 0;
                        FlashState = SpiFlashState.Reading;
                        // Console.WriteLine("SPI: Address written: " + Hex(Address, 6));
                    }
                    break;
                case SpiFlashState.Reading:
                    // Console.WriteLine("SPI: Read from address: " + Hex(Address, 6));
                    // Nds7.Cpu.Error("SPI");
                    if (Address < 0x40000)
                    {
                        OutData = Firmware[Address];
                    }
                    else
                    {
                        OutData = 0;
                    }
                    Address += TransferSize ? 2U : 1U;
                    Address &= 0xFFFFFF;
                    break;
                case SpiFlashState.Identification:
                    OutData = Id[IdIndex];
                    IdIndex++;
                    IdIndex %= 3;
                    break;
            }
        }

        public void TransferToTouchscreen(byte val)
        {
            switch (TouchscreenState)
            {
                case SpiTouchscreenState.Ready:
                    TouchscreenState = SpiTouchscreenState.Command;
                    OutData = 0;
                    TouchscreenCommand = val;
                    TouchscreenDataByte = 0;
                    break;
                case SpiTouchscreenState.Command:
                    switch ((TouchscreenCommand >> 4) & 0b111)
                    {
                        case 1: // Y position
                            // Shift 12-byte up left three to get start with 1-bit dummy
                            OutData = (byte)((TouchAdcY << 3) >> (8 * (1 - (TouchscreenDataByte & 1))));
                            break;
                        case 5: // X position
                            OutData = (byte)((TouchAdcX << 3) >> (8 * (1 - (TouchscreenDataByte & 1))));
                            // Console.WriteLine("Y");
                            break;
                    }
                    TouchscreenDataByte++;
                    break;
            }
        }
    }
}