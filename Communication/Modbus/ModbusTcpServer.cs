using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialVisionHost.Communication.Modbus
{
    public sealed class ModbusTcpServer : IDisposable
    {
        private const ushort ModbusProtocolId = 0;
        private const int MbapHeaderLength = 7;
        private const int MaximumPduLength = 253;
        private readonly object stateSync = new object();
        private readonly List<TcpClient> clients = new List<TcpClient>();
        private TcpListener? listener;
        private CancellationTokenSource? cancellation;

        public ModbusTcpServer(ModbusDataStore dataStore)
        {
            DataStore = dataStore
                ?? throw new ArgumentNullException(nameof(dataStore));
        }

        public event Action<byte>? RequestProcessed;

        public ModbusDataStore DataStore { get; }

        public bool IsRunning
        {
            get
            {
                lock (stateSync)
                {
                    return listener is not null;
                }
            }
        }

        public int ListeningPort { get; private set; }

        public void Start(int port)
        {
            if (port < 0 || port > 65535)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(port),
                    "Modbus TCP 端口必须在 0～65535 之间。");
            }

            lock (stateSync)
            {
                if (listener is not null)
                {
                    throw new InvalidOperationException(
                        "Modbus TCP 服务端已经在运行。");
                }

                var newListener = new TcpListener(IPAddress.Loopback, port);
                newListener.Start();
                var newCancellation = new CancellationTokenSource();
                listener = newListener;
                cancellation = newCancellation;
                ListeningPort = ((IPEndPoint)newListener.LocalEndpoint).Port;
                _ = AcceptClientsAsync(newListener, newCancellation.Token);
            }
        }

        public void Stop()
        {
            TcpListener? listenerToStop;
            CancellationTokenSource? cancellationToStop;
            TcpClient[] clientsToClose;

            lock (stateSync)
            {
                listenerToStop = listener;
                cancellationToStop = cancellation;
                listener = null;
                cancellation = null;
                ListeningPort = 0;
                clientsToClose = clients.ToArray();
                clients.Clear();
            }

            cancellationToStop?.Cancel();
            listenerToStop?.Stop();

            foreach (TcpClient client in clientsToClose)
            {
                client.Dispose();
            }

            cancellationToStop?.Dispose();
        }

        private async Task AcceptClientsAsync(
            TcpListener activeListener,
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await activeListener.AcceptTcpClientAsync();
                    client.NoDelay = true;

                    lock (stateSync)
                    {
                        clients.Add(client);
                    }

                    _ = HandleClientAsync(client, cancellationToken);
                }
                catch (ObjectDisposedException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (SocketException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }

        private async Task HandleClientAsync(
            TcpClient client,
            CancellationToken cancellationToken)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                var header = new byte[MbapHeaderLength];

                while (!cancellationToken.IsCancellationRequested)
                {
                    bool hasRequest = await ReadExactlyAsync(
                        stream,
                        header,
                        cancellationToken);
                    if (!hasRequest)
                    {
                        return;
                    }

                    ushort transactionId =
                        BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(0, 2));
                    ushort protocolId =
                        BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2));
                    ushort length =
                        BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
                    byte unitId = header[6];

                    if (protocolId != ModbusProtocolId ||
                        length < 2 ||
                        length - 1 > MaximumPduLength)
                    {
                        throw new InvalidDataException("无效的 Modbus TCP MBAP 报文头。");
                    }

                    var requestPdu = new byte[length - 1];
                    if (!await ReadExactlyAsync(
                            stream,
                            requestPdu,
                            cancellationToken))
                    {
                        throw new EndOfStreamException("Modbus PDU 接收不完整。");
                    }

                    byte[] responsePdu = ProcessPdu(requestPdu);
                    byte[] response = BuildResponse(
                        transactionId,
                        unitId,
                        responsePdu);
                    await stream.WriteAsync(response, cancellationToken);
                    RequestProcessed?.Invoke(requestPdu[0]);
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (IOException)
            {
            }
            catch (SocketException)
            {
            }
            finally
            {
                lock (stateSync)
                {
                    clients.Remove(client);
                }

                client.Dispose();
            }
        }

        private byte[] ProcessPdu(byte[] requestPdu)
        {
            if (requestPdu.Length == 0)
            {
                return CreateExceptionResponse(0, 3);
            }

            byte functionCode = requestPdu[0];

            try
            {
                return functionCode switch
                {
                    0x01 => ReadCoils(requestPdu),
                    0x03 => ReadHoldingRegisters(requestPdu),
                    0x05 => WriteSingleCoil(requestPdu),
                    0x06 => WriteSingleRegister(requestPdu),
                    _ => CreateExceptionResponse(functionCode, 1)
                };
            }
            catch (ArgumentOutOfRangeException)
            {
                return CreateExceptionResponse(functionCode, 2);
            }
            catch (ArgumentException)
            {
                return CreateExceptionResponse(functionCode, 3);
            }
            catch
            {
                return CreateExceptionResponse(functionCode, 4);
            }
        }

        private byte[] ReadCoils(byte[] pdu)
        {
            ValidateRequestLength(pdu, 5);
            ushort address = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(1, 2));
            ushort quantity = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(3, 2));
            if (quantity < 1 || quantity > 2000)
            {
                throw new ArgumentException("读线圈数量无效。");
            }

            bool[] values = DataStore.ReadCoils(address, quantity);
            int byteCount = (quantity + 7) / 8;
            var response = new byte[2 + byteCount];
            response[0] = 0x01;
            response[1] = (byte)byteCount;

            for (int index = 0; index < values.Length; index++)
            {
                if (values[index])
                {
                    response[2 + index / 8] |= (byte)(1 << (index % 8));
                }
            }

            return response;
        }

        private byte[] ReadHoldingRegisters(byte[] pdu)
        {
            ValidateRequestLength(pdu, 5);
            ushort address = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(1, 2));
            ushort quantity = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(3, 2));
            if (quantity < 1 || quantity > 125)
            {
                throw new ArgumentException("读保持寄存器数量无效。");
            }

            ushort[] values = DataStore.ReadHoldingRegisters(address, quantity);
            var response = new byte[2 + quantity * 2];
            response[0] = 0x03;
            response[1] = (byte)(quantity * 2);

            for (int index = 0; index < values.Length; index++)
            {
                BinaryPrimitives.WriteUInt16BigEndian(
                    response.AsSpan(2 + index * 2, 2),
                    values[index]);
            }

            return response;
        }

        private byte[] WriteSingleCoil(byte[] pdu)
        {
            ValidateRequestLength(pdu, 5);
            ushort address = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(1, 2));
            ushort encodedValue = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(3, 2));
            if (encodedValue != 0xFF00 && encodedValue != 0x0000)
            {
                throw new ArgumentException("写单线圈值无效。");
            }

            DataStore.WriteCoil(address, encodedValue == 0xFF00);
            return pdu.ToArray();
        }

        private byte[] WriteSingleRegister(byte[] pdu)
        {
            ValidateRequestLength(pdu, 5);
            ushort address = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(1, 2));
            ushort value = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(3, 2));
            DataStore.WriteHoldingRegister(address, value);
            return pdu.ToArray();
        }

        private static void ValidateRequestLength(byte[] pdu, int expectedLength)
        {
            if (pdu.Length != expectedLength)
            {
                throw new ArgumentException("Modbus PDU 长度无效。");
            }
        }

        private static byte[] CreateExceptionResponse(
            byte functionCode,
            byte exceptionCode)
        {
            return new[] { (byte)(functionCode | 0x80), exceptionCode };
        }

        private static byte[] BuildResponse(
            ushort transactionId,
            byte unitId,
            byte[] responsePdu)
        {
            var response = new byte[MbapHeaderLength + responsePdu.Length];
            BinaryPrimitives.WriteUInt16BigEndian(
                response.AsSpan(0, 2),
                transactionId);
            BinaryPrimitives.WriteUInt16BigEndian(
                response.AsSpan(2, 2),
                ModbusProtocolId);
            BinaryPrimitives.WriteUInt16BigEndian(
                response.AsSpan(4, 2),
                (ushort)(responsePdu.Length + 1));
            response[6] = unitId;
            responsePdu.CopyTo(response, MbapHeaderLength);
            return response;
        }

        private static async Task<bool> ReadExactlyAsync(
            NetworkStream stream,
            byte[] buffer,
            CancellationToken cancellationToken)
        {
            int offset = 0;

            while (offset < buffer.Length)
            {
                int read = await stream.ReadAsync(
                    buffer.AsMemory(offset, buffer.Length - offset),
                    cancellationToken);
                if (read == 0)
                {
                    if (offset == 0)
                    {
                        return false;
                    }

                    throw new EndOfStreamException("Modbus TCP 报文接收不完整。");
                }

                offset += read;
            }

            return true;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
