using Lagrange.Core.Common;
using Lagrange.Core.Internal.Packets.System;
using Lagrange.Core.Utility;
using Lagrange.Core.Utility.Binary;
using Lagrange.Core.Utility.Binary.Compression;
using Lagrange.Core.Utility.Extension;
using Lagrange.Core.Utility.Generator;
using Lagrange.Core.Utility.Sign;
using ProtoBuf;
using static Lagrange.Core.Utility.Binary.BinaryPacket;
using BitConverter = Lagrange.Core.Utility.Binary.BitConverter;

namespace Lagrange.Core.Internal.Packets;

internal static class SsoPacker
{
    /// <summary>
    /// Build Protocol 12 SSO packet
    /// </summary>
    public static BinaryPacket Build(SsoPacket packet, BotAppInfo appInfo, BotDeviceInfo device, BotKeystore keystore, SignProvider signProvider)
    {
        var writer = new BinaryPacket();

        var sign = signProvider.Sign(packet.Command, packet.Sequence, packet.Payload.ToArray(), out var software, out var token);
        var signature = new NTDeviceSign
        {
            Sign = sign == null ? null : new Sign
            {
                S = software == null ? new Software { Ver = appInfo.PackageSign } : Serializer.Deserialize<Software>(new MemoryStream(software)),
                Token = token,
                Signature = sign
            },
            Trace = StringGen.GenerateTrace(),
            Uid = keystore.Uid
        };
        var stream = new MemoryStream();
        Serializer.Serialize(stream, signature);
        
        writer.Barrier(typeof(uint), () => new BinaryPacket() // Barrier is used to calculate the length of the packet header only
            .WriteUint(packet.Sequence, false) // sequence
            .WriteUint((uint)appInfo.SubAppId, false) // appId
            .WriteUint(2052, false) // LocaleId
            .WriteBytes("020000000000000000000000".UnHex().AsSpan())
            .WriteBytes(keystore.Session.Tgt, Prefix.Uint32 | Prefix.WithPrefix)
            .WriteString(packet.Command, Prefix.Uint32 | Prefix.WithPrefix)
            .WriteBytes(Array.Empty<byte>(), Prefix.Uint32 | Prefix.WithPrefix) // TODO: unknown
            .WriteString(device.Guid.ToByteArray().Hex().ToLower(), Prefix.Uint32 | Prefix.WithPrefix)
            .WriteBytes(Array.Empty<byte>(), Prefix.Uint32 | Prefix.WithPrefix) // TODO: unknown
            .WriteString(appInfo.CurrentVersion, Prefix.Uint16 | Prefix.WithPrefix) // Actually at wtlogin.trans_emp, this string is empty and only prefix 00 02 is given, but we can just simply ignore that situation
            .WriteBytes(stream.ToArray(), Prefix.Uint32 | Prefix.WithPrefix), false, true); // packet end
        
        return writer.WriteBytes(packet.Payload.ToArray(), Prefix.Uint32 | Prefix.WithPrefix);
    }

    /// <summary>
    /// Parse Protocol 12 SSO packet
    /// </summary>
    public static SsoPacket Parse(BinaryPacket packet)
    {
        uint _ = packet.ReadUint(false); // header length
        uint sequence = packet.ReadUint(false);
        ulong dummy = packet.ReadUlong(false);
        string command = packet.ReadString(Prefix.Uint32 | Prefix.WithPrefix);
        packet.ReadString(Prefix.Uint32 | Prefix.WithPrefix); // unknown
        int isCompressed = packet.ReadInt(); 
        packet.ReadBytes(Prefix.Uint32 | Prefix.LengthOnly); // Dummy Sso header
        
        return new SsoPacket(12, command, sequence, isCompressed == 0 ? packet : InflatePacket(packet));
    }

    private static BinaryPacket InflatePacket(BinaryPacket original)
    {
        var raw = original.ReadBytes(Prefix.Uint32 | Prefix.WithPrefix);
        var decompressed = ZCompression.ZDecompress(raw);
        
        var stream = new MemoryStream();
        stream.Write(BitConverter.GetBytes(decompressed.Length + sizeof(int), false));
        stream.Write(decompressed);
        stream.Position = 0;
        
        return new BinaryPacket(stream);
    }
}