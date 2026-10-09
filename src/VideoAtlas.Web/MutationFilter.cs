using Microsoft.AspNetCore.Mvc.Filters;
using VideoAtlas.Application;

namespace VideoAtlas.Web;

public sealed class MutationFilter(MutationGate gate) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        await gate.Semaphore.WaitAsync(context.HttpContext.RequestAborted);
        try { await next(); }
        finally { gate.Semaphore.Release(); }
    }
}
