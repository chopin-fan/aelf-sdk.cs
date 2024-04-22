namespace AElf.Client.Core.Options;

public class AElfContractOptions
{
    public string NFTContractAddress { get; set; }
    public string BridgeContractAddress { get; set; }
    public string SRAddress { get; set; }
    public Dictionary<string, string> ContractDirectory { get; set; }
}