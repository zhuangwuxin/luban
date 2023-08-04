﻿using CommandLine;
using Luban.CodeFormat;
using Luban.CodeTarget;
using Luban.CodeTarget.CSharp;
using Luban.DataExporter.Builtin;
using Luban.DataLoader;
using Luban.DataLoader.Builtin;
using Luban.DataTarget;
using Luban.OutputSaver;
using Luban.Plugin;
using Luban.PostProcess;
using Luban.Schema;
using Luban.Schema.Builtin;
using Luban.Tmpl;
using Luban.Utils;
using NLog;
using System.Reflection;
using System.Text;

namespace Luban;

internal static class Program
{

    private class CommandOptions
    {
        [Option('t', "target", Required = true, HelpText = "target name")]
        public string Target { get; set; }
        
        [Option('c', "codeTarget", Required = false, HelpText = "code target name")]
        public IEnumerable<string> CodeTargets { get; set; }
        
        [Option('d', "dataTarget", Required = false, HelpText = "data target name")]
        public IEnumerable<string> DataTargets { get; set; }
        
        [Option('e', "excludeTag", Required = false, HelpText = "exclude tag")]
        public IEnumerable<string> ExcludeTags { get; set; }
        
        [Option('s', "schemaCollector", Required = false, HelpText = "schema collector name")]
        public string SchemaCollector { get; set; } = "default";
        
        [Option("schemaPath", Required = true, HelpText = "schema path")]
        public string SchemaPath { get; set; }
        
        [Option("tables", Required = false, HelpText = "tables")]
        public IEnumerable<string> Tables { get; set; }
        
        [Option("includedTables", Required = false, HelpText = "included tables")]
        public IEnumerable<string> IncludedTables { get; set; }
        
        [Option("excludedTables", Required = false, HelpText = "excluded tables")]
        public IEnumerable<string> ExcludedTables { get; set; }
        
        [Option('x', "xargs", Required = false, HelpText = "args like -x a=1 -x b=2")]
        public IEnumerable<string> Xargs { get; set; }
        
        [Option('v', "verbose", Required = false, HelpText = "verbose")]
        public bool Verbose { get; set; }
    }
    
    private static ILogger s_logger;

    private static CommandOptions s_commandOptions;

    private static void Main(string[] args)
    {
        GenerationContext.CurrentArguments = ParseArgs(args);
        SetupApp();
        InitManagers();
        ScanRegisterBuiltinAssemblies();
        ScanRegisterPlugins();
        LaunchPipeline();
        s_logger.Info("bye~");
    }

    private static GenerationArguments ParseArgs(string[] args)
    {
        var helpWriter = new StringWriter();
        var parser = new Parser(settings =>
        {
            settings.AllowMultiInstance = true;
            settings.HelpWriter = helpWriter;
        });

        var result = parser.ParseArguments<CommandOptions>(args);
        if (result.Tag == ParserResultType.NotParsed)
        {
            Console.Error.WriteLine(helpWriter.ToString());
            Environment.Exit(1);
        }
        var opts = ((Parsed<CommandOptions>)result).Value;
        s_commandOptions = opts;

        return new GenerationArguments()
        {
            Target = opts.Target,
            CodeTargets = opts.CodeTargets?.ToList() ?? new List<string>(),
            DataTargets = opts.DataTargets?.ToList() ?? new List<string>(),
            ExcludeTags = opts.ExcludeTags?.ToList() ?? new List<string>(),
            SchemaCollector = opts.SchemaCollector,
            SchemaPath = opts.SchemaPath,
            Tables = opts.Tables?.ToList() ?? new List<string>(),
            IncludedTables = opts.IncludedTables?.ToList() ?? new List<string>(),
            GeneralArgs = opts.Xargs.Select(s =>
            {
                string[] pair = s.Split('=', 2);
                return (pair[0], pair[1]);
            }).ToDictionary(p => p.Item1, p => p.Item2),
        };
    }

