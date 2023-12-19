using NBitcoin;
using NBitcoin.DataEncoders;

namespace NBXplorer
{
    public class BSCNetworkSet : INetworkSet
    {
        public BSCNetworkSet(String _CryptoCode)
        {
            CryptoCode = _CryptoCode;
        }
        public Network GetNetwork(ChainName chainName)
        {
            NetworkBuilder builder = new NetworkBuilder();
            builder.SetChainName(chainName);
            builder.SetNetworkSet(new BSCNetworkSet(CryptoCode));
            builder
                .SetBase58Bytes(Base58Type.PUBKEY_ADDRESS, new byte[] {111})
                .SetBase58Bytes(Base58Type.SCRIPT_ADDRESS, new byte[] {196})
                .SetBase58Bytes(Base58Type.SECRET_KEY, new byte[] {239})
                .SetBase58Bytes(Base58Type.EXT_PUBLIC_KEY, new byte[] {0x04, 0x35, 0x87, 0xCF})
                .SetBase58Bytes(Base58Type.EXT_SECRET_KEY, new byte[] {0x04, 0x35, 0x83, 0x94})
                .SetBech32(Bech32Type.WITNESS_PUBKEY_ADDRESS, "tb")
                .SetBech32(Bech32Type.WITNESS_SCRIPT_ADDRESS, "tb")
                .SetBech32(Bech32Type.TAPROOT_ADDRESS, "tb")
                .SetMagic(0)
                .SetPort(38333)
                .SetRPCPort(38332)
                .SetName("bsc")
                .SetGenesis(Bitcoin.Instance.GetNetwork(chainName).GetGenesis().ToString());
        
            return builder.BuildAndRegister();
        }

        public Network Mainnet { get; }
        public Network Testnet { get; }
        public Network Regtest { get; }
        public string CryptoCode { get; }
    }
    
    
}
