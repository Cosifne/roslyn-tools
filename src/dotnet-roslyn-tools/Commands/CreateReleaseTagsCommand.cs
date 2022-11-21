// Licensed to the.NET Foundation under one or more agreements.
// The.NET Foundation licenses this file to you under the MIT license.
// See the License.txt file in the project root for more information.

using System.CommandLine.Invocation;
using System.CommandLine;
using Microsoft.RoslynTools.VS;

namespace Microsoft.RoslynTools.Commands;

using static CommonOptions;

internal class CreateReleaseTagsCommand
{
    private static readonly CreateReleaseTagsCommandDefaultHandler s_defaultHandler = new();

    private static readonly string[] s_allProductNames = VSBranchInfo.AllProducts.Select(p => p.Name.ToLower()).ToArray();

    internal static readonly Option<string> ProductOption = new Option<string>(new[] { "--product", "-p" }, () => "roslyn", "Which product to get info for (roslyn or razor)").FromAmong(s_allProductNames);

    public static Symbol GetCommand()
    {
        var command = new Command("create-release-tags", "Generates git tags for VS releases in the Roslyn repo.")
        {
            ProductOption,
            VerbosityOption,
            DevDivAzDOTokenOption,
            DncEngAzDOTokenOption
        };
        command.Handler = s_defaultHandler;
        return command;
    }

    private class CreateReleaseTagsCommandDefaultHandler : ICommandHandler
    {
        public async Task<int> InvokeAsync(InvocationContext context)
        {
            var logger = context.SetupLogging();
            var settings = context.ParseResult.LoadSettings(logger);

            var product = context.ParseResult.GetValueForOption(ProductOption)!;

            return await CreateReleaseTags.CreateReleaseTags.CreateReleaseTagsAsync(product, settings, logger);
        }
    }
}

