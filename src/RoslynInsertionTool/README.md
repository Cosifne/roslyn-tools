# Roslyn Insertion Tool

## Example Usage

Inserting the Roslyn `master` branch into the VS `lab/vsuml` branch:

`rit.exe /in=Roslyn /bn=master /vsbn=lab/vsuml /rbq=Roslyn-Signed /ic=true /id=true /t`

Inserting the Roslyn `dev16` branch into the VS `lab/ml` branch:

`rit.exe /in=Roslyn /bn=dev16 /vsbn=lab/ml /rbq=Roslyn-Signed /ic=true /id=true /t`

Inserting the TestImpact `master` branch into the VS `lab/vsuml` branch: 

`rit.exe /in="Live Unit Testing" /bn=master /vsbn=lab/vsuml /rbq=TestImpact-Signed /ic=false /id=false`

Inserting the Project System `master` branch into the VS `rel/d15rel` branch, including a validation build:

`rit.exe /in="Project System" /bn=master /vsbn=rel/d15rel /rbq=DotNet-Project-System /ic=false /id=false /qv=true`

## Arguments

>NOTE all arguments can be of type `--option value`, `--option=value`, `/option value`, OR `/option=value`.

### Required Arguments

At a bare minimum the following arguments must be provided on the command line.

| Short Name | Full Name | Description |
| --- | --- | --- |
| **/bn**=master | **/branchname**=master | The branch we are inserting *from*. |
| **/vsbn**=lab/vsuml | **visualstudiobranchname**=lab/vsuml | The Visual Studio branch we are inserting *into*. |

### Common Arguments

These are not required as they have default values (in app.config), but you will frequently want to provide your own.

| Short Name | Full Name | Description |
| --- | --- | --- |
| **/in**="Project System" | **/insertionName**="Project System" | The "friendly" name of the components being inserted, e.g., Roslyn, Live Unit Testing, Project System. |
| **/rbq**=Roslyn-Signed | **/roslynbuildqueue**=Roslyn-Signed | The name of the build queue producing signed bits you wish to insert. |
| **/ic**=false | **/insertcorextpackages**=false | |
| **/id**=false | **/insertdevdivsourcefiles**=false | |
| **/t** | **/toolsetupdate** | Updates the Roslyn toolset used in the VS branch. |
| **/qv**=true | **/queuevalidationbuild**=true | Creates a VS validation build of the newly created branch. A comment is added to the PR with a link to the build. RPS and DDRITs are included by default. |
| **/ac**=true | **/setautocomplete**=true | Sets the PR to Auto-Complete once all requirements are met. |

### Optional Arguments

The default values for these (again, from app.config) are almost always what you want.

> NOTE the **password** argument should only be given if you are passing in a **username** that is not stored in Azure KeyVault.  Azure KeyVault is a service that will have all usernames of common administrative accounts such as vslsnap.  It will not have you personal accounts such as *alias@microsoft.com*. 
  
>NOTE while **partitions** or **partition** are not required arguments it is highly encouraged that you do some base level validation that the newly inserted binaries do not cause errors when building Visual Studio.  By `rit` will look for `dirs.proj` files in the directories you specify and attempt to build them locally after the Roslyn binaries have been changed.

| Short Name | Full Name | Description | 
| --- | --- | --- |
| **/u**=vslsnap@microsoft.com | **/username**=vslsnap@microsoft.com | Username to authenticate with VSTS *and* git. |
| **/p**=Your password | **/password**=Your password | The password used to authenticate both VSTS *and* git. If not specified will attempt to load from Azure KeyVault. |
| **/ep**=C:\Workspaces\DevDiv\VS | **/enlistmentpath**=C:\Workspaces\DevDiv\VS | This is the absolute path to the Visual Studio enlistment on the machine that is running `rit.exe`. |
| | **/vstsurl**=https://devdiv.visualstudio.com/DefaultCollection/ | The url to the default collection of the VSTS server. |
| **/tfspn**=DevDiv | **/tfsprojectname**=DevDiv | The project that contains the branch specified in **visualstudiobranchname** |
| **/rdp**=\\\\cpvsbuild\drops\Roslyn | **/roslyndroppath**=\\\\cpvsbuild\drops\Roslyn | Location where the signed binaries are dropped.  Will use this path in combination with  **roslynbuildname** to find signed binaries, unless the path ends with ```Binaries\Debug``` or ```Binaries\Release``` (for local testing purposes only) |
| **/nbm**=dev/vslsnap/insertions/ | **/newbranchname**=dev/vslsnap/insertions/ | The name of the branch we create when staging our insertion. Will have the current date and insertion branch appended to it. If empty a new branch and pull request are not created (for local testing purposes only).  |
| **/sb**=20160127.1  | **/specificbuild**=20160127.1 | Only the latest build is inserted by default, and `rit.exe` will exit if no discovered passing builds are newer than the currently inserted version.  By specifying this setting `rit.exe` will skip this logic and insert the specified build. |
| **/parts**=src\alm\shared;src\CodeSense | **/partitions**=src\alm\shared;src\CodeSense | A set of folders relative to **enlistmentpath** that should successfully build after we have inserted.  List should be separated by `;`. |
| **/part**=src\CodeSense | **/partition**=src\CodeSense | *Can be specified more than once.* A folder relative to **enlistmentpath** that should successfully build after we have inserted. |  
| **/esn**= | **/emailservername**= | Server to use to send status emails. |
| **/mr**=mlinfraswat@microsoft.com | **/mailrecipient**=mlinfraswat@microsoft.com | E-mail address to send status emails. |
| **/vbq**=DD-VS-VAL-VSALL | **/validationbuildqueue**=DD-VS-VAL-VSALL | The name of the build queue to use for validation builds. |
| **/rd**=false | **/runddritsinvalidation**=false | Whether or not to run DDRITs as part of a validation build. |
| **/rr**=false | **/runrpsinvalidation**=false | Whether or not to run RPS tests as part of a validation build. |
| **/iw**=true | **/insertWillowPackages**=true | |
| **/ri**=false | **/retaininsertedbuild**=false | Whether or not the inserted build will be marked for retention. |

## Testing the tool locally

To test locally one can set the /roslyndroppath to a local bin directory and /newbranchname to empty string. The insertion tool then applies necessary changes to the local enlistment without creating a branch and pull request, fetching the source binaries from the specified local bin directory.

```D:\Roslyn\Closed\Tools\Source\RoslynInsertionTool\RoslynInsertionTool.Commandline\bin\Debug\RIT.exe /vsbn=lab/vsuml /bn=Roslyn-Master-Signed-Release /ep=D:\vsuml /rdp=D:\Roslyn\Open\Binaries\Debug /nbm=""```
