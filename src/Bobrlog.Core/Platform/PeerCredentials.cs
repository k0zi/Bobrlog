using System.Buffers.Binary;
using System.Net.Sockets;

namespace Bobrlog.Core.Platform;

public static class PeerCredentials
{
    private const int SolSocket = 1;
    private const int SoPeerCred = 17;

    /// <summary>Returns (pid, uid, gid) of the process on the other end of a Unix domain socket.</summary>
    public static (int Pid, uint Uid, uint Gid) Get(Socket socket)
    {
        Span<byte> buffer = stackalloc byte[12];
        var length = socket.GetRawSocketOption(SolSocket, SoPeerCred, buffer);
        if (length < 12)
            throw new SocketException((int)SocketError.ProtocolNotSupported);
        return (BinaryPrimitives.ReadInt32LittleEndian(buffer),
                BinaryPrimitives.ReadUInt32LittleEndian(buffer[4..]),
                BinaryPrimitives.ReadUInt32LittleEndian(buffer[8..]));
    }
}
