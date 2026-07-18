using IndustrialVisionHost.Communication.Modbus;

namespace IndustrialVisionHost.Tests;

public sealed class ModbusIntegrationTests
{
    [Fact]
    public async Task ClientAndServer_ReadAndWriteCoilsAndRegisters()
    {
        var store = new ModbusDataStore();
        using var server = new ModbusTcpServer(store);
        using var client = new ModbusTcpClient();
        int processedRequests = 0;
        server.RequestProcessed += _ => Interlocked.Increment(ref processedRequests);
        server.Start(0);
        await client.ConnectAsync(
            "127.0.0.1",
            server.ListeningPort,
            TimeSpan.FromSeconds(2));

        await client.WriteSingleCoilAsync(
            7, true, TimeSpan.FromSeconds(2));
        bool[] coils = await client.ReadCoilsAsync(
            7, 1, TimeSpan.FromSeconds(2));
        await client.WriteSingleHoldingRegisterAsync(
            9, 4321, TimeSpan.FromSeconds(2));
        ushort[] registers = await client.ReadHoldingRegistersAsync(
            9, 1, TimeSpan.FromSeconds(2));

        Assert.True(Assert.Single(coils));
        Assert.Equal((ushort)4321, Assert.Single(registers));
        Assert.True(store.ReadCoil(7));
        Assert.Equal((ushort)4321, store.ReadHoldingRegister(9));
        Assert.Equal(4, Volatile.Read(ref processedRequests));
    }

    [Fact]
    public async Task Server_ReturnsProtocolExceptionForOutOfRangeAddress()
    {
        using var server = new ModbusTcpServer(new ModbusDataStore(10, 10));
        using var client = new ModbusTcpClient();
        server.Start(0);
        await client.ConnectAsync(
            "127.0.0.1",
            server.ListeningPort,
            TimeSpan.FromSeconds(2));

        ModbusProtocolException exception = await Assert.ThrowsAsync<
            ModbusProtocolException>(() => client.ReadHoldingRegistersAsync(
                9,
                2,
                TimeSpan.FromSeconds(2)));

        Assert.Equal((byte)0x03, exception.FunctionCode);
        Assert.Equal((byte)0x02, exception.ExceptionCode);
    }
}
