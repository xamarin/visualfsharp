#
# This script controls the F# build process. This encompasess everything from build, testing to
# publishing of NuGet packages. The intent is to structure it to allow for a simple flow of logic
# between the following phases:
#
#   - restore
#   - build
#   - sign
#   - pack
#   - test
#   - publish
#
# Each of these phases has a separate command which can be executed independently. For instance
# it's fine to call `build.ps1 -build -testDesktop` followed by repeated calls to
# `.\build.ps1 -testDesktop`.

[CmdletBinding(PositionalBinding=$false)]
param (
    [string][Alias('c')]$configuration = "Debug",
    [string][Alias('v')]$verbosity = "m",
    [string]$msbuildEngine = "vs",

    # Actions
    [switch][Alias('r')]$restore,
    [switch]$noRestore,
    [switch][Alias('b')]$build,
    [switch]$rebuild,
    [switch]$sign,
    [switch]$noSign,
    [switch]$pack,
    [switch]$publish,
    [switch]$launch,
    [switch]$help,

    # Options
    [switch][Alias('proto')]$bootstrap,
    [string]$bootstrapConfiguration = "Proto",
    [string]$bootstrapTfm = "net472",
    [switch][Alias('bl')]$binaryLog,
    [switch]$ci,
    [switch]$official,
    [switch]$procdump,
    [switch]$deployExtensions,
    [switch]$prepareMachine,
    [switch]$useGlobalNuGetCache = $true,
    [switch]$warnAsError = $true,
    [switch][Alias('test')]$testDesktop,
    [switch]$testCoreClr,
    [switch]$testCambridge,
    [switch]$testCompiler,
    [switch]$testDependencyManager,
    [switch]$testFSharpCore,
    [switch]$testFSharpQA,
    [switch]$testScripting,
    [switch]$testVs,
    [switch]$testAll,
    [string]$officialSkipTests = "false",
    [switch]$noVisualStudio,

    [parameter(ValueFromRemainingArguments=$true)][string[]]$properties)

Set-StrictMode -version 2.0
$ErrorActionPreference = "Stop"

function Print-Usage() {
    Write-Host "Common settings:"
    Write-Host "  -configuration <value>    Build configuration: 'Debug' or 'Release' (short: -c)"
    Write-Host "  -verbosity <value>        Msbuild verbosity: q[uiet], m[inimal], n[ormal], d[etailed], and diag[nostic]"
    Write-Host "  -deployExtensions         Deploy built vsixes"
    Write-Host "  -binaryLog                Create MSBuild binary log (short: -bl)"
    Write-Host ""
    Write-Host "Actions:"
    Write-Host "  -restore                  Restore packages (short: -r)"
    Write-Host "  -norestore                Don't restore packages"
    Write-Host "  -build                    Build main solution (short: -b)"
    Write-Host "  -rebuild                  Rebuild main solution"
    Write-Host "  -pack                     Build NuGet packages, VS insertion manifests and installer"
    Write-Host "  -sign                     Sign our binaries"
    Write-Host "  -publish                  Publish build artifacts (e.g. symbols)"
    Write-Host "  -launch                   Launch Visual Studio in developer hive"
    Write-Host "  -help                     Print help and exit"
    Write-Host ""
    Write-Host "Test actions"
    Write-Host "  -testAll                  Run all tests"
    Write-Host "  -testCambridge            Run Cambridge tests"
    Write-Host "  -testCompiler             Run FSharpCompiler unit tests"
    Write-Host "  -testDependencyManager    Run FSharp.DependencyManager.UnitTests"
    Write-Host "  -testDesktop              Run tests against full .NET Framework"
    Write-Host "  -testCoreClr              Run tests against CoreCLR"
    Write-Host "  -testFSharpCore           Run FSharpCore unit tests"
    Write-Host "  -testFSharpQA             Run F# Cambridge tests"
    Write-Host "  -testScripting            Run Scripting tests"
    Write-Host "  -testVs                   Run F# editor unit tests"
    Write-Host "  -officialSkipTests <bool> Set to 'true' to skip running tests"
    Write-Host ""
    Write-Host "Advanced settings:"
    Write-Host "  -ci                       Set when running on CI server"
    Write-Host "  -official                 Set when building an official build"
    Write-Host "  -bootstrap                Build using a bootstrap compiler"
    Write-Host "  -msbuildEngine <value>    Msbuild engine to use to run build ('dotnet', 'vs', or unspecified)."
    Write-Host "  -procdump                 Monitor test runs with procdump"
    Write-Host "  -prepareMachine           Prepare machine for CI run, clean up processes after build"
    Write-Host "  -useGlobalNuGetCache      Use global NuGet cache."
    Write-Host "  -noVisualStudio           Only build fsc and fsi as .NET Core applications. No Visual Studio required. '-configuration', '-verbosity', '-norestore', '-rebuild' are supported."
    Write-Host ""
    Write-Host "Command line arguments starting with '/p:' are passed through to MSBuild."
}

