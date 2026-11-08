using DiscImageStudio.Core;

namespace DiscImageStudio.Gnome;

internal static class Program
{
    private static int Main(string[] arguments)
    {
        // Without arguments, or with project files to open, start the desktop app. Any other
        // argument list is a generation command, exactly as the Windows executable and the
        // portable CLI accept them.
        if (arguments.Length == 0 || arguments.All(IsProjectFile) || arguments[0] is "--gapplication-service")
        {
            return App.Run(arguments);
        }

        if (arguments[0] is "--help" or "-h" or "help")
        {
            Console.WriteLine("Disc Image Studio (GTK)");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  disc-image-studio-gnome [PROJECT.discproj]   Open the desktop app");
            Console.WriteLine("  disc-image-studio-gnome <command> [options]  Run a generation command");
            Console.WriteLine();
            Console.WriteLine("Commands: " + string.Join(", ", ConsoleModuleRunner.Catalog.Modules.SelectMany(module => module.Descriptor.Commands).Select(command => command.Name)));
            return 0;
        }

        try
        {
            return ConsoleModuleRunner.Run(arguments);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
                                              or IOException or NotSupportedException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static bool IsProjectFile(string argument) =>
        argument.EndsWith(ProjectJsonStore.FileExtension, StringComparison.OrdinalIgnoreCase)
        || argument.StartsWith("file://", StringComparison.OrdinalIgnoreCase);
}
