using System.Net.Sockets;
using System.Text;
using AsaServerManager.Web.Constants;
using AsaServerManager.Web.Models.Rcon;

namespace AsaServerManager.Web.Infrastructure.Rcon;

internal sealed class RconConnection(TcpClient tcpClient, NetworkStream stream) : IAsyncDisposable
{
    private readonly TcpClient _tcpClient = tcpClient;
    private readonly NetworkStream _stream = stream;
    private int _nextPacketId = 100;

    public async Task AuthenticateAsync(string password, CancellationToken cancellationToken)
    {
        int authPacketId = GetNextPacketId();
        await WritePacketAsync(authPacketId, RconProtocolConstants.AuthPacketType, password, cancellationToken);

        for (int index = 0; index < 3; index++)
        {
            RconPacket packet = await ReadPacketAsync(cancellationToken);
            if (packet.Id == RconProtocolConstants.AuthFailureId)
            {
                throw new InvalidOperationException("RCON authentication failed.");
            }

            if (packet.Type == RconProtocolConstants.ExecPacketType && packet.Id == authPacketId)
            {
                return;
            }
        }

        throw new InvalidOperationException("RCON authentication did not return a valid response.");
    }

    public async Task<string> ExecuteAsync(string command, CancellationToken cancellationToken)
    {
        int requestId = GetNextPacketId();
        await WritePacketAsync(requestId, RconProtocolConstants.ExecPacketType, command, cancellationToken);

        StringBuilder responseBuilder = new();
        bool receivedResponse = false;

        while (true)
        {
            RconPacket packet = await ReadPacketAsync(cancellationToken);
            if (packet.Id == RconProtocolConstants.AuthFailureId)
            {
                throw new InvalidOperationException("RCON authentication was rejected.");
            }

            if (packet.Id != requestId)
            {
                if (receivedResponse && !_stream.DataAvailable)
                {
                    break;
                }

                continue;
            }

            if (packet.Type == RconProtocolConstants.ResponsePacketType)
            {
                responseBuilder.Append(packet.Body);
                receivedResponse = true;

                if (!await HasMoreDataSoonAsync(cancellationToken))
                {
                    break;
                }
            }
            else if (receivedResponse)
            {
                break;
            }
        }

        return responseBuilder.ToString();
    }

    private async Task<bool> HasMoreDataSoonAsync(CancellationToken cancellationToken)
    {
        if (_stream.DataAvailable)
        {
            return true;
        }

        await Task.Delay(RconProtocolConstants.ReadTimeoutMilliseconds, cancellationToken);
        return _stream.DataAvailable;
    }

    private async Task WritePacketAsync(int id, int type, string body, CancellationToken cancellationToken)
    {
        byte[] bodyBytes = Encoding.ASCII.GetBytes(body);
        int size = bodyBytes.Length + 10;
        byte[] packetBytes = new byte[size + 4];

        WriteInt32(packetBytes, 0, size);
        WriteInt32(packetBytes, 4, id);
        WriteInt32(packetBytes, 8, type);
        bodyBytes.CopyTo(packetBytes, 12);
        packetBytes[^2] = 0;
        packetBytes[^1] = 0;

        await _stream.WriteAsync(packetBytes, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    private async Task<RconPacket> ReadPacketAsync(CancellationToken cancellationToken)
    {
        byte[] sizeBytes = await ReadExactAsync(sizeof(int), cancellationToken);
        int size = BitConverter.ToInt32(sizeBytes, 0);
        if (size < 10)
        {
            throw new InvalidOperationException("Invalid RCON packet size.");
        }

        byte[] payloadBytes = await ReadExactAsync(size, cancellationToken);
        int id = BitConverter.ToInt32(payloadBytes, 0);
        int type = BitConverter.ToInt32(payloadBytes, 4);
        string body = Encoding.ASCII.GetString(payloadBytes, 8, size - 10).TrimEnd('\0');
        return new RconPacket(id, type, body);
    }

    private async Task<byte[]> ReadExactAsync(int byteCount, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[byteCount];
        int offset = 0;

        while (offset < byteCount)
        {
            int bytesRead = await _stream.ReadAsync(buffer.AsMemory(offset, byteCount - offset), cancellationToken);
            if (bytesRead == 0)
            {
                throw new InvalidOperationException("RCON connection closed.");
            }

            offset += bytesRead;
        }

        return buffer;
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        BitConverter.GetBytes(value).CopyTo(buffer, offset);
    }

    private int GetNextPacketId()
    {
        _nextPacketId++;
        return _nextPacketId;
    }

    public ValueTask DisposeAsync()
    {
        _stream.Dispose();
        _tcpClient.Dispose();
        return ValueTask.CompletedTask;
    }
}
