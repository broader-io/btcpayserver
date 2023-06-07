using System;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Plugins.ProsusPay
{
    [DbContext(typeof(ProsusPayDbContext))]
    [Migration("20230101154419_Init")]
    public partial class Init : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProsusPayPayments",
                columns: table => new
                {
                    Id = table.Column<string>(nullable: false),
                    Created = table.Column<DateTimeOffset>(nullable: false),
                    CryptoCode = table.Column<string>(nullable: false),
                    CryptoAmount = table.Column<decimal>(nullable: false),
                    FeeRate = table.Column<decimal>(nullable: false),
                    NetworkFee = table.Column<decimal>(nullable: false),
                    DestinationAddress = table.Column<string>(nullable: false),
                    SourceAddress = table.Column<string>(nullable: false),
                    TransactionId = table.Column<string>(nullable: true),
                    OriginalPaymentAmount = table.Column<decimal>(nullable: false),
                    OriginalNetworkPaymentFeeAmount = table.Column<decimal>(nullable: false),
                    OriginalTransactionId = table.Column<string>(nullable: false),
                    Status = table.Column<ProsusPayPaymentsStatus>(nullable: false),
                    InvoiceDataId = table.Column<string>(nullable: false),
                    StoreDataId = table.Column<string>(nullable: false),
                    OriginalPaymentDataId = table.Column<string>(nullable: false),
                    PayoutDataId = table.Column<string>(nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProsusPayPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProsusPayPayments_InvoiceData_InvoiceDataId",
                        column: x => x.InvoiceDataId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProsusPayPayments_PaymentData_PaymentDataId",
                        column: x => x.OriginalPaymentDataId,
                        principalTable: "Payments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProsusPayPayments_StoreData_StoreDataId",
                        column: x => x.StoreDataId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProsusPayPayments_PayoutData_PayoutDataId",
                        column: x => x.PayoutDataId,
                        principalTable: "Payouts",
                        principalColumn: "Id");
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProsusPayPayments");
        }
    }
}
