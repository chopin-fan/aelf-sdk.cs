using System;
using System.Linq;
using System.Threading.Tasks;
using AElf.Client.Token;
using AElf.Client.Token.SyncTokenInfo;
using AElf.Contracts.Bridge;
using AElf.Contracts.MultiToken;
using AElf.Contracts.NFT;
using AElf.Contracts.Profit;
using AElf.Types;
using Google.Protobuf;
using Shouldly;
using Xunit.Abstractions;
using TransferInput = AElf.Contracts.MultiToken.TransferInput;

namespace AElf.Client.Test.Token;

[Trait("Category", "TokenContractService")]
public sealed class TokenServiceTests : AElfClientAbpContractServiceTestBase
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly ITokenService _tokenService;
    private readonly ISyncTokenInfoQueueService _syncTokenInfoQueueService;

    public TokenServiceTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        _tokenService = GetRequiredService<ITokenService>();
        _syncTokenInfoQueueService = GetRequiredService<ISyncTokenInfoQueueService>();
    }

    [Theory]
    [InlineData("ELF")]
    public async Task GetTokenInfoTest(string symbol)
    {
        var tokenInfo = await _tokenService.GetTokenInfoAsync(symbol);
        tokenInfo.Symbol.ShouldBe(symbol);
    }

    [Theory]
    [InlineData("QxQpimticpTqXyQuv7WzP1ppDnvAu5cNKAsYY9iA9yFADYSb2", "ELF", 100000)]
    public async Task TransferTest(string address, string symbol, long amount)
    {
        var result = await _tokenService.TransferAsync(new TransferInput
        {
            To = Address.FromBase58(address),
            Symbol = symbol,
            Amount = amount
        });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
        var logEvent = result.TransactionResult.Logs.First(l => l.Name == nameof(Contracts.MultiToken.Transferred));
        var transferred = new Contracts.MultiToken.Transferred();
        foreach (var indexed in logEvent.Indexed)
        {
            transferred.MergeFrom(indexed);
        }

        transferred.MergeFrom(logEvent.NonIndexed);
        transferred.Symbol.ShouldBe(symbol);
        transferred.To.ToBase58().ShouldBe(address);
        transferred.Amount.ShouldBe(amount);
    }

    [Theory]
    [InlineData("BA994198147")]
    public async Task SyncTokenInfoTest(string symbol)
    {
        _syncTokenInfoQueueService.Enqueue(symbol);
    }

    [Theory]
    [InlineData("bb16f381b0f2e795a988285dec3a68affacdccd7d3ac2e74edc808c102efcd95", 228, "9413000000000000000000")]
    public async Task SwapTokenTest(string swapIdHex, long receiptId, string amount)
    {
        var swapId = Hash.LoadFromHex(swapIdHex);
        await _tokenService.SwapTokenAsync(new SwapTokenInput
        {
            SwapId = swapId,
            OriginAmount = amount,
            ReceiptId = receiptId
        });
    }

    [Fact]
    public void Test()
    {
        var pubkey =
            "04f785788757c15158d39c3fd989336f334b8439592b1e14a7c17dad8bc9fe53b4bff9d39e4cc38ddc0c1f13b154ed435e560e219a375aed42b57c1705e2f04f45";
        var address = Address.FromPublicKey(ByteArrayHelper.HexStringToByteArray(pubkey));
        address.ToBase58().ShouldBeNull();
    }

    [Fact]
    public void Test2()
    {
        var parameterBase64 =
            "CiIKIKpXDFiH8pHPrjiTGEbLSXCs9IVsFGEdgeKeP0KUmDogEiIKIJmyGXgy97JtGOqcDXPbvSvNPxKB2q3qs54sdjf7dfgpGiIKIDAm1SW5NFgZyLu8aihL3boLmnrgIMQoX0c+5qUseBgg";
        var parameter = RemoveBeneficiaryInput.Parser.ParseFrom(ByteString.FromBase64(parameterBase64));
        
    }
    
    [Theory]
    [InlineData("QxQpimticpTqXyQuv7WzP1ppDnvAu5cNKAsYY9iA9yFADYSb2" +
                ",2kDAQLcUvTKRcA9hEWyW9bHF7D7zTbp7tYj2HHXaxRh6HkmANs" +
                ",QxQpimticpTqXyQuv7WzP1ppDnvAu5cNKAsYY9iA9yFADYSb2", "ELF", 100000)]
    public async Task BatchTransferTest(string addressArray, string symbol, long amount)
    {
        foreach (var address in addressArray.Split(","))
        {
            var result = await _tokenService.TransferAsync(new TransferInput
            {
                To = Address.FromBase58(address),
                Symbol = symbol,
                Amount = amount
            });
            result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
            var logEvent = result.TransactionResult.Logs.First(l => l.Name == nameof(Contracts.MultiToken.Transferred));
            var transferred = new Contracts.MultiToken.Transferred();
            foreach (var indexed in logEvent.Indexed)
            {
                transferred.MergeFrom(indexed);
            }

            transferred.MergeFrom(logEvent.NonIndexed);
            transferred.Symbol.ShouldBe(symbol);
            transferred.To.ToBase58().ShouldBe(address);
            transferred.Amount.ShouldBe(amount);
        }
        
    }
    
    [Theory]
    [InlineData("bb16f381b0f2e795a988285dec3a68affacdccd7d3ac2e74edc808c102efcd95", 228, "9413000000000000000000")]
    public async Task queryBalanceTest(string swapIdHex, long receiptId, string amount)
    {
        var tokenBalanceAsync = await _tokenService.GetTokenBalanceAsync("ELF",
            Address.FromBase58("2FxTWccGgAQZ5ffS4UicQrd474CnJcYH8CBRcRYxhfrD4eMQdp"));
        _testOutputHelper.WriteLine("Token balance: "+tokenBalanceAsync.Balance.ToString());
        
        var tokenBalanceAsync1 = await _tokenService.GetTokenBalanceAsync("ELF",
            Address.FromBase58("2XHsq3pgWt6ep4Vx5RZKiowpr7MqXycAcPj26TQ7sEMD3ZFiha"));
        _testOutputHelper.WriteLine("Token balance: "+tokenBalanceAsync1.Balance.ToString());
        
        var tokenBalanceAsync2 = await _tokenService.GetTokenBalanceAsync("ELF",
            Address.FromBase58("QxQpimticpTqXyQuv7WzP1ppDnvAu5cNKAsYY9iA9yFADYSb2"));
        _testOutputHelper.WriteLine("Token balance: "+tokenBalanceAsync2.Balance.ToString());
        
        var tokenBalanceAsync3 = await _tokenService.GetTokenBalanceAsync("ELF",
            Address.FromBase58("2kDAQLcUvTKRcA9hEWyW9bHF7D7zTbp7tYj2HHXaxRh6HkmANs"));
        _testOutputHelper.WriteLine("Token balance: "+tokenBalanceAsync3.Balance.ToString());
    }
}