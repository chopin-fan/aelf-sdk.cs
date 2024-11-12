using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AElf.Client.Core;
using AElf.Client.Core.Options;
using AElf.Contracts.MultiToken;
using AElf.Contracts.MultiToken;
using AElf.Types;
using Google.Api;
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

        var virtualCreatedLogs = transferResult.TransactionResult.Logs
            .Where(p => p.Name.Equals(CrossChainTransferred.Descriptor.Name))
            .ToList();
        if (!(transferResult.TransactionResult.Status == TransactionResultStatus.Mined && virtualCreatedLogs.Count > 0))
        {
            return;
        }

        var index = 0;
        foreach (var virtualCreatedLog in virtualCreatedLogs)
        {
            virtualCreatedLog.Indexed.Add(virtualCreatedLog.NonIndexed);
            var virtualTransactionCreated = ProtoExtensions.MergeFromIndexed<CrossChainTransferred>(virtualCreatedLog.Indexed);

            if (transferResult.Transaction.MethodName.Equals(nameof(CrossChainTransfer)))
            {
                continue;
            }
            var transaction = WrapTransaction(virtualTransactionCreated,transferResult,index);

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

    private Transaction WrapTransaction(CrossChainTransferred inlineTx, SendTransactionResult transferResult, int index)
    {
        return new Transaction()
        {
            From = inlineTx.From,
            To = Address.FromBase58("JRmBduh4nXWi1aXgdUsj5gJrzeZb2LxmrAbf7W99faZSvoAaE"),
            MethodName = transferResult.Transaction.GetHash().ToHex()+"."+transferResult.Transaction.MethodName+"."+nameof(CrossChainTransfer)+"."+index,
            Params = new CrossChainTransferInput
            {
                Symbol = inlineTx.Symbol,
                To = inlineTx.To,
                Amount = inlineTx.Amount,
                Memo = inlineTx.Memo,
                IssueChainId = inlineTx.IssueChainId,
                ToChainId = inlineTx.ToChainId
            }.ToByteString(),
        };
    }
}