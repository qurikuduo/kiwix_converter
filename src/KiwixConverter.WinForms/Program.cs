using KiwixConverter.Core.Infrastructure;

namespace KiwixConverter.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var scope = FileTraceLogger.Enter(nameof(Program), nameof(Main), new
        {
            baseDirectory = FileTraceLogger.SummarizePath(AppContext.BaseDirectory),
            executablePath = FileTraceLogger.SummarizePath(Application.ExecutablePath),
            logFile = FileTraceLogger.SummarizePath(FileTraceLogger.CurrentLogFilePath)
        });

        try
        {
            RegisterGlobalExceptionHandlers();

            FileTraceLogger.Info(nameof(Program), "ApplicationConfiguration.Initialize START");
            ApplicationConfiguration.Initialize();
            FileTraceLogger.Info(nameof(Program), "ApplicationConfiguration.Initialize EXIT");

            using var mainForm = new MainForm();
            FileTraceLogger.Info(nameof(Program), "MainForm constructed", new { formType = nameof(MainForm) });
            Application.Run(mainForm);

            scope.Success(new { message = "Application.Run returned" });
        }
        catch (Exception exception)
        {
            scope.Fail(exception);
            throw;
        }
    }

    private static void RegisterGlobalExceptionHandlers()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        FileTraceLogger.Info(nameof(Program), nameof(RegisterGlobalExceptionHandlers), new
        {
            logFile = FileTraceLogger.SummarizePath(FileTraceLogger.CurrentLogFilePath)
        });

        Application.ThreadException += (_, args) =>
            FileTraceLogger.Error(nameof(Program), "Application.ThreadException", args.Exception);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                FileTraceLogger.Error(nameof(Program), "AppDomain.CurrentDomain.UnhandledException", exception, new
                {
                    args.IsTerminating
                });
                return;
            }

            FileTraceLogger.Warning(nameof(Program), "AppDomain.CurrentDomain.UnhandledException", new
            {
                args.IsTerminating,
                exceptionObject = FileTraceLogger.SummarizeText(args.ExceptionObject?.ToString(), 512)
            });
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
            FileTraceLogger.Error(nameof(Program), "TaskScheduler.UnobservedTaskException", args.Exception);
    }
}