# Process the command line arguments and establish defaults for the values which are not
# specified.
function Process-Arguments() {
    if ($help -or (($properties -ne $null) -and ($properties.Contains("/help") -or $properties.Contains("/?")))) {
       Print-Usage
       exit 0
    }
    $script:nodeReuse = $False;

    if ($testAll) {
        $script:testDesktop = $True
        $script:testCoreClr = $True
        $script:testFSharpQA = $True
        $script:testVs = $True
    }

    if ([System.Boolean]::Parse($script:officialSkipTests)) {
        $script:testAll = $False
        $script:testCambridge = $False
        $script:testCompiler = $False
        $script:testDependencyManager = $False
        $script:testDesktop = $False
        $script:testCoreClr = $False
        $script:testFSharpCore = $False
        $script:testFSharpQA = $False
        $script:testVs = $False
    }

    if ($noRestore) {
        $script:restore = $False;
    }

    if ($noSign) {
        $script:sign = $False;
    }

    foreach ($property in $properties) {
        if (!$property.StartsWith("/p:", "InvariantCultureIgnoreCase")) {
            Write-Host "Invalid argument: $property"
            Print-Usage
            exit 1
        }
    }
}

function Update-Arguments() {
    if ($script:noVisualStudio) {
        $script:bootstrapTfm = "netcoreapp3.0"
        $script:msbuildEngine = "dotnet"
    }

    if ($bootstrapTfm -eq "netcoreapp3.0") {
        if (-Not (Test-Path "$ArtifactsDir\Bootstrap\fsc\fsc.runtimeconfig.json")) {
            $script:bootstrap = $True
        }
    } else {
        if (-Not (Test-Path "$ArtifactsDir\Bootstrap\fsc\fsc.exe") -or (Test-Path "$ArtifactsDir\Bootstrap\fsc\fsc.runtimeconfig.json")) {
            $script:bootstrap = $True
        }
    }
}

function BuildSolution() {
    # VisualFSharp.sln can't be built with dotnet due to WPF, WinForms and VSIX build task dependencies
    $solution = "VisualFSharp.sln"

    Write-Host "$($solution):"

    $bl = if ($binaryLog) { "/bl:" + (Join-Path $LogDir "Build.binlog") } else { "" }
    $projects = Join-Path $RepoRoot $solution
    $officialBuildId = if ($official) { $env:BUILD_BUILDNUMBER } else { "" }
    $toolsetBuildProj = InitializeToolset
    $quietRestore = !$ci
    $testTargetFrameworks = if ($testCoreClr) { "netcoreapp3.0" } else { "" }

    # Do not set the property to true explicitly, since that would override value projects might set.
    $suppressExtensionDeployment = if (!$deployExtensions) { "/p:DeployExtension=false" } else { "" }

    MSBuild $toolsetBuildProj `
        $bl `
        /p:Configuration=$configuration `
        /p:Projects=$projects `
        /p:RepoRoot=$RepoRoot `
        /p:Restore=$restore `
        /p:Build=$build `
        /p:Rebuild=$rebuild `
        /p:Pack=$pack `
        /p:Sign=$sign `
        /p:Publish=$publish `
        /p:ContinuousIntegrationBuild=$ci `
        /p:OfficialBuildId=$officialBuildId `
        /p:QuietRestore=$quietRestore `
        /p:QuietRestoreBinaryLog=$binaryLog `
        /p:TestTargetFrameworks=$testTargetFrameworks `
        /v:$verbosity `
        $suppressExtensionDeployment `
        @properties
}

