using Serilog;
using Serilog.Core;

namespace BotLooter;

public static class FlowUtils
{
    public static bool AskForApproval { get; set; }
    public static bool AskForExit { get; set; }
    
    public static void AbortWithError(string error)
    {
        Environment.Exit(0);
    }

    [MessageTemplateFormatMethod("messageTemplate")]
    public static void WaitForApproval(string messageTemplate, params object[] args)
    {
        Log.Logger.Information(messageTemplate, args);

        Console.CursorLeft = 0;
    }

    public static void WaitForExit(string? message = null)
    {
        Environment.Exit(0);
    }
}
