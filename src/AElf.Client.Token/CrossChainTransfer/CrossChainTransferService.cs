using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AElf.Client.Core;
using AElf.Client.Core.Options;
using AElf.Contracts.MultiToken;
using AElf.Contracts.MultiToken;
using AElf.Types;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace AElf.Client.Token.CrossChainTransfer;

public class CrossChainTransferService : ICrossChainTransferService, ITransientDependency
{
    private readonly ITokenService _tokenService;
    private readonly IAElfClientService _clientService;
    private readonly AElfClientConfigOptions _clientConfigOptions;
    public ILogger<CrossChainTransferService> Logger { get; set; }

    public CrossChainTransferService(ITokenService tokenService, IAElfClientService clientService,
        IOptionsSnapshot<AElfClientConfigOptions> clientConfigOptions)
    {
        _tokenService = tokenService;
        _clientService = clientService;
        _clientConfigOptions = clientConfigOptions.Value;
    }

    public async Task CrossChainTransferAsync(Address to, string symbol, long amount, string fromClientAlias,
        string toClientAlias)
    {
        var fromChainStatus = await _clientService.GetChainStatusAsync(fromClientAlias);
        var toChainStatus = await _clientService.GetChainStatusAsync(toClientAlias);
        var tokenInfo = await _tokenService.GetTokenInfoAsync(symbol);

        // First tx
        var crossChainTransferInput = new CrossChainTransferInput
        {
            To = to,
            Symbol = symbol,
            Amount = amount,
            ToChainId = ChainHelper.ConvertBase58ToChainId(toChainStatus.ChainId),
            IssueChainId = tokenInfo.IssueChainId
        };
        var transferResult = await _tokenService.CrossChainTransferAsync(crossChainTransferInput, fromClientAlias);
        Logger.LogInformation("CrossChainTransfer: {ResultID}", transferResult.TransactionResult.TransactionId);
        if (transferResult.TransactionResult.Status == TransactionResultStatus.Mined)
        {
            while (true)
            {
                // Wait until the cross-chain indexing is done.
                var chainStatus =
                    await _clientService.GetChainStatusAsync(fromClientAlias);
                Logger.LogInformation(
                    "From chain lib height: {LibHeight}, Transfer tx package height: {TransferHeight}",
                    chainStatus.LastIrreversibleBlockHeight, transferResult.TransactionResult.BlockNumber);
                if (chainStatus.LastIrreversibleBlockHeight - transferResult.TransactionResult.BlockNumber > 300)
                    break;
                await Task.Delay(AElfTokenConstants.TenSeconds);
            }

            // Query merkle path to prepare the data validation for cross-chain receive.
            var merklePath = await _clientService.GetMerklePathByTransactionIdAsync(
                transferResult.TransactionResult.TransactionId.ToHex(),
                fromClientAlias);
            var crossChainReceiveTokenInput = new CrossChainReceiveTokenInput
            {
                FromChainId = ChainHelper.ConvertBase58ToChainId(fromChainStatus.ChainId),
                MerklePath = merklePath,
                ParentChainHeight = transferResult.TransactionResult.BlockNumber,
                TransferTransactionBytes = transferResult.Transaction.ToByteString()
            };

            // Second tx
            var crossChainReceiveTokenResult =
                await _tokenService.CrossChainReceiveTokenAsync(crossChainReceiveTokenInput, toClientAlias);
            Console.WriteLine("TransactionResult=" + crossChainReceiveTokenResult.TransactionResult);
            Logger.LogInformation("CrossChainReceiveToken: {Result}", crossChainReceiveTokenResult.TransactionResult);
        }
    }



    public async Task CrossChainTransferWithVritualInlineAsync(Address to, string symbol, long amount,
        string fromClientAlias,
        string toClientAlias)
    {
        var fromChainStatus = await _clientService.GetChainStatusAsync(fromClientAlias);
        var transferResult = await _tokenService.TransferAsync(new TransferInput
        {
            To = Address.FromBase58("2XHsq3pgWt6ep4Vx5RZKiowpr7MqXycAcPj26TQ7sEMD3ZFiha"),
            Symbol = symbol,
            Amount = amount,
            Memo = "test1"
        });

        var inlineTxLogs = transferResult.TransactionResult.Logs
            .Where(p => p.Name.Equals(InlineTransactionCreated.Descriptor.Name))
            .ToList();
        if (!(transferResult.TransactionResult.Status == TransactionResultStatus.Mined && inlineTxLogs.Count > 0))
        {
            return;
        }
        foreach (var virtualCreatedLog in inlineTxLogs)
        {
            var inlineTransactionCreated = ProtoExtensions.MergeFromIndexed<InlineTransactionCreated>(virtualCreatedLog.Indexed);
            var transaction = inlineTransactionCreated.Transaction;
            if (!transaction.MethodName.Contains(".Transfer.CrossChainTransfer."))
            {
                continue;
            }
            
            Logger.LogInformation("CrossChainTransfer: {ResultID}", transferResult.TransactionResult.TransactionId);
            if (transferResult.TransactionResult.Status == TransactionResultStatus.Mined)
            {
                while (true)
                {
                    // Wait until the cross-chain indexing is done.
                    var chainStatus =
                        await _clientService.GetChainStatusAsync(fromClientAlias);
                    Logger.LogInformation(
                        "From chain lib height: {LibHeight}, Transfer tx package height: {TransferHeight}",
                        chainStatus.LastIrreversibleBlockHeight, transferResult.TransactionResult.BlockNumber);
                    if (chainStatus.LastIrreversibleBlockHeight - transferResult.TransactionResult.BlockNumber > 150)
                        break;
                    await Task.Delay(AElfTokenConstants.TenSeconds);
                }

                // Query merkle path to prepare the data validation for cross-chain receive.
                var merklePath = await _clientService.GetMerklePathByTransactionIdAsync(
                    // transferResult.TransactionResult.TransactionId.ToHex(),
                    transaction.GetHash().ToHex(),
                    fromClientAlias);

                var crossChainReceiveTokenInput = new CrossChainReceiveTokenInput
                {
                    FromChainId = ChainHelper.ConvertBase58ToChainId(fromChainStatus.ChainId),
                    MerklePath = merklePath,
                    ParentChainHeight = transferResult.TransactionResult.BlockNumber,
                    TransferTransactionBytes = transaction.ToByteString(),
                };
                Console.WriteLine(
                    "crossChainReceiveTokenInput=" + JsonSerializer.Serialize(crossChainReceiveTokenInput));

                // Second tx
                var crossChainReceiveTokenResult =
                    await _tokenService.CrossChainReceiveTokenAsync(crossChainReceiveTokenInput, toClientAlias);
                Console.WriteLine("TransactionResult=" + crossChainReceiveTokenResult.TransactionResult);
                Logger.LogInformation("CrossChainReceiveToken: {Result}",
                    crossChainReceiveTokenResult.TransactionResult);
            }
        }
    }
}