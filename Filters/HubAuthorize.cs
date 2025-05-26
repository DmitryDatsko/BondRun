using BondRun.Configuration;
using BondRun.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace BondRun.Filters;

public class HubAuthorize(IOptions<EnvVariables> envVariables) : IHubFilter
{
    private readonly EnvVariables _envVariables = envVariables.Value;

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var httpContext = invocationContext.Context.GetHttpContext();

        if (invocationContext.HubMethodName == nameof(GameHub.PlaceBet))
        {
            if (httpContext == null
                || !httpContext.Request.Cookies.ContainsKey(_envVariables.CookieName))
            {
                await invocationContext.Hub.Clients.Caller
                    .SendAsync("BetRejected", "Invalid cookie");
            
                return null;
            }
        }
        
        return await next(invocationContext);
    }
}