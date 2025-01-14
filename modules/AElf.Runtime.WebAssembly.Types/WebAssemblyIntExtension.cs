using System.Numerics;
using Nethereum.ABI;

namespace AElf.Runtime.WebAssembly.Types;

public static class WebAssemblyIntExtension
{
    public static ABIValue ToWebAssemblyUInt256(this long value)
    {
        var bytes = new BigInteger(value).ToByteArray().RightPad(32);
        Array.Reverse(bytes, 0, bytes.Length);
        return new ABIValue("uint256", new BigInteger(bytes, true));
    }

    public static ABIValue ToWebAssemblyUInt256(this int value)
    {
        var bytes = new BigInteger(value).ToByteArray().RightPad(32);
        Array.Reverse(bytes, 0, bytes.Length);
        return new ABIValue("uint256", new BigInteger(bytes, true));
    }
}