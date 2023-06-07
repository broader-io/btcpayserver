#if ALTCOINS
using System;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.BSC.Payments;
using NBitcoin;
using Nethereum.HdWallet;

namespace BTCPayServer.Plugins.BSC.Payments
{
    public class BSCSupportedPaymentMethod : ISupportedPaymentMethod
    {
        public string CryptoCode { get; set; }
        public string Seed { get; set; }
        public string Password { get; set; }
        public string XPub { get; set; }
        public PaymentMethodId PaymentId => new PaymentMethodId(CryptoCode, BSCPaymentType.Instance);
        public long CurrentIndex { get; set; }
        public string KeyPath { get; set; }

        public Func<int, string> GetDerivedAddress()
        {
            if (string.IsNullOrEmpty(XPub))
            {
                return i =>
                {
                    Mnemonic mnemo = new Mnemonic(Seed);
                    ExtKey masterKey = mnemo.DeriveExtKey();
                    var ETH_keyPath = new KeyPath("m/44'/60'/0'/0/");
                    ExtKey ETH_derived = masterKey.Derive(ETH_keyPath);
                    ExtPubKey derivedETHPublicKey = ETH_derived.Neuter();
                    ExtPubKey ExtendedPublicKey = derivedETHPublicKey.Derive((uint)i);
                    PubKey ETH_publickKey = ExtendedPublicKey.GetPublicKey();
                    byte[] byte_ETH_publicKey = ETH_publickKey.Decompress().ToBytes();
                    var PubKeyNoPrefix = new byte[byte_ETH_publicKey.Length - 1];
                    Array.Copy(byte_ETH_publicKey, 1, PubKeyNoPrefix, 0, PubKeyNoPrefix.Length);
                    var initaddr = new Nethereum.Util.Sha3Keccack().CalculateHash(PubKeyNoPrefix);
                    var addr = new byte[initaddr.Length - 12];
                    Array.Copy(initaddr, 12, addr, 0, initaddr.Length - 12);
                    var hex_addr = BitConverter.ToString(addr).Replace("-", "");
                    string public_address = new Nethereum.Util.AddressUtil().ConvertToChecksumAddress(hex_addr);
                    Console.WriteLine("Generated address  " + 0 + ": " + public_address);
                    
                    BitcoinExtPubKey seedMainNetPublickKey = derivedETHPublicKey.GetWif(Network.Main);
                    Console.WriteLine(seedMainNetPublickKey.ToString());
                    
                    //string result = BitConverter.ToString(ExtendedPublicKey.ToBytes()).Replace("-", string.Empty);
                    return public_address;
                };
                //return i => new Wallet(Seed, Password, KeyPath).GetAccount(i).Address;
            }
            else
            {
                try
                {
                    return new PublicWallet(XPub).GetAddress;
                }
                catch (Exception)
                {
                    return new PublicWallet(new BitcoinExtPubKey(XPub, Network.Main).ExtPubKey).GetAddress;
                }
            }
        }
    }
}
#endif
