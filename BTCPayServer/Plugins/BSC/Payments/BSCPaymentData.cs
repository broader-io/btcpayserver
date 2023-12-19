#if ALTCOINS
using System;
using System.Globalization;
using System.Numerics;
//using System.Text.Json;
using BTCPayServer.Client.Models;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Newtonsoft.Json;
using JsonException = Newtonsoft.Json.JsonException;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;

namespace BTCPayServer.Plugins.BSC
{
    public class BSCPaymentData : CryptoPaymentData
    {
        public BigInteger Value { get; set; }
        public string KeyPath { get; set; }
        
        [JsonConverter(typeof(BlockParameterJsonConverter))]
        public BlockParameter BlockParameter { get; set; }
        public string Address { get; set; }
        public string AddressFrom { get; set; }
        public long ConfirmationCount { get; set; }
        
        //DATA, 32 Bytes - hash of the transaction.
        public string TransactionHash { get; set; }
        
        public string ContractAddress { get; set; }
        
        //integer of the transactions index position in the block. null when its pending.
        public BigInteger TransactionIndex { get; set; }
        
        //The transaction type.
        public BigInteger TransactionType { get; set; }
        
        //hash of the block where this transaction was in. null when its pending.
        public string BlockHash { get; set; }

        [JsonIgnore] public string CryptoCode { get; set; }
        // public long AccountIndex { get; set; }
        // public string XPub { get; set; }

        public BTCPayNetworkBase Network { get; set; }

        public string GetPaymentId()
        {
            return $"{TransactionHash}-{TransactionIndex}";
        }

        public static string GetPaymentId(string cryptoCode, string address, BigInteger amount)
        {
            return $"{cryptoCode}#{address}#{amount}";
        }

        public string[] GetSearchTerms()
        {
            return new[] {Address};
        }

        public decimal GetValue()
        {
            return decimal.Parse(
                Web3.Convert.FromWeiToBigDecimal(Value, Network.Divisibility).ToString(),
                CultureInfo.InvariantCulture);
        }

        public bool PaymentCompleted(PaymentEntity entity)
        {
            return ConfirmationCount >= 25;
        }

        public bool PaymentConfirmed(PaymentEntity entity, SpeedPolicy speedPolicy)
        {
            switch (speedPolicy)
            {
                case SpeedPolicy.HighSpeed:
                    return ConfirmationCount >= 2;
                case SpeedPolicy.MediumSpeed:
                    return ConfirmationCount >= 6;
                case SpeedPolicy.LowMediumSpeed:
                    return ConfirmationCount >= 12;
                case SpeedPolicy.LowSpeed:
                    return ConfirmationCount >= 20;
                default:
                    return false;
            }
        }

        public PaymentType GetPaymentType()
        {
            return BSCPaymentType.Instance;
        }

        public string GetDestination()
        {
            return Address;
        }

        public string GetPaymentProof()
        {
            throw new NotImplementedException();
        }
    }
    
    
}

public class BlockParameterJsonConverter : JsonConverter<BlockParameter>
{
    public override void WriteJson(JsonWriter writer, BlockParameter value, JsonSerializer serializer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("ParameterType");
        writer.WriteValue(value.ParameterType.ToString());
        
        writer.WritePropertyName("BlockNumber");
        if (value.ParameterType == BlockParameter.BlockParameterType.blockNumber)
        {
            writer.WriteValue(value.BlockNumber);
        }
        else
        {
            writer.WriteNull();
        }

        writer.WriteEnd();
    }

    public override BlockParameter? ReadJson(
        JsonReader reader, 
        Type objectType, 
        BlockParameter? existingValue, 
        bool hasExistingValue,
        JsonSerializer serializer)
    {
        var blockParameter = new BlockParameter();
        
        while (reader.Read())
        {
            if (reader.TokenType == JsonToken.EndObject)
            {
                break;
            }
            if (reader.TokenType != JsonToken.PropertyName)
            {
                throw new JsonException("Expecting TokenType as first property name");
            }

            string propertyName = reader.ReadAsString();
            if (propertyName == "ParameterType")
            {
                string parameterType = reader.ReadAsString();
                if (parameterType == "blockNumber")
                {
                    string blockNumber = reader.ReadAsString();
                    if (blockNumber == null)
                    {
                        throw new JsonException("BlockNumber cannot be null when ParameterType=blockNumber");
                    }
                    blockParameter.SetValue(BigInteger.Parse(blockNumber));                    
                }
                else
                {
                    if(!Enum.TryParse(parameterType, out BlockParameter.BlockParameterType value))
                    {
                        throw new JsonException(
                            $"Unable to convert \"{propertyName}\" to Enum \"BlockParameterType\".");
                    }
                    blockParameter.SetValue(value);
                }
            }
        }

        return blockParameter;
    }
}
#endif
