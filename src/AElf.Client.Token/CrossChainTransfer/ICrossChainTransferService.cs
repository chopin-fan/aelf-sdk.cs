using System.Threading.Tasks;
using AElf.Types;

namespace AElf.Client.Token.CrossChainTransfer;

public interface ICrossChainTransferService
{
    Task CrossChainTransferAsync(Address to, string symbol, long amount, string fromClientAlias, string toClientAlias);
    Task CrossChainTransferWithVritualInlineAsync(Address to, string symbol, long amount, string fromClientAlias, string toClientAlias);
}