    private static void SetupApp()
    {
        ConsoleUtil.EnableQuickEditMode(false);
        Console.OutputEncoding = Encoding.UTF8;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        int processorCount = Environment.ProcessorCount;
        ThreadPool.SetMinThreads(Math.Max(4, processorCount), 0);
        ThreadPool.SetMaxThreads(Math.Max(16, processorCount * 2), 2);

        InitSimpleNLogConfigure(s_commandOptions.Verbose ? LogLevel.Trace : LogLevel.Info);
        s_logger = LogManager.GetCurrentClassLogger();
        PrintCopyRight();
    }

    private static void PrintCopyRight()
    {
        s_logger.Info("// =============================================================================================");
        s_logger.Info("// ");
        s_logger.Info("//    Luban is developed by Code Philosophy Technology Co., LTD. (https://code-philosophy.com/)");
        s_logger.Info("// ");
        s_logger.Info("// =============================================================================================");
    }

    private static void InitManagers()
    {
        TemplateManager.Ins.Init();
        CodeFormatManager.Ins.Init();
        CodeTargetManager.Ins.Init();
        PostProcessManager.Ins.Init();
        OutputSaverManager.Ins.Init();
        DataLoaderManager.Ins.Init();
        DataTargetManager.Ins.Init();
        PluginManager.Ins.Init();
    }

    private static void ScanRegisterBuiltinAssemblies()
    {
        var builtinAssemblies = new List<Assembly>()
        {
            typeof(CsharpBinCodeTarget).Assembly,
            typeof(DefaultSchemaCollector).Assembly,
            typeof(FieldNames).Assembly,
            typeof(DefaultDataExporter).Assembly,
        };

        foreach (var assembly in builtinAssemblies)
        {
            ScanRegisterAssembly(assembly);
        }
    }

    private static void ScanRegisterAssembly(Assembly assembly)
    {
        SchemaCollectorManager.Ins.ScanRegisterCollectorCreator(assembly);
        SchemaLoaderManager.Ins.ScanRegisterSchemaLoaderCreator(assembly);
        CodeFormatManager.Ins.ScanRegisterFormatters(assembly);
        CodeFormatManager.Ins.ScanRegisterCodeStyle(assembly);
        CodeTargetManager.Ins.ScanResisterCodeTarget(assembly);
        PostProcessManager.Ins.ScanRegisterPostProcess(assembly);
        OutputSaverManager.Ins.ScanRegisterOutputSaver(assembly);
        DataLoaderManager.Ins.ScanRegisterDataLoader(assembly);
        DataTargetManager.Ins.ScanRegisterDataExporter(assembly);
        DataTargetManager.Ins.ScanRegisterTableExporter(assembly);
    }

    private static void ScanRegisterPlugins()
    {
        foreach (var plugin in PluginManager.Ins.Plugins)
        {
            TemplateManager.Ins.AddTemplateSearchPath($"{plugin.Location}/Templates", false);
            ScanRegisterAssembly(plugin.GetType().Assembly);
        }
    }
    
    private static void LaunchPipeline()
    {
        var pipeline = new Pipeline();
        pipeline.Run();
    }
    
    private static void InitSimpleNLogConfigure(NLog.LogLevel minConsoleLogLevel)
    {
        var logConfig = new NLog.Config.LoggingConfiguration();
        NLog.Layouts.Layout layout;
        if (minConsoleLogLevel <= NLog.LogLevel.Debug)
        {
            layout = NLog.Layouts.Layout.FromString("${longdate}|${level:uppercase=true}|${callsite}:${callsite-linenumber}|${message}${onexception:${newline}${exception:format=tostring}${exception:format=StackTrace}}");
        }
        else
        {
            layout = NLog.Layouts.Layout.FromString("${longdate}|${message}${onexception:${newline}${exception:format=tostring}${exception:format=StackTrace}}");
        }
        logConfig.AddTarget("console", new NLog.Targets.ColoredConsoleTarget() { Layout = layout });
        logConfig.AddRule(minConsoleLogLevel, NLog.LogLevel.Fatal, "console");
        NLog.LogManager.Configuration = logConfig;
    }
}