

// mainnet
const web3 = new Web3('https://bsc-dataseed1.binance.org:443');
// testnet
const web3 = new Web3('https://data-seed-prebsc-1-s1.binance.org:8545');

# Wprosus contract
0x56f86cfa34cf4004736554c2784d59e477589c8c

# WBNB address
0xbb4CdB9CBd36B01bD1cBaEBF2De08d9173bc095c

# PancakeSwap:Nonfungible Position Manager V3
# 
https://bscscan.com/token/0x56f86cfa34cf4004736554c2784d59e477589c8c?a=0x46a15b0b27311cedf172ab29e4f4766fbe7f4364

# Quoter v2 address
https://bscscan.com/address/0xb048bbc1ee6b733fffcfb9e9cef7375518e25997

# Pool: 0x7d77776ba9ca97004956a0805f206845e772271d

```docker buildx build \
    --build-arg GIT_COMMIT=9f97efa03ee3d5e4bbc4049a3b9e84ce527845e0 \
    --platform linux/amd64 \
    --pull \
    --build-arg CONFIGURATION_NAME=Altcoins-Release \
    -t 304575033194.dkr.ecr.sa-east-1.amazonaws.com/prod-btcpayserver:latest \
    -f amd64.Dockerfile .```

`docker push 304575033194.dkr.ecr.sa-east-1.amazonaws.com/prod-btcpayserver:latest`
