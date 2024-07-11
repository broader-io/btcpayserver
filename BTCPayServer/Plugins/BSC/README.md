

BlockNumbers must by of type BlockParameter because when a transaction is first created, 
it doesn't have a block assigned, so it's value is BlockParameter.pending.


// mainnet
const web3 = new Web3('https://bsc-dataseed1.binance.org:443');
// testnet
const web3 = new Web3('https://data-seed-prebsc-1-s1.binance.org:8545');
https://rpc.ankr.com/bsc_testnet_chapel/15f6cfe6547482841817ca3f83db4fcb75344d802ec519649cec2ea631590a9f

# Wprosus contract
0x56f86cfa34cf4004736554c2784d59e477589c8c
# Testnet
0xe6F47738F66256b8C230f99852CBfAf96d3C02D4

# WBNB address
0xbb4CdB9CBd36B01bD1cBaEBF2De08d9173bc095c

# PancakeSwap:Nonfungible Position Manager V3
# 
https://bscscan.com/token/0x56f86cfa34cf4004736554c2784d59e477589c8c?a=0x46a15b0b27311cedf172ab29e4f4766fbe7f4364

# Quoter v2 address
https://bscscan.com/address/0xb048bbc1ee6b733fffcfb9e9cef7375518e25997

# Pool: 0x7d77776ba9ca97004956a0805f206845e772271d

```
docker buildx build \
    --build-arg GIT_COMMIT=9f97efa03ee3d5e4bbc4049a3b9e84ce527845e0 \
    --platform linux/amd64 \
    --pull \
    --build-arg CONFIGURATION_NAME=Altcoins-Release \
    -t prosuspay-prod-btcpayserver:latest \
    -f amd64.Dockerfile .
```

```
docker tag prosuspay-prod-btcpayserver:latest odroid-h3:5000/prosuspay-prod-btcpayserver:latest
docker push odroid-h3:5000/prosuspay-prod-btcpayserver:latest
```
