using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialVisionHost.Communication.Modbus
{
    public sealed class ModbusTcpClient : IDisposable
    {
        private const ushort ModbusProtocolId = 0;
        private const int MbapHeaderLength = 7;
        private readonly object stateSync = new object();
        private readonly SemaphoreSlim requestSync = new SemaphoreSlim(1, 1);
        private TcpClient? client;
        private NetworkStream? stream;
        private ushort nextTransactionId;

        public bool IsConnected
        {
            get
            {
                lock (stateSync)
                {
                    return client is not null;
                }
            }
        }

        public async Task ConnectAsync(
            string host,
            int port,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            ValidateEndpoint(host, port, timeout);

            lock (stateSync)
            {
                if (client is not null)
                {
                    throw new InvalidOperationException(
                        "Modbus TCP 客户端已经连接。");
                }
            }

            var newClient = new TcpClient { NoDelay = true };

            try
            {
                await newClient
                    .ConnectAsync(host.Trim(), port)
                    .WaitAsync(timeout, cancellationToken);

                lock (stateSync)
                {
                    if (client is not null)
                    {
                        throw new InvalidOperationException(
                            "连接期间已经建立了另一个 Modbus TCP 连接。");
                    }

                    client = newClient;
                    stream = newClient.GetStream();
                }
            }
            catch
            {
                newClient.Dispose();
                throw;
            }
        }

        public async Task<bool[]> ReadCoilsAsync(
            ushort startAddress,
            ushort quantity,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (quantity < 1 || quantity > 2000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(quantity),
                    "读取线圈数量必须在 1～2000 之间。");
            }

            byte[] response = await SendRequestAsync(
                0x01,
                CreateAddressQuantityPdu(0x01, startAddress, quantity),
                timeout,
                cancellationToken);
            int expectedByteCount = (quantity + 7) / 8;
            if (response.Length != expectedByteCount + 2 ||
                response[1] != expectedByteCount)
            {
                throw new InvalidDataException("读线圈响应长度不正确。");
            }

            var values = new bool[quantity];
            for (int index = 0; index < quantity; index++)
            {
                values[index] =
                    (response[2 + index / 8] & (1 << (index % 8))) != 0;
            }

            return values;
        }

        public async Task<ushort[]> ReadHoldingRegistersAsync(
            ushort startAddress,
            ushort quantity,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (quantity < 1 || quantity > 125)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(quantity),
                    "读取保持寄存器数量必须在 1～125 之间。");
            }

            byte[] response = await SendRequestAsync(
                0x03,
                CreateAddressQuantityPdu(0x03, startAddress, quantity),
                timeout,
                cancellationToken);
            int expectedByteCount = quantity * 2;
            if (response.Length != expectedByteCount + 2 ||
                response[1] != expectedByteCount)
            {
                throw new InvalidDataException("读保持寄存器响应长度不正确。");
            }

            var values = new ushort[quantity];
            for (int index = 0; index < quantity; index++)
            {
                values[index] = BinaryPrimitives.ReadUInt16BigEndian(
                    response.AsSpan(2 + index * 2, 2));
            }

            return values;
        }

        public async Task WriteSingleCoilAsync(
            ushort address,
            bool value,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            var request = new byte[5];
            request[0] = 0x05;
            BinaryPrimitives.WriteUInt16BigEndian(
                request.AsSpan(1, 2),
                address);
            BinaryPrimitives.WriteUInt16BigEndian(
                request.AsSpan(3, 2),
                value ? (ushort)0xFF00 : (ushort)0x0000);

            byte[] response = await SendRequestAsync(
                0x05,
                request,
                timeout,
                cancellationToken);
            ValidateWriteEcho(request, response, "写单线圈");
        }

        public async Task WriteSingleHoldingRegisterAsync(
            ushort address,
            ushort value,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            var request = new byte[5];
            request[0] = 0x06;
            BinaryPrimitives.WriteUInt16BigEndian(
                request.AsSpan(1, 2),
                address);
            BinaryPrimitives.WriteUInt16BigEndian(
                request.AsSpan(3, 2),
                value);

            byte[] response = await SendRequestAsync(
                0x06,
                request,
                timeout,
                cancellationToken);
            ValidateWriteEcho(request, response, "写单寄存器");
        }

        public void Disconnect()
        {
            TcpClient? clientToDispose;

            lock (stateSync)
            {
                clientToDispose = client;
                client = null;
                stream = null;
            }

            clientToDispose?.Dispose();
        }

        private async Task<byte[]> SendRequestAsync(
            byte functionCode,
            byte[] requestPdu,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeout),
                    "Modbus 请求超时时间必须大于 0。");
            }

            await requestSync.WaitAsync(cancellationToken);

            try
            {
                NetworkStream activeStream;
                ushort transactionId;

                lock (stateSync)
                {
                    activeStream = stream
                        ?? throw new InvalidOperationException(
                            "Modbus TCP 客户端尚未连接。");
                    transactionId = ++nextTransactionId;
                }

                byte[] request = BuildRequest(transactionId, requestPdu);
                using var timeoutCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);
                timeoutCancellation.CancelAfter(timeout);

                try
                {
                    await activeStream.WriteAsync(
                        request,
                        timeoutCancellation.Token);

                    var responseHeader = new byte[MbapHeaderLength];
                    await ReadExactlyAsync(
                        activeStream,
                        responseHeader,
                        timeoutCancellation.Token);

                    ushort responseTransactionId =
                        BinaryPrimitives.ReadUInt16BigEndian(
                            responseHeader.AsSpan(0, 2));
                    ushort protocolId = BinaryPrimitives.ReadUInt16BigEndian(
                        responseHeader.AsSpan(2, 2));
                    ushort length = BinaryPrimitives.ReadUInt16BigEndian(
                        responseHeader.AsSpan(4, 2));

                    if (responseTransactionId != transactionId ||
                        protocolId != ModbusProtocolId ||
                        responseHeader[6] != 1 ||
                        length < 2 ||
                        length > 254)
                    {
                        throw new InvalidDataException(
                            "Modbus TCP 响应的 MBAP 报文头无效。");
                    }

                    var responsePdu = new byte[length - 1];
                    await ReadExactlyAsync(
                        activeStream,
                        responsePdu,
                        timeoutCancellation.Token);
                    ValidateFunctionCode(functionCode, responsePdu);
                    return responsePdu;
                }
                catch (OperationCanceledException)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    Disconnect();
                    throw new TimeoutException(
                        $"Modbus 功能码 0x{functionCode:X2} 请求超时。");
                }
                catch (ModbusProtocolException)
                {
                    throw;
                }
                catch
                {
                    Disconnect();
                    throw;
                }
            }
            finally
            {
                requestSync.Release();
            }
        }

        private static byte[] CreateAddressQuantityPdu(
            byte functionCode,
            ushort address,
            ushort quantity)
        {
            var pdu = new byte[5];
            pdu[0] = functionCode;
            BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(1, 2), address);
            BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(3, 2), quantity);
            return pdu;
        }

        private static byte[] BuildRequest(
            ushort transactionId,
            byte[] requestPdu)
        {
            var request = new byte[MbapHeaderLength + requestPdu.Length];
            BinaryPrimitives.WriteUInt16BigEndian(
                request.AsSpan(0, 2),
                transactionId);
            BinaryPrimitives.WriteUInt16BigEndian(
                request.AsSpan(2, 2),
                ModbusProtocolId);
            BinaryPrimitives.WriteUInt16BigEndian(
                request.AsSpan(4, 2),
                (ushort)(requestPdu.Length + 1));
            request[6] = 1;
            requestPdu.CopyTo(request, MbapHeaderLength);
            return request;
        }

        private static void ValidateFunctionCode(
            byte requestedFunctionCode,
            byte[] responsePdu)
        {
            if (responsePdu.Length == 2 &&
                responsePdu[0] == (requestedFunctionCode | 0x80))
            {
                throw new ModbusProtocolException(
                    requestedFunctionCode,
                    responsePdu[1]);
            }

            if (responsePdu.Length == 0 ||
                responsePdu[0] != requestedFunctionCode)
            {
                throw new InvalidDataException(
                    "Modbus 响应功能码与请求不一致。");
            }
        }

        private static void ValidateWriteEcho(
            byte[] request,
            byte[] response,
            string operationName)
        {
            if (!request.SequenceEqual(response))
            {
                throw new InvalidDataException(
                    $"{operationName}响应没有正确回显请求内容。");
            }
        }

        private static async Task ReadExactlyAsync(
            NetworkStream activeStream,
            byte[] buffer,
            CancellationToken cancellationToken)
        {
            int offset = 0;

            while (offset < buffer.Length)
            {
                int read = await activeStream.ReadAsync(
                    buffer.AsMemory(offset, buffer.Length - offset),
                    cancellationToken);
                if (read == 0)
                {
                    throw new EndOfStreamException(
                        "Modbus TCP 远端已关闭连接。");
                }

                offset += read;
            }
        }

        private static void ValidateEndpoint(
            string host,
            int port,
            TimeSpan timeout)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException(
                    "Modbus TCP 地址不能为空。",
                    nameof(host));
            }

            if (port < 1 || port > 65535)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(port),
                    "Modbus TCP 端口必须在 1～65535 之间。");
            }

            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeout),
                    "Modbus TCP 连接超时时间必须大于 0。");
            }
        }

        public void Dispose()
        {
            Disconnect();
            requestSync.Dispose();
        }
    }

    public sealed class ModbusProtocolException : Exception
    {
        public ModbusProtocolException(byte functionCode, byte exceptionCode)
            : base(CreateMessage(functionCode, exceptionCode))
        {
            FunctionCode = functionCode;
            ExceptionCode = exceptionCode;
        }

        public byte FunctionCode { get; }

        public byte ExceptionCode { get; }

        private static string CreateMessage(
            byte functionCode,
            byte exceptionCode)
        {
            string reason = exceptionCode switch
            {
                1 => "非法功能码",
                2 => "非法数据地址",
                3 => "非法数据值",
                4 => "从站设备故障",
                _ => "未知异常"
            };

            return $"Modbus 功能码 0x{functionCode:X2} 执行失败：" +
                $"异常码 0x{exceptionCode:X2}（{reason}）。";
        }
    }
}
