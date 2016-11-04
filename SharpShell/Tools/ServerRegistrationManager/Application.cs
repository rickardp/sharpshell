using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition.Hosting;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using ServerRegistrationManager.Actions;
using ServerRegistrationManager.OutputService;
using SharpShell;
using SharpShell.Diagnostics;
using SharpShell.ServerRegistration;

namespace ServerRegistrationManager
{
    /// <summary>
    /// The main Server Registration Manager application.
    /// </summary>
    public class Application
    {
        /// <summary>
        /// The output service.
        /// </summary>
        private readonly IOutputService outputService;

        /// <summary>
        /// Initializes a new instance of the <see cref="Application"/> class.
        /// </summary>
        /// <param name="outputService">The output service.</param>
        public Application(IOutputService outputService)
        {
            this.outputService = outputService;
        }

        /// <summary>
        /// Runs the specified application using the specified arguments.
        /// </summary>
        /// <param name="args">The arguments.</param>
        public int Run(string[] args)
        {
            Logging.Log("Started the Server Registration Manager.");

            //  If we have no verb or target or our verb is help, show the help.
            if (args.Length == 0 || args.First().Equals(Verb.Help.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                //  Show the welcome.
                ShowWelcome();
                ShowHelpAction.Execute(outputService);
                return 1;
            }

            //  Get the architecture.
            var registrationType = Environment.Is64BitOperatingSystem ? RegistrationType.OS64Bit : RegistrationType.OS32Bit;

            //  Get the verb, target and parameters.
            Verb verb;
            if (!Enum.TryParse(args[0], true, out verb))
            {
                verb = Verb.Help;
            }

            var parameters = args.Skip(1).ToList();
            // Boolean options
            var hasOptionCodeBase = parameters.RemoveAll(x => x.Equals(ParameterCodebase, StringComparison.CurrentCultureIgnoreCase)) > 0;
            var hasOptionUser = parameters.RemoveAll(x => x.Equals(ParameterUser, StringComparison.CurrentCultureIgnoreCase)) > 0;
            // Parent is an internal option that takes one argument (its parent PID)
            var parent = 0;
            {
                var parentIndex = parameters.IndexOf(ParameterParent);
                if (parentIndex >= 0)
                {
                    int.TryParse(parameters.Skip(parentIndex + 1).FirstOrDefault() ?? "", out parent);
                    parameters.RemoveAt(parentIndex);
                    parameters.RemoveAt(parentIndex);
                    if (parent > 0)
                    {
                        (outputService as ConsoleOutputService)?.SetParent(parent);
                    }
                }
            }
            string outfile = null;
            {
                var outfileIndex = parameters.IndexOf(ParameterOutfile);
                if (outfileIndex >= 0)
                {
                    outfile = parameters.Skip(outfileIndex + 1).FirstOrDefault();
                    parameters.RemoveAt(outfileIndex);
                    parameters.RemoveAt(outfileIndex);
                }
            }
            var target = parameters.FirstOrDefault(x => !x.StartsWith("-", StringComparison.Ordinal));

            bool requiresElevation;
            switch (verb)
            {
                case Verb.EnableEventLog:
                    // Creating event log always requires admin
                    requiresElevation = true;
                    break;
                case Verb.Install:
                case Verb.Uninstall:
                    // Installing system global requires admin, as user not
                    requiresElevation = !hasOptionUser;
                    break;
                case Verb.Config:
                    // Setting system global config requires admin, as user not
                    requiresElevation = !hasOptionUser && parameters.Any();
                    break;
                default:
                    // E.g. help
                    requiresElevation = false;
                    break;
            }
            if (!string.IsNullOrEmpty(outfile))
            {
                // If we just write the changes to a file, we don't need to elevate to admin
                requiresElevation = false;
            }

            if (verb != Verb.Help && parent == 0 && requiresElevation && !IsAdministrator())
            {
                // Needs to have administrative privileges, restart as admin

                try
                {
                    var p = Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName)
                    {
                        WindowStyle = ProcessWindowStyle.Hidden,
                        Arguments = string.Join(" ", Environment.GetCommandLineArgs().Skip(1)) + " " + ParameterParent + " " + Process.GetCurrentProcess().Id,
                        Verb = "runas"
                    });
                    p?.WaitForExit();
                    return p?.ExitCode ?? 255;
                }
                catch (Win32Exception e)
                {
                    outputService.WriteError(e.Message);
                    return 2;
                }
            }

            //  Show the welcome.
            ShowWelcome();

            if (verb == Verb.Install || verb == Verb.Uninstall)
            {

                IRegistryService registry;
                if (!string.IsNullOrEmpty(outfile))
                {
                    registry = new RegFileService(hasOptionUser, outfile);
                }
                else
                {
                    registry = new RegistryService(hasOptionUser, registrationType);
                }
                using (registry as IDisposable)
                {
                    if (verb == Verb.Install)
                        return InstallServer(target, registry, hasOptionCodeBase);
                    else if (verb == Verb.Uninstall)
                        return UninstallServer(target, registry);
                }
            }
            else
            {
                if (verb == Verb.Config)
                    ConfigAction.Execute(outputService, parameters, hasOptionUser);
                else if (verb == Verb.EnableEventLog)
                    EnableEventLogAction.Execute(outputService);
                else
                    ShowHelpAction.Execute(outputService);
            }
            return 0;
        }

