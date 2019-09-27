Configuration ?= Release
ConfigurationProperty = /p:Configuration=$(Configuration)

Verbosity ?= normal
VerbosityProperty = /Verbosity:$(Verbosity)

MSBuild = $(shell which msbuild)
RestoreCommand = $(MSBuild) /t:Restore
BuildCommand = $(MSBuild) /t:Build
TestCommand = $(MSBuild) /t:VSTest
ProtoConfiguration = /p:Configuration=Proto

NF45 = /p:TargetFramework=net45
NF472 = /p:TargetFramework=net472
NS16 = /p:TargetFramework=netstandard1.6
NS20 = /p:TargetFramework=netstandard2.0
NCA20 = /p:TargetFramework=netcoreapp2.0
NCA21 = /p:TargetFramework=netcoreapp2.1

PackageRoot ?= $(shell nuget locals global-packages -list | cut -d' ' -f2)
NUnitVersion ?= $(shell ls $(PackageRoot)nunit.runners | sort -r | head -n 1)
NUnitRunner = $(PackageRoot)nunit.consolerunner/$(NUnitVersion)/tools/nunit3-console.exe
NUnitCommand = $(monocmd) $(NUnitRunner)

include $(topsrcdir)mono/config.make

debug: debugvars
	@echo PackageRoot=$(PackageRoot)
	@echo NUnitVersion=$(NUnitVersion)
	@echo NUnitCommand=$(NUnitCommand)

all: proto restore build

proto:
	$(RestoreCommand) $(NF472) src/buildtools/buildtools.proj 
	$(RestoreCommand) $(NF472) src/fsharp/FSharp.Build/FSharp.Build.fsproj 
	$(RestoreCommand) $(NF472) src/fsharp/fsc/fsc.fsproj
	$(BuildCommand) $(NF472) $(ConfigurationProperty) src/buildtools/buildtools.proj 
	$(BuildCommand) $(NF472) $(ProtoConfiguration) src/fsharp/FSharp.Build/FSharp.Build.fsproj
	$(BuildCommand) $(NF472) $(ProtoConfiguration) $(VerbosityProperty) src/fsharp/fsc/fsc.fsproj

restore:
	$(RestoreCommand) src/fsharp/FSharp.Core/FSharp.Core.fsproj
	$(RestoreCommand) src/fsharp/FSharp.Build/FSharp.Build.fsproj
	$(RestoreCommand) src/fsharp/FSharp.Compiler.Private/FSharp.Compiler.Private.fsproj
	$(RestoreCommand) src/fsharp/fsc/fsc.fsproj
	$(RestoreCommand) src/fsharp/FSharp.Compiler.Interactive.Settings/FSharp.Compiler.Interactive.Settings.fsproj
	$(RestoreCommand) src/fsharp/fsi/fsi.fsproj
	$(RestoreCommand) src/fsharp/fsiAnyCpu/fsiAnyCpu.fsproj
	$(RestoreCommand) tests/FSharp.Core.UnitTests/FSharp.Core.UnitTests.fsproj
	$(RestoreCommand) tests/FSharp.Build.UnitTests/FSharp.Build.UnitTests.fsproj

build: proto restore
	$(BuildCommand) $(ConfigurationProperty) $(NF45) src/fsharp/FSharp.Core/FSharp.Core.fsproj
	$(BuildCommand) $(ConfigurationProperty) $(NF472) src/fsharp/FSharp.Build/FSharp.Build.fsproj
	$(BuildCommand) $(ConfigurationProperty) $(NF472) src/fsharp/FSharp.Compiler.Private/FSharp.Compiler.Private.fsproj
	$(BuildCommand) $(ConfigurationProperty) $(NF472) src/fsharp/fsc/fsc.fsproj
	$(BuildCommand) $(ConfigurationProperty) $(NF472) src/fsharp/FSharp.Compiler.Interactive.Settings/FSharp.Compiler.Interactive.Settings.fsproj
	$(BuildCommand) $(ConfigurationProperty) $(NF472) src/fsharp/fsi/fsi.fsproj
	$(BuildCommand) $(ConfigurationProperty) $(NF472) src/fsharp/fsiAnyCpu/fsiAnyCpu.fsproj
	$(BuildCommand) $(ConfigurationProperty) $(NF472) tests/FSharp.Core.UnitTests/FSharp.Core.UnitTests.fsproj
	$(BuildCommand) $(ConfigurationProperty) $(NF472) tests/FSharp.Build.UnitTests/FSharp.Build.UnitTests.fsproj