function TestAndAddToPath([string] $testPath) {
    if (Test-Path $testPath) {
        $env:PATH = "$testPath;$env:PATH"
        Write-Host "Added [$testPath] to the path."
    }
}

function UpdatePath() {
    # add highest framework dir
    $subdir = ""
    foreach ($child in Get-ChildItem "$env:WINDIR\Microsoft.NET\Framework\v4.0.?????") {
        $subdir = $child
    }
    TestAndAddToPath $subdir

    # add windows SDK dir for ildasm.exe
    foreach ($child in Get-ChildItem "${env:ProgramFiles(x86)}\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.?.? Tools") {
        $subdir = $child
    }
    TestAndAddToPath $subdir

    TestAndAddToPath "$ArtifactsDir\bin\fsc\$configuration\net472"
    TestAndAddToPath "$ArtifactsDir\bin\fsiAnyCpu\$configuration\net472"
}

function VerifyAssemblyVersionsAndSymbols() {
    $assemblyVerCheckPath = Join-Path $ArtifactsDir "Bootstrap\AssemblyCheck\AssemblyCheck.dll"

    # Only verify versions on CI or official build
    if ($ci -or $official) {
        $dotnetPath = InitializeDotNetCli
        $dotnetExe = Join-Path $dotnetPath "dotnet.exe"
        Exec-Console $dotnetExe """$assemblyVerCheckPath"" ""$ArtifactsDir"""
    }
}

function TestUsingNUnit([string] $testProject, [string] $targetFramework) {
    $dotnetPath = InitializeDotNetCli
    $dotnetExe = Join-Path $dotnetPath "dotnet.exe"
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($testProject)
    $testLogPath = "$ArtifactsDir\TestResults\$configuration\${projectName}_$targetFramework.xml"
    $testBinLogPath = "$LogDir\${projectName}_$targetFramework.binlog"
    $args = "test $testProject -c $configuration -f $targetFramework -v n --test-adapter-path . --logger ""nunit;LogFilePath=$testLogPath"" /bl:$testBinLogPath"

    if (-not $noVisualStudio -or $norestore) {
        $args += " --no-restore"
    }

    if (-not $noVisualStudio) {
        $args += " --no-build"
    }

    Exec-Console $dotnetExe $args
}

function BuildCompiler() {
    if ($bootstrapTfm -eq "netcoreapp3.0") {
        $dotnetPath = InitializeDotNetCli
        $dotnetExe = Join-Path $dotnetPath "dotnet.exe"
        $fscProject = "$RepoRoot\src\fsharp\fsc\fsc.fsproj"
        $fsiProject = "$RepoRoot\src\fsharp\fsi\fsi.fsproj"
        
        $argNoRestore = if ($norestore) { " --no-restore" } else { "" }
        $argNoIncremental = if ($rebuild) { " --no-incremental" } else { "" }

        $args = "build $fscProject -c $configuration -v $verbosity -f netcoreapp3.0" + $argNoRestore + $argNoIncremental
        Exec-Console $dotnetExe $args

        $args = "build $fsiProject -c $configuration -v $verbosity -f netcoreapp3.0" + $argNoRestore + $argNoIncremental
        Exec-Console $dotnetExe $args
    }
}

function Prepare-TempDir() {
    Copy-Item (Join-Path $RepoRoot "tests\Resources\Directory.Build.props") $TempDir
    Copy-Item (Join-Path $RepoRoot "tests\Resources\Directory.Build.targets") $TempDir
}

function EnablePreviewSdks() {
  if (Test-Path variable:global:_MSBuildExe) {
    return
  }
  $vsInfo = LocateVisualStudio
  if ($vsInfo -eq $null) {
    # Preview SDKs are allowed when no Visual Studio instance is installed
    return
  }

  $vsId = $vsInfo.instanceId
  $vsMajorVersion = $vsInfo.installationVersion.Split('.')[0]

  $instanceDir = Join-Path ${env:USERPROFILE} "AppData\Local\Microsoft\VisualStudio\$vsMajorVersion.0_$vsId"
  Create-Directory $instanceDir
  $sdkFile = Join-Path $instanceDir "sdk.txt"
  'UsePreviews=True' | Set-Content $sdkFile
}

