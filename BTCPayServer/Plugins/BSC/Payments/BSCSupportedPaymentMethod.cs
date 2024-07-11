#if ALTCOINS
using System;
using BTCPayServer.Payments;
using NBitcoin;
using Nethereum.HdWallet;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.BSC
{
    public class BSCSupportedPaymentMethod : ISupportedPaymentMethod
    {
        [JsonProperty(PropertyName = "label")]
        public string Label { get; set; }
        
        [JsonProperty(PropertyName = "source")]
        public string Source { get; set; }

        [JsonProperty(PropertyName = "accountDerivation")]
        public string AccountDerivation { get; set; }
        
        [JsonProperty(PropertyName = "currentIndex")]
        public int CurrentIndex { get; set; }

        [JsonProperty(PropertyName = "cryptoCode")]
        public string CryptoCode { get; set; }

        [JsonIgnore] 
        public PaymentMethodId PaymentId => new(CryptoCode, BSCPaymentType.Instance);

        [JsonProperty(PropertyName = "accountKeySettings")]
        public AccountKeySettings[] AccountKeySettings = {};

        public int GetAccountNumber()
        {
            string KeyPath = AccountKeySettings[0].AccountKeyPath;
            var parts = KeyPath.Split("/");
            var accountNumber = parts[3].Replace("'", "");
            return Int32.Parse(accountNumber);
        }

        public string GetKeyPath()
        {
            return AccountKeySettings[0].AccountKeyPath;
        }

        public Func<int, string> GetDerivedAddress()
        {
            return i =>
            {
                // var deposit = new KeyPathTemplates(null).GetKeyPathTemplate(DerivationFeature.Deposit);
                // var AccountDerivation = new DirectDerivationStrategy(new BitcoinExtPubKey(accountDerivation, Network), false);
                // var line = AccountDerivation.GetLineFor(deposit);
                // var keyPath = deposit.GetKeyPath(0);
                // var derivation = line.Derive(0);
                // var dest = derivation.ScriptPubKey.GetDestination();
                // var address = derivation.ScriptPubKey.GetDestination().GetAddress();


                var k0 = new BitcoinExtPubKey(AccountDerivation, Network.Main);
                var p0 = new PublicWallet(k0);
                var addr0 = p0.GetAddress(0);

                var kp1 = KeyPath.Parse("0/0");
                var k1 = new BitcoinExtPubKey(AccountDerivation, Network.Main).Derive(kp1);
                var p1 = new PublicWallet(k1);
                var addr1 = p1.GetAddress(0);

                var kp2 = KeyPath.Parse("1/0");
                var k2 = new BitcoinExtPubKey(AccountDerivation, Network.Main).Derive(kp2);
                var p2 = new PublicWallet(k2);
                var addr2 = p2.GetAddress(0);

                //derivation.
                string xpub1 = new BitcoinExtPubKey(AccountDerivation, Network.Main).ToWif();
                Console.WriteLine(xpub1);
                // string addr1 = new PublicWallet(new BitcoinExtPubKey(accountDerivation, Network).ExtPubKey)
                //     .GetChildPublicWallet(0).GetAddress(0);
                // string addr2 = new PublicWallet(new BitcoinExtPubKey(accountDerivation, Network).ExtPubKey)
                //     .GetAddress(0);
                //return new PublicWallet(new BitcoinExtPubKey(accountDerivation, Network).ExtPubKey).GetExtPubKey().Derive().GetAddress(i);
                return new PublicWallet(
                        new BitcoinExtPubKey(AccountDerivation, Network.Main))
                    .GetAddress(i);
            };
        }
    }

    public class AccountKeySettings
    {
        [JsonProperty(PropertyName = "accountKeyPath")]
        public string AccountKeyPath { get; set; }

        public RootedKeyPath GetRootedKeyPath()
        {
            // if (RootFingerprint is HDFingerprint fp && AccountKeyPath != null)
            //     return new RootedKeyPath(fp, AccountKeyPath);
            return null;
        }

        [JsonProperty(PropertyName = "accountKey")]
        public string AccountKey { get; set; }
        // public bool IsFullySetup()
        // {
        //     return AccountKeyPath != null && RootFingerprint is HDFingerprint;
        // }
    }
}
#endif