        /// <summary>
        /// Installs a SharpShell server at the specified path.
        /// </summary>
        /// <param name="path">The path to the SharpShell server.</param>
        /// <param name="registry">The registry service used to write to the registry.</param>
        /// <param name="codeBase">if set to <c>true</c> install from codebase rather than GAC.</param>
        private int InstallServer(string path, IRegistryService registry, bool codeBase)
        {
            //  Validate the path.
            if (File.Exists(path) == false)
            {
                outputService.WriteError("File '" + path + "' does not exist.", true);
                return 2;
            }

            //  Try and load the server types.
            IEnumerable<ISharpShellServer> serverTypes = null;
            try
            {
                serverTypes = LoadServerTypes(path);
            }
            catch (Exception e)
            {
                outputService.WriteError("An unhandled exception occured when loading the SharpShell");
                outputService.WriteError("Server Types from the specified assembly. Is it a SharpShell");
                outputService.WriteError("Server Assembly?");
                System.Diagnostics.Trace.Write(e);
                Logging.Error("An unhandled exception occured when loading a SharpShell server.", e);
                return 2;
            }

            //  Install each server type.
            foreach (var serverType in serverTypes)
            {
                //  Inform the user we're going to install the server.
                outputService.WriteMessage("Preparing to install (" + registry + "): " + serverType.DisplayName, true);

                //  Install the server.
                try
                {
                    SharpShell.ServerRegistration.ServerRegistrationManager.InstallServer(serverType, registry, codeBase);
                    SharpShell.ServerRegistration.ServerRegistrationManager.RegisterServer(serverType, registry);
                }
                catch (Exception e)
                {
                    outputService.WriteError("Failed to install and register the server: " + e.Message);
                    Logging.Error("An unhandled exception occured installing and registering the server " + serverType.DisplayName, e);
                    continue;
                }

                outputService.WriteSuccess("    " + serverType.DisplayName + " installed and registered.", true);
            }
            return 0;
        }

        /// <summary>
        /// Uninstalls a SharpShell server located at 'path'.
        /// </summary>
        /// <param name="path">The path to the SharpShell server.</param>
        /// <param name="registry">The registry service used to write to the registry.</param>
        private int UninstallServer(string path, IRegistryService registry)
        {
            //  Try and load the server types.
            IEnumerable<ISharpShellServer> serverTypes = null;
            try
            {
                serverTypes = LoadServerTypes(path);
            }
            catch (Exception e)
            {
                outputService.WriteError("An unhandled exception occured when loading the SharpShell");
                outputService.WriteError("Server Types from the specified assembly. Is it a SharpShell");
                outputService.WriteError("Server Assembly?");
                System.Diagnostics.Trace.Write(e);
                Logging.Error("An unhandled exception occured when loading a SharpShell server.", e);
                return 2;
            }

            //  Install each server type.
            foreach (var serverType in serverTypes)
            {
                //  Inform the user we're going to install the server.
                Console.WriteLine("Preparing to uninstall (" + registry + "): " + serverType.DisplayName, true);

                //  Install the server.
                try
                {

                    SharpShell.ServerRegistration.ServerRegistrationManager.UnregisterServer(serverType, registry);
                    SharpShell.ServerRegistration.ServerRegistrationManager.UninstallServer(serverType, registry);
                }
                catch (Exception e)
                {
                    outputService.WriteError("Failed to unregister and uninstall the server.");
                    Logging.Error("An unhandled exception occured un registering and uninstalling the server " + serverType.DisplayName, e);
                    continue;
                }

                outputService.WriteSuccess("    " + serverType.DisplayName + " unregistered and uninstalled.", true);
            }
            return 0;
        }

        /// <summary>
        /// Shows the welcome message.
        /// </summary>
        private void ShowWelcome()
        {
            outputService.WriteMessage("");
            outputService.WriteMessage("========================================");
            outputService.WriteMessage("SharpShell - Server Registration Manager");
            outputService.WriteMessage("========================================");
            outputService.WriteMessage("");
        }

        /// <summary>
        /// Loads the server types from an assembly.
        /// </summary>
        /// <param name="assemblyPath">The assembly path.</param>
        /// <returns>The SharpShell Server types from the assembly.</returns>
        private static IEnumerable<ISharpShellServer> LoadServerTypes(string assemblyPath)
        {
            //  Create an assembly catalog for the assembly and a container from it.
            var catalog = new AssemblyCatalog(Path.GetFullPath(assemblyPath));
            var container = new CompositionContainer(catalog);

            //  Get all exports of type ISharpShellServer.
            return container.GetExports<ISharpShellServer>().Select(st => st.Value);
        }

        private static bool IsAdministrator()
        {
            var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private enum Verb
        {
            Help,
            Install,
            Uninstall,
            Config,
            EnableEventLog
        }

        private const string ParameterParent = @"-parent";
        private const string ParameterCodebase = @"-codebase";
        private const string ParameterOutfile = @"-outfile";
        private const string ParameterUser = @"-user";
    }
}