try {
    Process-Arguments

    . (Join-Path $PSScriptRoot "build-utils.ps1")

    Update-Arguments

    Push-Location $RepoRoot

    if ($ci) {
        Prepare-TempDir
        EnablePreviewSdks
    }

    if ($bootstrap) {
        $bootstrapDir = Make-BootstrapBuild
    }

    if ($restore -or $build -or $rebuild -or $pack -or $sign -or $publish) {
        if ($noVisualStudio) {
            BuildCompiler
        } else {
            BuildSolution
        }
    }

    if ($build) {
        VerifyAssemblyVersionsAndSymbols
    }

    $desktopTargetFramework = "net472"
    $coreclrTargetFramework = "netcoreapp3.0"

    if ($testDesktop -and -not $noVisualStudio) {
        TestUsingNUnit -testProject "$RepoRoot\tests\FSharp.Compiler.UnitTests\FSharp.Compiler.UnitTests.fsproj" -targetFramework $desktopTargetFramework
        TestUsingNUnit -testProject "$RepoRoot\tests\FSharp.Compiler.LanguageServer.UnitTests\FSharp.Compiler.LanguageServer.UnitTests.fsproj" -targetFramework $desktopTargetFramework
        TestUsingNUnit -testProject "$RepoRoot\tests\FSharp.Compiler.Private.Scripting.UnitTests\FSharp.Compiler.Private.Scripting.UnitTests.fsproj" -targetFramework $desktopTargetFramework
        TestUsingNUnit -testProject "$RepoRoot\tests\FSharp.DependencyManager.UnitTests\FSharp.DependencyManager.UnitTests.fsproj" -targetFramework $desktopTargetFramework
        TestUsingNUnit -testProject "$RepoRoot\tests\FSharp.Build.UnitTests\FSharp.Build.UnitTests.fsproj" -targetFramework $desktopTargetFramework
        TestUsingNUnit -testProject "$RepoRoot\tests\FSharp.Core.UnitTests\FSharp.Core.UnitTests.fsproj" -targetFramework $desktopTargetFramework
        TestUsingNUnit -testProject "$RepoRoot\tests\fsharp\FSharpSuite.Tests.fsproj" -targetFramework $desktopTargetFramework
    }

    if ($testCoreClr) {
        TestUsingNUnit -testProject "$RepoRoot\tests\FSharp.Compiler.UnitTests\FSharp.Compiler.UnitTests.fsproj" -targetFramework $coreclrTargetFramework
        TestUsingNUnit -testProject "$RepoRoot\tests\FSharp.Compiler.LanguageServer.UnitTests\FSharp.Compiler.LanguageServer.UnitTests.fsproj" -targetFramework $coreclrTargetFramework
        TestUsingNUnit -testProject "$RepoRoot\tests\FSharp.Compiler.Private.Scripting.UnitTests\FSharp.Compiler.Private.Scripting.UnitTests.fsproj" -targetFramework $coreclrTargetFramework
        TestUsingNUnit -testProject "$RepoRoot\tests\FSharp.DependencyManager.UnitTests\FSharp.DependencyManager.UnitTests.fsproj" -targetFramework $coreclrTargetFramework
        TestUsingNUnit -testProject "$RepoRoot\tests\FSharp.Build.UnitTests\FSharp.Build.UnitTests.fsproj" -targetFramework $coreclrTargetFramework
        TestUsingNUnit -testProject "$RepoRoot\tests\FSharp.Core.UnitTests\FSharp.Core.UnitTests.fsproj" -targetFramework $coreclrTargetFramework
        TestUsingNUnit -testProject "$RepoRoot\tests\fsharp\FSharpSuite.Tests.fsproj" -targetFramework $coreclrTargetFramework
    }

    if ($testFSharpQA -and -not $noVisualStudio) {
        Push-Location "$RepoRoot\tests\fsharpqa\source"
        $resultsRoot = "$ArtifactsDir\TestResults\$configuration"
        $resultsLog = "test-net40-fsharpqa-results.log"
        $errorLog = "test-net40-fsharpqa-errors.log"
        $failLog = "test-net40-fsharpqa-errors"
        $perlPackageRoot = "$env:USERPROFILE\.nuget\packages\StrawberryPerl\5.28.0.1";
        $perlExe = "$perlPackageRoot\bin\perl.exe"
        Create-Directory $resultsRoot
        UpdatePath
        $env:HOSTED_COMPILER = 1
        $env:CSC_PIPE = "$env:USERPROFILE\.nuget\packages\Microsoft.Net.Compilers\2.7.0\tools\csc.exe"
        $env:FSCOREDLLPATH = "$ArtifactsDir\bin\fsc\$configuration\net472\FSharp.Core.dll"
        $env:LINK_EXE = "$RepoRoot\tests\fsharpqa\testenv\bin\link\link.exe"
        $env:OSARCH = $env:PROCESSOR_ARCHITECTURE
        $env:PERL5LIB = "$perlPackageRoot\vendor\lib"
        Exec-Console $perlExe """$RepoRoot\tests\fsharpqa\testenv\bin\runall.pl"" -resultsroot ""$resultsRoot"" -results $resultsLog -log $errorLog -fail $failLog -cleanup:no -procs:$env:NUMBER_OF_PROCESSORS"
        Pop-Location
    }

    if ($testFSharpCore) {
        if (-not $noVisualStudio) {
            TestUsingNUnit -testProject "$RepoRoot\tests\FSharp.Core.UnitTests\FSharp.Core.UnitTests.fsproj" -targetFramework $desktopTargetFramework
        }
        TestUsingNUnit -testProject "$RepoRoot\tests\FSharp.Core.UnitTests\FSharp.Core.UnitTests.fsproj" -targetFramework $coreclrTargetFramework
    }

    if ($testCompiler) {
        if (-not $noVisualStudio) {
            TestUsingNUnit -testProject "$RepoRoot\tests\FSharp.Compiler.UnitTests\FSharp.Compiler.UnitTests.fsproj" -targetFramework $desktopTargetFramework
        }
        TestUsingNUnit -testProject "$RepoRoot\tests\FSharp.Compiler.UnitTests\FSharp.Compiler.UnitTests.fsproj" -targetFramework $coreclrTargetFramework
    }

    if ($testDependencyManager) {
        if (-not $noVisualStudio) {
            TestUsingNUnit -testProject "$RepoRoot\tests\FSharp.DependencyManager.UnitTests\FSharp.DependencyManager.UnitTests.fsproj" -targetFramework $desktopTargetFramework
        }
        TestUsingNUnit -testProject "$RepoRoot\tests\FSharp.DependencyManager.UnitTests\FSharp.DependencyManager.UnitTests.fsproj" -targetFramework $coreclrTargetFramework
    }

    if ($testCambridge) {
        if (-not $noVisualStudio) {
            TestUsingNUnit -testProject "$RepoRoot\tests\fsharp\FSharpSuite.Tests.fsproj" -targetFramework $desktopTargetFramework
        }
        TestUsingNUnit -testProject "$RepoRoot\tests\fsharp\FSharpSuite.Tests.fsproj" -targetFramework $coreclrTargetFramework
    }

    if ($testScripting) {
        if (-not $noVisualStudio) {
            TestUsingNUnit -testProject "$RepoRoot\tests\FSharp.Compiler.Private.Scripting.UnitTests\FSharp.Compiler.Private.Scripting.UnitTests.fsproj" -targetFramework $desktopTargetFramework
        }
        TestUsingNUnit -testProject "$RepoRoot\tests\FSharp.Compiler.Private.Scripting.UnitTests\FSharp.Compiler.Private.Scripting.UnitTests.fsproj" -targetFramework $coreclrTargetFramework
    }

    if ($testVs -and -not $noVisualStudio) {
        TestUsingNUnit -testProject "$RepoRoot\vsintegration\tests\GetTypesVS.UnitTests\GetTypesVS.UnitTests.fsproj" -targetFramework $desktopTargetFramework
        TestUsingNUnit -testProject "$RepoRoot\vsintegration\tests\UnitTests\VisualFSharp.UnitTests.fsproj" -targetFramework $desktopTargetFramework
    }

    ExitWithExitCode 0
}
catch {
    Write-Host $_
    Write-Host $_.Exception
    Write-Host $_.ScriptStackTrace
    ExitWithExitCode 1
}
finally {
    Pop-Location
}
