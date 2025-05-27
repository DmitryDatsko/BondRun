using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace BondRun.Models.Contracts.Functions;

[Function("addWinnersBalance")]
public class AddWinnersBalanceFunction : FunctionMessage
{
    [Parameter("tuple[]", "banch",1)]
    public List<User> Banch { get; set; }
}