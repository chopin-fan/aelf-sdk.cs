using System.Text;
using System.Text.Json;
using AElf.Client.Core;
using AElf.Client.Core.Options;
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
            Console.WriteLine("TransactionResult="+crossChainReceiveTokenResult.TransactionResult);
            Logger.LogInformation("CrossChainReceiveToken: {Result}", crossChainReceiveTokenResult.TransactionResult);
        }
    }
    
    public async Task CrossChainTransferWithInlineAsync(Address to, string symbol, long amount, string fromClientAlias,
        string toClientAlias)
    {
        var fromChainStatus = await _clientService.GetChainStatusAsync(fromClientAlias);
        var toChainStatus = await _clientService.GetChainStatusAsync(toClientAlias);
        var tokenInfo = await _tokenService.GetTokenInfoAsync(symbol);
        
        var transferResult = await _tokenService.TransferAsync(new TransferInput
        {
            To = Address.FromBase58("2XHsq3pgWt6ep4Vx5RZKiowpr7MqXycAcPj26TQ7sEMD3ZFiha"),
            Symbol = symbol,
            Amount = amount,
            Memo = "test1"
        });
        Logger.LogInformation("TransactionId="+transferResult.TransactionResult.TransactionId);
        var inlineWithTransactionIdCreated =ProtoExtensions.MergeFromIndexed<VirtualTransactionCreated>(transferResult.TransactionResult.Logs
            .Where(e => e.Name.Contains(nameof(VirtualTransactionCreated))).First().Indexed);
        var inlinetx = inlineWithTransactionIdCreated.InlineTransaction;
        var inlineFactor = inlineWithTransactionIdCreated.InlineFactor;
        inlinetx.IsInlineTxWithId = true;
        inlinetx.SetInlineTxId(inlineFactor);
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
                if (chainStatus.LastIrreversibleBlockHeight - transferResult.TransactionResult.BlockNumber > 100)
                    break;
                await Task.Delay(AElfTokenConstants.TenSeconds);
            }

            // Query merkle path to prepare the data validation for cross-chain receive.
            var merklePath = await _clientService.GetMerklePathByTransactionIdAsync(
                // transferResult.TransactionResult.TransactionId.ToHex(),
                inlinetx.GetHash().ToHex(),
                fromClientAlias);
            Console.WriteLine("inlinedata="+JsonSerializer.Serialize(inlinetx));
            
            var protoData = inlinetx.ToByteArray();

            // 序列化自定义属性（例如：以JSON或其他方式）
            var customData = Encoding.UTF8.GetBytes(inlinetx.IsInlineTxWithId.ToString());

            // 合并两个字节数组
            ;
            var crossChainReceiveTokenInput = new CrossChainReceiveTokenInput
            {
                FromChainId = ChainHelper.ConvertBase58ToChainId(fromChainStatus.ChainId),
                MerklePath = merklePath,
                ParentChainHeight = transferResult.TransactionResult.BlockNumber,
                TransferTransactionBytes = inlinetx.ToByteString(),
                InlineFactor = inlineFactor
            };
            Console.WriteLine("crossChainReceiveTokenInput="+JsonSerializer.Serialize(crossChainReceiveTokenInput));

            // Second tx
            var crossChainReceiveTokenResult =
                await _tokenService.CrossChainReceiveTokenAsync(crossChainReceiveTokenInput, toClientAlias);
            Console.WriteLine("TransactionResult="+crossChainReceiveTokenResult.TransactionResult);
            Logger.LogInformation("CrossChainReceiveToken: {Result}", crossChainReceiveTokenResult.TransactionResult);
        }
    }
    
    public async Task CrossChainTransferWithVritualInlineAsync(Address to, string symbol, long amount, string fromClientAlias,
        string toClientAlias)
    {
        var fromChainStatus = await _clientService.GetChainStatusAsync(fromClientAlias);
        var toChainStatus = await _clientService.GetChainStatusAsync(toClientAlias);
        var tokenInfo = await _tokenService.GetTokenInfoAsync(symbol);
        
        var transferResult = await _tokenService.TransferAsync(new TransferInput
        {
            To = Address.FromBase58("2XHsq3pgWt6ep4Vx5RZKiowpr7MqXycAcPj26TQ7sEMD3ZFiha"),
            Symbol = symbol,
            Amount = amount,
            Memo = "test1"
        });
        Logger.LogInformation("TransactionId="+transferResult.TransactionResult.TransactionId);
        var inlineWithTransactionIdCreated =ProtoExtensions.MergeFromIndexed<VirtualTransactionCreated>(transferResult.TransactionResult.Logs.First(e => e.Name.Contains(nameof(VirtualTransactionCreated))).Indexed);
        var inlinetx = inlineWithTransactionIdCreated.InlineTransaction;
        var inlineFactor = inlineWithTransactionIdCreated.InlineFactor;
        inlinetx.IsInlineTxWithId = true;
        inlinetx.SetInlineTxId(inlineFactor);
        
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
                if (chainStatus.LastIrreversibleBlockHeight - transferResult.TransactionResult.BlockNumber > 100)
                    break;
                await Task.Delay(AElfTokenConstants.TenSeconds);
            }

            // Query merkle path to prepare the data validation for cross-chain receive.
            var merklePath = await _clientService.GetMerklePathByTransactionIdAsync(
                // transferResult.TransactionResult.TransactionId.ToHex(),
                inlinetx.GetHash().ToHex(),
                fromClientAlias);
            Console.WriteLine("inlinedata="+JsonSerializer.Serialize(inlinetx));
            
            var protoData = inlinetx.ToByteArray();

            // 序列化自定义属性（例如：以JSON或其他方式）
            var customData = Encoding.UTF8.GetBytes(inlinetx.IsInlineTxWithId.ToString());

            // 合并两个字节数组
            ;
            var crossChainReceiveTokenInput = new CrossChainReceiveTokenInput
            {
                FromChainId = ChainHelper.ConvertBase58ToChainId(fromChainStatus.ChainId),
                MerklePath = merklePath,
                ParentChainHeight = transferResult.TransactionResult.BlockNumber,
                TransferTransactionBytes = inlinetx.ToByteString(),
                InlineFactor = inlineFactor
            };
            Console.WriteLine("crossChainReceiveTokenInput="+JsonSerializer.Serialize(crossChainReceiveTokenInput));

            // Second tx
            var crossChainReceiveTokenResult =
                await _tokenService.CrossChainReceiveTokenAsync(crossChainReceiveTokenInput, toClientAlias);
            Console.WriteLine("TransactionResult="+crossChainReceiveTokenResult.TransactionResult);
            Logger.LogInformation("CrossChainReceiveToken: {Result}", crossChainReceiveTokenResult.TransactionResult);
        }
    }
}