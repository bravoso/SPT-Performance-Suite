using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Http;
using SPTarkov.Server.Core.Models.Common;
using TarkovPerformanceSuite.Server.Diagnostics;

namespace TarkovPerformanceSuite.Server.Patches;

/// <summary>
/// Harmony entry points for measuring HTTP requests and optionally changing SPT's zlib compression level.
/// </summary>
/// <remarks>
/// <para>
/// The response method mirrors the public contract of SPT's <c>SptHttpListener.SendZlibJson</c>; it does not
/// contain Fika code. Using <see cref="CompressionLevel.Fastest"/> trades a larger response for less server CPU.
/// </para>
/// <para>
/// CompoundingPerf independently patches the same SPT method and predates this implementation. Keep the two
/// mods mutually exclusive for this patch: whichever Harmony prefix runs first may suppress the original method.
/// </para>
/// </remarks>
public static class ServerRequestPatches
{
    public static void HandlePrefix(HttpContext context, out ServerRequestState __state)
    {
        __state = new ServerRequestState(Stopwatch.GetTimestamp(), context.Request.Method, context.Request.Path.ToString());
    }

    public static void HandlePostfix(ServerRequestState __state, ref Task __result)
    {
        __result = AwaitAndRecord(__result, __state);
    }

    public static bool SendZlibJsonPrefix(HttpResponse resp, string output, MongoId sessionID, ref Task __result)
    {
        if (!ServerLoadingRuntime.FastCompressionEnabled)
        {
            return true;
        }

        __result = SendFastZlib(resp, output, sessionID);
        return false;
    }

    private static async Task AwaitAndRecord(Task original, ServerRequestState state)
    {
        Exception? error = null;
        try
        {
            await original.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            ServerLoadingRuntime.RecordRequest(state.Method, state.Path, state.Started, error);
        }
    }

    private static async Task SendFastZlib(HttpResponse response, string output, MongoId sessionId)
    {
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "application/json";
        response.Headers.Append("Set-Cookie", "PHPSESSID=" + sessionId);

        await using ZLibStream stream = new(response.Body, CompressionLevel.Fastest, leaveOpen: true);
        byte[] bytes = Encoding.UTF8.GetBytes(output);
        await stream.WriteAsync(bytes).ConfigureAwait(false);
    }
}
