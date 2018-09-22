using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using roslyn.optprof.json;
using roslyn.optprof.lib;

namespace roslyn.optprof
{
    internal static class Program
    {
        static async Task<int> Main(string[] args)
        {
            var parser = new CommandLineBuilder()
                .UseParseDirective()
                .UseHelp()
                .UseSuggestDirective()
                .UseParseErrorReporting()
                .UseExceptionHandler()
                .AddOption(
                    new[] { "-c", "--configFile" },
                    "The absolute path to the OptProf.json config file.",
                    c => c.WithDefaultValue(() => null).LegalFilePathsOnly().ExistingFilesOnly().ParseArgumentsAs<string>())
                .AddOption(
                    new[] { "-if", "--insertionFolder" },
                    "This is the absolute path to the folder that contains the VSIXes that will be inserted.",
                    i => i.WithDefaultValue(() => null).LegalFilePathsOnly().ParseArgumentsAs<string>())
                .AddOption(
                    new[] { "-o", "--outputFolder" },
                    "The folder to output the results optprof data to.",
                    o => o.WithDefaultValue(() => null).LegalFilePathsOnly().ParseArgumentsAs<string>())
                .AddVersionOption()
                .OnExecute(typeof(Program).GetMethod(nameof(Execute)))
                .Build();

            return await parser.InvokeAsync(args);
        }

        public static async Task<int> Execute(string configFile, string insertionFolder, string outputFolder, IConsole console = null)
        {
            if (string.IsNullOrEmpty(configFile) || string.IsNullOrEmpty(insertionFolder) || string.IsNullOrEmpty(outputFolder))
            {
                return 1;
            }

            var config = ReadConfigFile(configFile);
            foreach (var product in config.Products)
            {
                string productName = product.Name;
                string path = Path.Combine(insertionFolder, productName);
                var jsonManifest = GetJsonManifest(path);
                var fileEntries = Manifest.GetNgenEntriesFromJsonManifest(jsonManifest).ToArray();

                foreach (var test in product.Tests)
                {
                    var folder = Path.Combine(outputFolder, test.Container);
                    var configurations = Path.Combine(folder, "Configurations");
                    foreach (var fullyQualifiedName in test.TestCases)
                    {
                        var folderToWriteJsonEntires = Path.Combine(configurations, fullyQualifiedName);
                        if (!Directory.Exists(folderToWriteJsonEntires))
                        {
                            Directory.CreateDirectory(folderToWriteJsonEntires);
                        }

                        await WriteEntriesAsync(fileEntries, folderToWriteJsonEntires);
                    }
                }
            }

            return 0;
        }

        private static async Task WriteEntriesAsync((string Technology, string RelativeInstallationPath, string InstrumentationArguments)[] fileEntries, string folderToWriteJsonEntires)
        {
            foreach (var entry in fileEntries)
            {
                var filename = Path.GetFileNameWithoutExtension(entry.RelativeInstallationPath) + ".IBC.json";
                filename = Path.Combine(folderToWriteJsonEntires, filename);
                await WriteEntryAsync(filename, entry);
            }
        }

        private static JObject GetJsonManifest(string path)
        {
            using (var vsix = Vsix.Create(path))
            {
                return vsix.ParseJsonManifest("/manifest.json");
            }
        }

        private static OptProfTrainingConfiguration ReadConfigFile(string pathToConfigFile)
        {
            using (var file = File.OpenText(pathToConfigFile))
            {
                var (success, config) = Config.TryReadConfigFile(file);
                if (!success)
                {
                    // handle error case
                    throw new Exception($"Unable to open the config file '{pathToConfigFile}'");
                }

                return config;
            }
        }

        private static async Task WriteEntryAsync(string filename, (string Technology, string RelativeInstallationPath, string InstrumentationArguments) entry)
        {
            using (var writer = new StreamWriter(File.Open(filename, FileMode.Create, FileAccess.Write, FileShare.Read)))
            {
                string jsonString = ToJsonString(entry);
                await writer.WriteLineAsync(jsonString);
            }
        }

        private static string ToJsonString((string Technology, string RelativeInstallationPath, string InstrumentationArguments) entry)
        {
            return new JObject(
                new JProperty("Technology", entry.Technology),
                new JProperty("RelativeInstallationPath", entry.RelativeInstallationPath),
                new JProperty("InstrumentationArguments", entry.InstrumentationArguments)).ToString();
        }
    }
}