test:
	$(NUnitCommand) $(builddir)/FSharp.Core.UnitTests/$(Configuration)/net472/FSharp.Core.UnitTests.dll
	$(NUnitCommand) $(builddir)/FSharp.Build.UnitTests/$(Configuration)/net472/FSharp.Build.UnitTests.dll
	# TODO: expand the set of tests that are run on mono here (ie FSharp.Compiler.UnitTests, fsharp/FSharpSuite.Tests.fsproj, fsharpqa/run.fsharpqa.test.fsx)

clean:
	rm -rf $(CURDIR)/artifacts

install:
	-rm -fr $(DESTDIR)$(monodir)/fsharp
	-rm -fr $(DESTDIR)$(monodir)/Microsoft\ F#
	-rm -fr $(DESTDIR)$(monodir)/Microsoft\ SDKs/F#
	-rm -fr $(DESTDIR)$(monodir)/msbuild/Microsoft/VisualStudio/v/FSharp
	-rm -fr $(DESTDIR)$(monodir)/msbuild/Microsoft/VisualStudio/v11.0/FSharp
	-rm -fr $(DESTDIR)$(monodir)/msbuild/Microsoft/VisualStudio/v12.0/FSharp
	-rm -fr $(DESTDIR)$(monodir)/msbuild/Microsoft/VisualStudio/v14.0/FSharp
	-rm -fr $(DESTDIR)$(monodir)/msbuild/Microsoft/VisualStudio/v15.0/FSharp
	$(MAKE) -C mono/FSharp.Core TargetDotnetProfile=net45 install
	$(MAKE) -C mono/FSharp.Build TargetDotnetProfile=net472 install
	$(MAKE) -C mono/FSharp.Compiler.Private TargetDotnetProfile=net472 install
	$(MAKE) -C mono/Fsc TargetDotnetProfile=net472 install
	$(MAKE) -C mono/FSharp.Compiler.Interactive.Settings TargetDotnetProfile=net472 install
	$(MAKE) -C mono/FSharp.Compiler.Server.Shared TargetDotnetProfile=net472 install
	$(MAKE) -C mono/fsi TargetDotnetProfile=net472 install
	$(MAKE) -C mono/fsiAnyCpu TargetDotnetProfile=net472 install
	# $(MAKE) -C mono/FSharp.Core TargetDotnetProfile=net40 FSharpCoreBackVersion=3.0 install
	# $(MAKE) -C mono/FSharp.Core TargetDotnetProfile=net40 FSharpCoreBackVersion=3.1 install
	# $(MAKE) -C mono/FSharp.Core TargetDotnetProfile=net40 FSharpCoreBackVersion=4.0 install
	# $(MAKE) -C mono/FSharp.Core TargetDotnetProfile=net40 FSharpCoreBackVersion=4.1 install
	# $(MAKE) -C mono/FSharp.Core TargetDotnetProfile=portable47 install
	# $(MAKE) -C mono/FSharp.Core TargetDotnetProfile=portable7 install
	# $(MAKE) -C mono/FSharp.Core TargetDotnetProfile=portable78 install
	# $(MAKE) -C mono/FSharp.Core TargetDotnetProfile=portable259 install
	# $(MAKE) -C mono/FSharp.Core TargetDotnetProfile=monoandroid10+monotouch10+xamarinios10 install
	# $(MAKE) -C mono/FSharp.Core TargetDotnetProfile=xamarinmacmobile install
	echo "------------------------------ INSTALLED FILES --------------"
	ls -xlR $(DESTDIR)$(monodir)/fsharp $(DESTDIR)$(monodir)/msbuild $(DESTDIR)$(monodir)/xbuild $(DESTDIR)$(monodir)/Reference\ Assemblies $(DESTDIR)$(monodir)/gac/FSharp* $(DESTDIR)$(monodir)/Microsoft* || true

