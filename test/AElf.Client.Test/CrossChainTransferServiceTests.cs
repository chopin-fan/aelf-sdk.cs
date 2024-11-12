using System.Threading.Tasks;
using AElf.Client.Test;
using AElf.Types;

namespace AElf.Client.Token.CrossChainTransfer;

[Trait("Category", "CrossChainTransferService")]
public sealed class CrossChainTransferServiceTests : AElfClientAbpContractServiceTestBase
{
    private readonly ICrossChainTransferService _crossChainTransferService;

    public CrossChainTransferServiceTests()
    {
        _crossChainTransferService = GetRequiredService<ICrossChainTransferService>();
    }


    [Theory]
    [InlineData("2nSXrp4iM3A1gB5WKXjkwJQwy56jzcw1ESNpVnWywnyjXFixGc", "ELF", 2)]
    public async Task CrossChainTransferAsync(string address, string symbol, long amount)
    {
        await _crossChainTransferService.CrossChainTransferAsync(
            Address.FromBase58("2FxTWccGgAQZ5ffS4UicQrd474CnJcYH8CBRcRYxhfrD4eMQdp"),
            symbol,
            amount,
            "MainNetMainChain", "TestNetSideChain2"
        );
    }

    
    [Theory]
    [InlineData("2nSXrp4iM3A1gB5WKXjkwJQwy56jzcw1ESNpVnWywnyjXFixGc", "ELF", 3)]
    public async Task CrossChainTransferWithVirtualInlineAsync(string address, string symbol, long amount)
    {
        await _crossChainTransferService.CrossChainTransferWithVritualInlineAsync(
            Address.FromBase58("2FxTWccGgAQZ5ffS4UicQrd474CnJcYH8CBRcRYxhfrD4eMQdp"),
            symbol,
            amount,
            "MainNetMainChain", "TestNetSideChain2"
        );
    }
}