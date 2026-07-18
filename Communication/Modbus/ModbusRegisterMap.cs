namespace IndustrialVisionHost.Communication.Modbus
{
    public static class ModbusRegisterMap
    {
        public const ushort StartRequestCoil = 0;

        public const ushort BusyCoil = 1;

        public const ushort CompletedCoil = 2;

        public const ushort ResultOkCoil = 3;

        public const ushort ResultNgCoil = 4;

        public const ushort CycleIdRegister = 0;

        public const ushort ResultCodeRegister = 1;

        public const ushort ResultNone = 0;

        public const ushort ResultOk = 1;

        public const ushort ResultNg = 2;
    }
}
