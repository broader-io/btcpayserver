

The ProsusPayPayments table represent fee payments in favor of the payment gateway provider. 
Each time an invoice receives a payment, and entry is added to this table to indicate that a fee 
payment is to be sent to the provider. When the amount to be paid per store, and crypto coin exceeds
a configured minimum, a fee payment is scheduled.

It could happen that an invoice with a confirmed status receives a larger payment than the invoice total, 
or receives more payments in the future. In this cases, we only create fee payouts for the total of
the invoice, and automatically create refunds for the exceeding amounts. This could create an ambiguity 
if there are more than one payment per crypto coin. In that case it would be ideal to create the fee
payouts in the order of lower network fee crypto coins.

There is a complexity around payout fee estimation, we would like to show the vendor how much fee
they'll have to pay for a given product price. But since the customer can use different crypto coins,
which have different network fees, is not possible to provide an accurate estimate.

BTCPay server doesn't allow to process two Payouts against the same destination at the same time, this is due 
to the line 

```var payoutByDestination = payouts.ToDictionary(p => p.Destination);``` 

in the class `BitcoinLikePayoutHandler`. 
For this reason we cannot create two payouts in favor of the payment gateway 
provider at the same time. At the moment, these payouts are using the same destination address. We could
schedule payouts one at a time, but this could create a bottleneck in the long run. Another option is to
generate derived addresses for vendor payouts. Using a single destination is much simpler because we don't
have to use the `reserve next address` logic, and we don't have to scan all the derived addresses to get the correct 
balance. Can we use `NBXplorer` for this?
On the other hand, we will not show the balance using a wallet, but ProsusPayPayments. 

