using NLog.Config;
using NLog.Layouts;
using NLog;
using Rubedo.Compiler.ContentBuilders;
using System.Diagnostics;
using NLog.Targets;
using System.Reflection;

namespace Rubedo.Compiler;

class Program
{
    public static NLog.Logger Logger { get; protected set; }

    static void Main(string[] args)
    {

#if DEBUG //NOTE: this is designed to automatically get the correct directories based on what VS spits out.
          //That is, project_root/bin/Debug/net8.0/Content
          //This is only used for making the compiler build itself for testing, so don't worry about that if you're not doing that.
        string targetDirectory = System.AppContext.BaseDirectory + "Content";
        string sourceDirectory = Path.GetFullPath(System.AppContext.BaseDirectory + "..\\..\\..\\Content");
        string textures = "textures";
#else
        if (args == null || args.Length != 3)
            throw new ArgumentException("Incorrect number of arguments! Should be 3 (source directory, target directory, relative path to textures directory)");

        string sourceDirectory = args[0] + "\\Content";
        string targetDirectory = args[1] + "\\Content";
        string textures = args[2];
#endif

        SetupLogger();

        Builder builder = new Builder(sourceDirectory, targetDirectory, textures);
        int endCode = builder.Build();
        
        System.Environment.Exit(endCode);
    }

    /// <summary>
    /// Creates the rules and config for the NLog logger.
    /// </summary>
    private static void SetupLogger()
    {
        LoggingConfiguration config = new NLog.Config.LoggingConfiguration();

        // Nicer log output.
        SimpleLayout layout = new SimpleLayout("[${longdate}] [${level:uppercase=true}] ${literal:text=\\:} ${message:withexception=true}");

        // Targets where to log to: File and Console
        //FileTarget logfile = new NLog.Targets.FileTarget("logfile") { FileName = "gamelog.txt" };
        ConsoleTarget logconsole = new NLog.Targets.ConsoleTarget("logconsole");
        DebuggerTarget logDebugConsole = new NLog.Targets.DebuggerTarget("debugconsole");
        logconsole.Layout = layout;
        logDebugConsole.Layout = layout;

        // Rules for mapping loggers to targets            
        config.AddRule(LogLevel.Debug, LogLevel.Fatal, logconsole);
        config.AddRule(LogLevel.Debug, LogLevel.Fatal, logDebugConsole);

        //config.AddRule(LogLevel.Debug, LogLevel.Fatal, logfile);

        // Apply config           
        NLog.LogManager.Configuration = config;

        //Load the logger.
        Logger = NLog.LogManager.GetCurrentClassLogger();
    }
}