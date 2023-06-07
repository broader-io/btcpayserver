using System;
using System.ComponentModel.DataAnnotations.Schema;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data
{
    public class ProsusPayPaymentsData
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public string Id { get; set; }

        public DateTimeOffset Created { get; set; }
        public string CryptoCode { get; set; }
        
        //Amount payed to the payment gateway vendor
        public decimal CryptoAmount { get; set; }

        // Payment gateway vendor fee rate
        public decimal FeeRate { get; set; }
        
        // Network fee associated to the payment gateway vendor payment.
        public decimal NetworkFee { get; set; }
        
        public string DestinationAddress { get; set; }
        
        public string SourceAddress { get; set; }
        
        //Transaction Id associated to the payout for the payment gateway vendor
        public string TransactionId { get; set; }
        
        //Crypto amount payed by the customer for the associated transaction
        public decimal OriginalPaymentAmount { get; set; }
        
        //Fee payed by the customer for the associated transaction
        public decimal OriginalNetworkPaymentFeeAmount { get; set; }
        
        // Transaction Id for the customer payment
        public string OriginalTransactionId { get; set;  }
        
        public ProsusPayPaymentsStatus Status { get; set; }

        public string InvoiceDataId { get; set; }
        public InvoiceData InvoiceData { get; set; }
        
        public string StoreDataId { get; set; }
        public InvoiceData StoreData { get; set; }
        
        public string OriginalPaymentDataId { get; set; }
        public PaymentData OriginalPaymentData { get; set; }
        
        public string PayoutDataId { get; set; }
        public PayoutData PayoutData { get; set; }

        internal static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<ProsusPayPaymentsData>()
                .HasIndex(o => o.InvoiceDataId);

            builder.Entity<ProsusPayPaymentsData>()
                .HasIndex(o => o.StoreDataId);
            
            builder.Entity<ProsusPayPaymentsData>()
                .HasIndex(o => o.PayoutDataId)
                .IsUnique();
            
            builder.Entity<ProsusPayPaymentsData>()
                .HasIndex(o => o.OriginalPaymentDataId)
                .IsUnique();
        }
    }
    
    public enum ProsusPayPaymentsStatus
    {
        Created,
        InvoiceCompleted,
        Pending,
        Failed,
        Paid
    }
}
