using System;

namespace IndustrialVisionHost.Communication.Modbus
{
    public sealed class ModbusDataStore
    {
        private readonly object dataSync = new object();
        private readonly bool[] coils;
        private readonly ushort[] holdingRegisters;

        public ModbusDataStore(int coilCount = 100, int holdingRegisterCount = 100)
        {
            if (coilCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(coilCount),
                    "线圈数量必须大于 0。");
            }

            if (holdingRegisterCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(holdingRegisterCount),
                    "保持寄存器数量必须大于 0。");
            }

            coils = new bool[coilCount];
            holdingRegisters = new ushort[holdingRegisterCount];
        }

        public int CoilCount => coils.Length;

        public int HoldingRegisterCount => holdingRegisters.Length;

        public bool ReadCoil(ushort address)
        {
            ValidateRange(address, 1, coils.Length, "线圈");

            lock (dataSync)
            {
                return coils[address];
            }
        }

        public bool[] ReadCoils(ushort startAddress, ushort count)
        {
            ValidateRange(startAddress, count, coils.Length, "线圈");
            var result = new bool[count];

            lock (dataSync)
            {
                Array.Copy(coils, startAddress, result, 0, count);
            }

            return result;
        }

        public void WriteCoil(ushort address, bool value)
        {
            ValidateRange(address, 1, coils.Length, "线圈");

            lock (dataSync)
            {
                coils[address] = value;
            }
        }

        public void WriteCoils(ushort startAddress, bool[] values)
        {
            if (values is null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            ValidateRange(startAddress, values.Length, coils.Length, "线圈");

            lock (dataSync)
            {
                Array.Copy(values, 0, coils, startAddress, values.Length);
            }
        }

        public ushort ReadHoldingRegister(ushort address)
        {
            ValidateRange(address, 1, holdingRegisters.Length, "保持寄存器");

            lock (dataSync)
            {
                return holdingRegisters[address];
            }
        }

        public ushort[] ReadHoldingRegisters(ushort startAddress, ushort count)
        {
            ValidateRange(
                startAddress,
                count,
                holdingRegisters.Length,
                "保持寄存器");
            var result = new ushort[count];

            lock (dataSync)
            {
                Array.Copy(
                    holdingRegisters,
                    startAddress,
                    result,
                    0,
                    count);
            }

            return result;
        }

        public void WriteHoldingRegister(ushort address, ushort value)
        {
            ValidateRange(address, 1, holdingRegisters.Length, "保持寄存器");

            lock (dataSync)
            {
                holdingRegisters[address] = value;
            }
        }

        public void WriteHoldingRegisters(ushort startAddress, ushort[] values)
        {
            if (values is null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            ValidateRange(
                startAddress,
                values.Length,
                holdingRegisters.Length,
                "保持寄存器");

            lock (dataSync)
            {
                Array.Copy(
                    values,
                    0,
                    holdingRegisters,
                    startAddress,
                    values.Length);
            }
        }

        private static void ValidateRange(
            int startAddress,
            int count,
            int capacity,
            string areaName)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count),
                    $"{areaName}读取或写入数量必须大于 0。");
            }

            if (startAddress < 0 ||
                startAddress >= capacity ||
                (long)startAddress + count > capacity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(startAddress),
                    $"{areaName}地址范围超出内存区。起始地址={startAddress}，" +
                    $"数量={count}，容量={capacity}。");
            }
        }
    }
}
