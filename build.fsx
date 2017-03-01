(* -- Fake Dependencies paket-inline
source https://nuget.org/api/v2
source .fake/current-packages

nuget System.AppContext prerelease
nuget Fake.Core.Targets prerelease
nuget Fake.Core.Globbing prerelease
nuget Fake.Core.SemVer prerelease
nuget Fake.IO.FileSystem prerelease
nuget Fake.IO.Zip prerelease
nuget Fake.Core.ReleaseNotes prerelease
nuget Fake.DotNet.AssemblyInfoFile prerelease
nuget Fake.DotNet.MsBuild prerelease
nuget Fake.DotNet.Cli prerelease
nuget Mono.Cecil 0.10.0-beta4
-- Fake Dependencies -- *)

#if DOTNETCORE
// We need to use this for now as "regular" Fake breaks when its caching logic cannot find "loadDependencies.fsx".
// This is the reason why we need to checkin the "loadDependencies.fsx" file for now...
#cd ".fake"
#cd __SOURCE_FILE__
#load "loadDependencies.fsx"
#cd __SOURCE_DIRECTORY__

open System
open System.IO
open System.Reflection
open Fake.Core
open Fake.Core.BuildServer
open Fake.Core.Environment
open Fake.Core.Trace
open Fake.Core.Targets
open Fake.Core.TargetOperators
open Fake.Core.String
open Fake.Core.SemVer
open Fake.Core.ReleaseNotes
open Fake.Core.Process
open Fake.Core.Globbing
open Fake.Core.Globbing.Operators
open Fake.IO.FileSystem
open Fake.IO.Zip
open Fake.IO.FileSystem.Directory
open Fake.IO.FileSystem.File
open Fake.IO.FileSystem.Operators
open Fake.IO.FileSystem.Shell
open Fake.DotNet.AssemblyInfoFile
open Fake.DotNet.AssemblyInfoFile.AssemblyInfo
open Fake.DotNet.MsBuild
open Fake.DotNet.Cli

#else
#I @"packages/build/FAKE/tools/"
#r @"FakeLib.dll"
#r @"packages/Mono.Cecil/lib/net40/Mono.Cecil.dll"
#I "packages/build/SourceLink.Fake/tools/"
#load "packages/build/SourceLink.Fake/tools/SourceLink.fsx"

open Fake
open Fake.Git
open Fake.FSharpFormatting
open System.IO
open SourceLink
open Fake.ReleaseNotesHelper
open Fake.AssemblyInfoFile
#endif

// properties
let projectName = "FAKE"
let projectSummary = "FAKE - F# Make - Get rid of the noise in your build scripts."
let projectDescription = "FAKE - F# Make - is a build automation tool for .NET. Tasks and dependencies are specified in a DSL which is integrated in F#."
let authors = ["Steffen Forkmann"; "Mauricio Scheffer"; "Colin Bull"]
let gitRaw = environVarOrDefault "gitRaw" "https://raw.github.com/fsharp"

let release = LoadReleaseNotes "RELEASE_NOTES.md"
#if DOTNETCORE
let allFiles = (fun _ -> true)
#endif

let packages =
    ["FAKE.Core",projectDescription
     "FAKE.Gallio",projectDescription + " Extensions for Gallio"
     "FAKE.IIS",projectDescription + " Extensions for IIS"
     "FAKE.FluentMigrator",projectDescription + " Extensions for FluentMigrator"
     "FAKE.SQL",projectDescription + " Extensions for SQL Server"
     "FAKE.Experimental",projectDescription + " Experimental Extensions"
     "FAKE.Deploy.Lib",projectDescription + " Extensions for FAKE Deploy"
     projectName,projectDescription + " This package bundles all extensions."
     "FAKE.Lib",projectDescription + " FAKE helper functions as library"]

let buildDir = "./build"
let testDir = "./test"
let docsDir = "./docs"
let apidocsDir = "./docs/apidocs/"
let nugetDir = "./nuget"
let reportDir = "./report"
let packagesDir = "./packages"
let buildMergedDir = buildDir </> "merged"

let additionalFiles = [
    "License.txt"
    "README.markdown"
    "RELEASE_NOTES.md"
    "./packages/FSharp.Core/lib/net40/FSharp.Core.sigdata"
    "./packages/FSharp.Core/lib/net40/FSharp.Core.optdata"]

// Targets
Target "Clean" (fun _ -> CleanDirs [buildDir; testDir; docsDir; apidocsDir; nugetDir; reportDir])

Target "RenameFSharpCompilerService" (fun _ ->
    // for framework in ["net40"; "net45"] do
    for framework in ["netstandard1.6"] do
      let dir = __SOURCE_DIRECTORY__ </> "packages"</>"netcore"</>"FSharp.Compiler.Service"</>"lib"</>framework
      let targetFile = dir </>"netcore"</>  "FAKE.FSharp.Compiler.Service.dll"
      DeleteFile targetFile

#if DOTNETCORE
      let reader =
          let searchpaths =
              [ dir; __SOURCE_DIRECTORY__ </> "packages/FSharp.Core/lib/net40" ]
          let resolve name =
              let n = AssemblyName(name)
              match searchpaths
                      |> Seq.collect (fun p -> Directory.GetFiles(p, "*.dll"))
                      |> Seq.tryFind (fun f -> f.ToLowerInvariant().Contains(n.Name.ToLowerInvariant())) with
              | Some f -> f
              | None ->
                  failwithf "Could not resolve '%s'" name
          { new Mono.Cecil.IAssemblyResolver with
              member x.Dispose () = ()
              member x.Resolve (name : string) =
                  Mono.Cecil.AssemblyDefinition.ReadAssembly(
                      resolve name,
                      new Mono.Cecil.ReaderParameters(AssemblyResolver = x))
              member x.Resolve (name : string, parms : Mono.Cecil.ReaderParameters) =
                  Mono.Cecil.AssemblyDefinition.ReadAssembly(resolve name, parms)
              member x.Resolve (name : Mono.Cecil.AssemblyNameReference) =
                  x.Resolve(name.FullName)
              member x.Resolve (name : Mono.Cecil.AssemblyNameReference, parms : Mono.Cecil.ReaderParameters) =
                  x.Resolve(name.FullName, parms) }
#else
      let reader = new Mono.Cecil.DefaultAssemblyResolver()
      reader.AddSearchDirectory(dir)
      reader.AddSearchDirectory(__SOURCE_DIRECTORY__ </> "packages/FSharp.Core/lib/net40")
#endif
      let readerParams = new Mono.Cecil.ReaderParameters(AssemblyResolver = reader)
      let asem = Mono.Cecil.AssemblyDefinition.ReadAssembly(dir </>"FSharp.Compiler.Service.dll", readerParams)
      asem.Name <- new Mono.Cecil.AssemblyNameDefinition("FAKE.FSharp.Compiler.Service", new System.Version(1,0,0,0))
      asem.Write(dir</>"FAKE.FSharp.Compiler.Service.dll")
)

Target "SetAssemblyInfo" (fun _ ->
    let common = [
         Attribute.Product "FAKE - F# Make"
         Attribute.Version release.AssemblyVersion
         Attribute.InformationalVersion release.AssemblyVersion
         Attribute.FileVersion release.AssemblyVersion]

    [Attribute.Title "FAKE - F# Make Command line tool"
     Attribute.Guid "fb2b540f-d97a-4660-972f-5eeff8120fba"] @ common
    |> CreateFSharpAssemblyInfo "./src/app/FAKE/AssemblyInfo.fs"

    [Attribute.Title "FAKE - F# Make Deploy tool"
     Attribute.Guid "413E2050-BECC-4FA6-87AA-5A74ACE9B8E1"] @ common
    |> CreateFSharpAssemblyInfo "./src/app/Fake.Deploy/AssemblyInfo.fs"

    [Attribute.Title "FAKE - F# Make Deploy Web"
     Attribute.Guid "27BA7705-3F57-47BE-B607-8A46B27AE876"] @ common
    |> CreateFSharpAssemblyInfo "./src/deploy.web/Fake.Deploy.Web/AssemblyInfo.fs"

    [Attribute.Title "FAKE - F# Make Deploy Lib"
     Attribute.Guid "AA284C42-1396-42CB-BCAC-D27F18D14AC7"] @ common
    |> CreateFSharpAssemblyInfo "./src/app/Fake.Deploy.Lib/AssemblyInfo.fs"

    [Attribute.Title "FAKE - F# Make Lib"
     Attribute.InternalsVisibleTo "Test.FAKECore"
     Attribute.Guid "d6dd5aec-636d-4354-88d6-d66e094dadb5"] @ common
    |> CreateFSharpAssemblyInfo "./src/app/FakeLib/AssemblyInfo.fs"

    [Attribute.Title "FAKE - F# Make SQL Lib"
     Attribute.Guid "A161EAAF-EFDA-4EF2-BD5A-4AD97439F1BE"] @ common
    |> CreateFSharpAssemblyInfo "./src/app/Fake.SQL/AssemblyInfo.fs"

    [Attribute.Title "FAKE - F# Make Experimental Lib"
     Attribute.Guid "5AA28AED-B9D8-4158-A594-32FE5ABC5713"] @ common
    |> CreateFSharpAssemblyInfo "./src/app/Fake.Experimental/AssemblyInfo.fs"

    [Attribute.Title "FAKE - F# Make FluentMigrator Lib"
     Attribute.Guid "E18BDD6F-1AF8-42BB-AEB6-31CD1AC7E56D"] @ common
    |> CreateFSharpAssemblyInfo "./src/app/Fake.FluentMigrator/AssemblyInfo.fs"

)


Target "ConvertProjectJsonTemplates" (fun _ ->
    let commonDotNetCoreVersion = "1.0.0-alpha-10"
    // Set project.json.template -> project.json
    let mappings = [
      "__FSHARP_CORE_VERSION__", "4.0.1.7-alpha"
      "__ARGU_VERSION__", "3.3.0"
      "__FSHARP_COMPILER_SERVICE_PACKAGE__", "FSharp.Compiler.Service"
      "__FSHARP_COMPILER_SERVICE_VERSION__", "6.0.3-alpha1"
      "__MONO_CECIL_VERSION__", "0.10.0-beta1-v2"
      "__PAKET_CORE_VERSION__", "3.19.4"
      "__PAKET_CORE_PACKAGE__", "Paket.Core"
      "__FAKE_CORE_TRACING_VERSION__", commonDotNetCoreVersion
      "__FAKE_CORE_CONTEXT_VERSION__", commonDotNetCoreVersion
      "__FAKE_CORE_GLOBBING_VERSION__", commonDotNetCoreVersion
      "__FAKE_CORE_TARGETS_VERSION__", commonDotNetCoreVersion
      "__FAKE_IO_FILESYSTEM_VERSION__", commonDotNetCoreVersion
      "__FAKE_CORE_PROCESS_VERSION__", commonDotNetCoreVersion
      "__FAKE_CORE_ENVIRONMENT_VERSION__", commonDotNetCoreVersion
      "__FAKE_CORE_STRING_VERSION__", commonDotNetCoreVersion
      "__FAKE_CORE_BUILDSERVER_VERSION__", commonDotNetCoreVersion
      "__FAKE_DOTNET_ASSEMBLYINFOFILE_VERSION__", commonDotNetCoreVersion
      "__FAKE_DOTNET_CLI_VERSION__", commonDotNetCoreVersion
      "__FAKE_DOTNET_MSBUILD_VERSION__", commonDotNetCoreVersion
      "__FAKE_TRACING_NANTXML_VERSION__", commonDotNetCoreVersion
      "__FAKE_NETCORE_EXE_VERSION__", commonDotNetCoreVersion
      "__FAKE_RUNTIME_VERSION__", commonDotNetCoreVersion
      "__FAKE_CORE_RELEASENOTES_VERSION__", commonDotNetCoreVersion
      "__FAKE_CORE_SEMVER_VERSION__", commonDotNetCoreVersion
      "__FAKE_IO_ZIP_VERSION__", commonDotNetCoreVersion
      ]

    !! "src/app/*/project.json.template"
    |> Seq.iter(fun template ->
        let original = template.Replace("project.json.template", "project.json")
        let templateContent = File.ReadAllText template
        mappings
        |> Seq.fold (fun (s:string) (fromMapping, toMapping) -> s.Replace(fromMapping, toMapping)) templateContent
        |> fun c -> File.WriteAllText (original, c)
        let dir = Path.GetDirectoryName template
        let dirName = Path.GetFileName dir
        [Attribute.Product "FAKE - F# Make"
         Attribute.Version commonDotNetCoreVersion
         Attribute.InformationalVersion commonDotNetCoreVersion
         Attribute.FileVersion commonDotNetCoreVersion
         Attribute.Title (sprintf "FAKE - F# %s" dirName)]
        |> CreateFSharpAssemblyInfo (sprintf "%s/AssemblyInfo.fs" dir)
    )
)

Target "BuildSolution" (fun _ ->
    MSBuildWithDefaults "Build" ["./FAKE.sln"; "./FAKE.Deploy.Web.sln"]
    |> Log "AppBuild-Output: "
)

Target "GenerateDocs" (fun _ ->
#if DOTNETCORE
    printfn "No Documentation helpers on dotnetcore jet."
#else
    let source = "./help"
    let template = "./help/literate/templates/template-project.html"
    let templatesDir = "./help/templates/reference/"
    let githubLink = "https://github.com/fsharp/FAKE"
    let projInfo =
      [ "page-description", "FAKE - F# Make"
        "page-author", separated ", " authors
        "project-author", separated ", " authors
        "github-link", githubLink
        "project-github", "http://github.com/fsharp/fake"
        "project-nuget", "https://www.nuget.org/packages/FAKE"
        "root", "http://fsharp.github.io/FAKE"
        "project-name", "FAKE - F# Make" ]

    Copy source ["RELEASE_NOTES.md"]

    CreateDocs source docsDir template projInfo

    let dllFiles =
        !! "./build/**/Fake.*.dll"
          ++ "./build/FakeLib.dll"
          -- "./build/**/Fake.Experimental.dll"
          -- "./build/**/FSharp.Compiler.Service.dll"
          -- "./build/**/netcore/FAKE.FSharp.Compiler.Service.dll"
          -- "./build/**/Fake.IIS.dll"
          -- "./build/**/Fake.Deploy.Lib.dll"

    CreateDocsForDlls apidocsDir templatesDir (projInfo @ ["--libDirs", "./build"]) (githubLink + "/blob/master") dllFiles

    WriteStringToFile false "./docs/.nojekyll" ""

    CopyDir (docsDir @@ "content") "help/content" allFiles
    CopyDir (docsDir @@ "pics") "help/pics" allFiles
#endif
)

Target "CopyLicense" (fun _ ->
    CopyTo buildDir additionalFiles
)

open Fake.Testing.XUnit2

Target "Test" (fun _ ->
#if !DOTNETCORE
    !! (testDir @@ "Test.*.dll")
    |> Seq.filter (fun fileName -> if isMono then fileName.ToLower().Contains "deploy" |> not else true)
    |> MSpec (fun p ->
            {p with
                ToolPath = findToolInSubPath "mspec-x86-clr4.exe" (currentDirectory @@ "tools" @@ "MSpec")
                ExcludeTags = ["HTTP"]
                HtmlOutputDir = reportDir})

    !! (testDir @@ "Test.*.dll")
      ++ (testDir @@ "FsCheck.Fake.dll")
    |>  xUnit2 id
#else
    printfn "We don't currently have MSpec and xunit on dotnetcore."
#endif
)

Target "TestDotnetCore" (fun _ ->
#if !DOTNETCORE
    try
        !! (testDir @@ "*.IntegrationTests.dll")
        |> Fake.Testing.NUnit3.NUnit3 id
    with e ->
        printfn "Failed to run tests (because 'dotnet publish' currently fails): %O" e
#else
    printfn "We don't currently have NUnit3 on dotnetcore."
#endif
)

Target "BootstrapTest" (fun _ ->
    let buildScript = "build.fsx"
    let testScript = "testbuild.fsx"
    // Check if we can build ourself with the new binaries.
    let test clearCache script =
        let clear () =
            // Will make sure the test call actually compiles the script.
            // Note: We cannot just clean .fake here as it might be locked by the currently executing code :)
            if Directory.Exists ".fake" then
                Directory.EnumerateFiles(".fake")
                  |> Seq.filter (fun s -> (Path.GetFileName s).StartsWith script)
                  |> Seq.iter File.Delete
        let executeTarget target =
            if clearCache then clear ()
            ExecProcess (fun info ->
                info.FileName <- "build/FAKE.exe"
                info.WorkingDirectory <- "."
                info.Arguments <- sprintf "%s %s -pd" script target) (System.TimeSpan.FromMinutes 3.0)

        let result = executeTarget "PrintColors"
        if result <> 0 then failwith "Bootstrapping failed"

        let result = executeTarget "FailFast"
        if result = 0 then failwith "Bootstrapping failed"

    // Replace the include line to use the newly build FakeLib, otherwise things will be weird.
    File.ReadAllText buildScript
    |> fun s -> s.Replace("#I @\"packages/build/FAKE/tools/\"", "#I @\"build/\"")
    |> fun text -> File.WriteAllText(testScript, text)

    try
      // Will compile the script.
      test true testScript
      // Will use the compiled/cached version.
      test false testScript
    finally File.Delete(testScript)
)


Target "BootstrapTestDotnetCore" (fun _ ->
    let buildScript = "build.fsx"
    let testScript = "testbuild.fsx"
    // Check if we can build ourself with the new binaries.
    let test clearCache script =
        let clear () =
            // Will make sure the test call actually compiles the script.
            // Note: We cannot just clean .fake here as it might be locked by the currently executing code :)
            if Directory.Exists ".fake/testbuild.fsx/packages" then
              Directory.Delete (".fake/testbuild.fsx/packages", true)
            if File.Exists ".fake/testbuild.fsx/paket.depedencies.sha1" then
              File.Delete ".fake/testbuild.fsx/paket.depedencies.sha1"
            if File.Exists ".fake/testbuild.fsx/paket.lock" then
              File.Delete ".fake/testbuild.fsx/paket.lock"
            // TODO: Clean a potentially cached dll as well.

        let executeTarget target =
            if clearCache then clear ()
            ExecProcess (fun info ->
                info.FileName <- "nuget/dotnetcore/Fake.netcore/current/Fake.netcore" + (if isUnix then "" else ".exe")
                info.WorkingDirectory <- "."
                info.Arguments <- sprintf "run %s --target %s" script target) (System.TimeSpan.FromMinutes 3.0)

        let result = executeTarget "PrintColors"
        if result <> 0 then failwithf "Bootstrapping failed (because of exitcode %d)" result

        let result = executeTarget "FailFast"
        if result = 0 then failwithf "Bootstrapping failed (because of exitcode %d)" result

    // Replace the include line to use the newly build FakeLib, otherwise things will be weird.
    File.ReadAllText buildScript
    |> fun s -> s.Replace("source .fake/bin/core-v1.0-alpha-09/packages", "source nuget/dotnetcore")
    |> fun text -> File.WriteAllText(testScript, text)

    try
      // Will compile the script.
      test true testScript
      // Will use the compiled/cached version.
      test false testScript
    finally File.Delete(testScript)
)

Target "SourceLink" (fun _ ->
#if !DOTNETCORE
    !! "src/app/**/*.fsproj"
    |> Seq.iter (fun f ->
        let proj = VsProj.LoadRelease f
        let url = sprintf "%s/%s/{0}/%%var2%%" gitRaw projectName
        SourceLink.Index proj.CompilesNotLinked proj.OutputFilePdb __SOURCE_DIRECTORY__ url )
    let pdbFakeLib = "./build/FakeLib.pdb"
    CopyFile "./build/FAKE.Deploy" pdbFakeLib
    CopyFile "./build/FAKE.Deploy.Lib" pdbFakeLib
#else
    printfn "We don't currently have VsProj.LoadRelease on dotnetcore."
#endif
)

Target "ILRepack" (fun _ ->
    CreateDir buildMergedDir

    let internalizeIn filename =
        let toPack =
            [filename; "FSharp.Compiler.Service.dll"]
            |> List.map (fun l -> buildDir </> l)
            |> separated " "
        let targetFile = buildMergedDir </> filename

        let result =
            ExecProcess (fun info ->
                info.FileName <- Directory.GetCurrentDirectory() </> "packages" </> "build" </> "ILRepack" </> "tools" </> "ILRepack.exe"
                info.Arguments <- sprintf "/verbose /lib:%s /ver:%s /out:%s %s" buildDir release.AssemblyVersion targetFile toPack) (System.TimeSpan.FromMinutes 5.)

        if result <> 0 then failwithf "Error during ILRepack execution."

        CopyFile (buildDir </> filename) targetFile

    internalizeIn "FAKE.exe"

    !! (buildDir </> "FSharp.Compiler.Service.**")
    |> Seq.iter DeleteFile

    DeleteDir buildMergedDir
)

Target "CreateNuGet" (fun _ ->
    let set64BitCorFlags files =
        files
        |> Seq.iter (fun file ->
            let args =
                { Program = "lib" @@ "corflags.exe"
                  WorkingDirectory = Path.GetDirectoryName file
                  CommandLine = "/32BIT- /32BITPREF- " + quoteIfNeeded file
                  Args = [] }
            printfn "%A" args
            shellExec args |> ignore)

    let x64ify package =
#if !DOTNETCORE
        { package with
            Dependencies = package.Dependencies |> List.map (fun (pkg, ver) -> pkg + ".x64", ver)
            Project = package.Project + ".x64" }
#else
        ()
#endif

    for package,description in packages do
        let nugetDocsDir = nugetDir @@ "docs"
        let nugetToolsDir = nugetDir @@ "tools"
        let nugetLibDir = nugetDir @@ "lib"
        let nugetLib451Dir = nugetLibDir @@ "net451"

        CleanDir nugetDocsDir
        CleanDir nugetToolsDir
        CleanDir nugetLibDir
        DeleteDir nugetLibDir

        DeleteFile "./build/FAKE.Gallio/Gallio.dll"

        let deleteFCS dir =
          //!! (dir </> "FSharp.Compiler.Service.**")
          //|> Seq.iter DeleteFile
          ()

        match package with
        | p when p = projectName ->
            !! (buildDir @@ "**/*.*") |> Copy nugetToolsDir
            CopyDir nugetDocsDir docsDir allFiles
            deleteFCS nugetToolsDir
        | p when p = "FAKE.Core" ->
            !! (buildDir @@ "*.*") |> Copy nugetToolsDir
            CopyDir nugetDocsDir docsDir allFiles
            deleteFCS nugetToolsDir
        | p when p = "FAKE.Lib" ->
            CleanDir nugetLib451Dir
            !! (buildDir @@ "FakeLib.dll") |> Copy nugetLib451Dir
            deleteFCS nugetLib451Dir
        | _ ->
            CopyDir nugetToolsDir (buildDir @@ package) allFiles
            CopyTo nugetToolsDir additionalFiles
        !! (nugetToolsDir @@ "*.srcsv") |> DeleteFiles

        let setParams p =
#if !DOTNETCORE
            {p with
                Authors = authors
                Project = package
                Description = description
                Version = release.NugetVersion
                OutputPath = nugetDir
                Summary = projectSummary
                ReleaseNotes = release.Notes |> toLines
                Dependencies =
                    (if package <> "FAKE.Core" && package <> projectName && package <> "FAKE.Lib" then
                       ["FAKE.Core", RequireExactly (NormalizeVersion release.AssemblyVersion)]
                     else p.Dependencies )
                Publish = false }
#else
            p
#endif

#if !DOTNETCORE
        NuGet setParams "fake.nuspec"
        !! (nugetToolsDir @@ "FAKE.exe") |> set64BitCorFlags
        NuGet (setParams >> x64ify) "fake.nuspec"
#else
        printfn "We don't currently have NuGet on dotnetcore."
#endif
)

#if !DOTNETCORE
#load "src/app/Fake.DotNet.Cli/Dotnet.fs"
open Fake.DotNet.Cli
#endif

Target "InstallDotnetCore" (fun _ ->
//     // DotnetCliInstall Preview2ToolingOptions
     DotnetCliInstall RC4_004771ToolingOptions
)

let root = __SOURCE_DIRECTORY__
let srcDir = root</>"src"
let appDir = srcDir</>"app"


let netCoreProjs =
    !! "src/app/Fake.Core.*/*.fsproj"
    ++ "src/app/Fake.DotNet.*/*.fsproj"
    ++ "src/app/Fake.IO.*/*.fsproj"
    ++ "src/app/Fake.netcore/*.fsproj"
    ++ "src/app/Fake.Runtime/*.fsproj"

Target "DotnetRestore" (fun _ ->

    //dotnet root "--info"
    Dotnet { DotnetOptions.Default with WorkingDirectory = root } "--info"
    
    // Workaround bug where paket integration doesn't generate
    // .nuget\packages\.tools\dotnet-compile-fsc\1.0.0-preview2-020000\netcoreapp1.0\dotnet-compile-fsc.deps.json
    let t = Path.GetFullPath "workaround"
    ensureDirectory t
    Dotnet { DotnetOptions.Default with WorkingDirectory = t } "new console --language f#"
    Dotnet { DotnetOptions.Default with WorkingDirectory = t } "restore"
    Dotnet { DotnetOptions.Default with WorkingDirectory = t } "build"
    Directory.Delete(t, true)
    
    // Copy nupkgs to nuget/dotnetcore
    !! "lib/nupgks/**/*.nupkg"
    |> Seq.iter (fun file ->
        let dir = nugetDir @@ "dotnetcore"
        ensureDirectory dir
        File.Copy(file, dir @@ Path.GetFileName file, true))

    // dotnet restore
    netCoreProjs
    |> Seq.iter(fun proj ->
        let dir = (FileInfo (Path.GetFullPath proj)).Directory.FullName
        //dotnet dir "restore"
        DotnetRestore id proj
    )
)

let runtimes =
  [ "win7-x86"; "win7-x64"; "osx.10.11-x64"; "ubuntu.14.04-x64"; "ubuntu.16.04-x64" ]

Target "DotnetPackage" (fun _ ->
    let nugetDir = System.IO.Path.GetFullPath nugetDir
    
    // dotnet pack
    netCoreProjs
    -- "src/app/Fake.netcore/Fake.netcore.fsproj"
    |> Seq.iter(fun proj ->
        try
            DotnetPack (fun c ->
                { c with
                    Configuration = Release
                    OutputPath = Some (nugetDir @@ "dotnetcore")
                }) proj
        with _ ->
            printfn "pack failed, retrying..."
            DotnetPack (fun c ->
                { c with
                    Configuration = Release
                    OutputPath = Some (nugetDir @@ "dotnetcore")
                }) proj
    )

    let info = DotnetInfo id

    let mutable runtimeWorked = false
    // dotnet publish
    runtimes
    |> List.map Some
    |> (fun rs -> None :: rs)
    |> Seq.iter (fun runtime ->
        !! "src/app/Fake.netcore/Fake.netcore.fsproj"
        |> Seq.iter(fun proj ->
            let projName = Path.GetFileName(Path.GetDirectoryName proj)
            let runtimeName, runtime =
                match runtime with
                | Some r -> r, r
                | None -> "current", info.RID

            DotnetRestore (fun c -> {c with Runtime = Some runtime}) proj
            DotnetPublish (fun c ->
                { c with
                    Runtime = Some runtime
                    Configuration = Release
                    OutputPath = Some (nugetDir @@ "dotnetcore" @@ projName @@ runtimeName)
                }) proj
        )
    )

    // Publish portable as well (see https://docs.microsoft.com/en-us/dotnet/articles/core/app-types)
    let netcoreFsproj = "src/app/Fake.netcore/Fake.netcore.fsproj"
    let oldContent = File.ReadAllText netcoreFsproj
    try
        // File.WriteAllText(netcoreJson, newContent)

        DotnetPublish (fun c ->
            { c with
                Framework = Some "netcoreapp1.0"
                OutputPath = Some (nugetDir @@ "dotnetcore" @@ "Fake.netcore" @@ "portable")
            }) netcoreFsproj
    with e ->
        printfn "failed to publish portable!"
        // File.WriteAllText(netcoreJson, oldContent)
        ()
)

Target "DotnetCoreCreateZipPackages" (fun _ ->
    // build zip packages
    !! "nuget/dotnetcore/*.nupkg"
    -- "nuget/dotnetcore/*.symbols.nupkg"
    |> Zip "nuget/dotnetcore" "nuget/dotnetcore/Fake.netcore/fake-dotnetcore-packages.zip"

    ("portable" :: runtimes)
    |> Seq.iter (fun runtime ->
      try
        let runtimeDir = sprintf "nuget/dotnetcore/Fake.netcore/%s" runtime
        !! (sprintf "%s/**" runtimeDir)
        |> Zip runtimeDir (sprintf "nuget/dotnetcore/Fake.netcore/fake-dotnetcore-%s.zip" runtime)
      with _ ->
        printfn "FIXME: Runtime '%s' failed to zip!" runtime
    )
)

Target "DotnetCorePushNuGet" (fun _ ->
    let nuget_exe = Directory.GetCurrentDirectory() </> "packages" </> "build" </> "NuGet.CommandLine" </> "tools" </> "NuGet.exe"
    let apikey = environVarOrDefault "nugetkey" ""
    let nugetsource = environVarOrDefault "nugetsource" "https://www.nuget.org/api/v2/package"
    let nugetPush nugetpackage =
        if not <| System.String.IsNullOrEmpty apikey then
            ExecProcess (fun info ->
                info.FileName <- nuget_exe
                info.Arguments <- sprintf "push '%s' '%s' -Source '%s'" nugetpackage apikey nugetsource) (System.TimeSpan.FromMinutes 5.)
            |> (fun r -> if r <> 0 then failwithf "failed to push package %s" nugetpackage)

    // dotnet pack
    !! "src/app/*/project.json"
    -- "src/app/Fake.netcore/project.json"
    |> Seq.iter(fun proj ->
        let projName = Path.GetFileName(Path.GetDirectoryName proj)
        !! (sprintf "nuget/dotnetcore/%s.*.nupkg" projName)
        -- (sprintf "nuget/dotnetcore/%s.*.symbols.nupkg" projName)
        |> Seq.iter(fun nugetpackage ->
          nugetPush nugetpackage)
    )
)

Target "BootstrapAndBuildDnc" (fun _ ->
    let buildScript = __SOURCE_FILE__
    let target = "DotnetPackage"


    let noDncBootstrap = environVar "NO_DOTNETCORE_BOOTSTRAP" = "true"
    if noDncBootstrap then
        // Just use "old" fake
        if isLinux then
            ExecProcess (fun info ->
                info.FileName <- "build.sh"
                info.WorkingDirectory <- "."
                info.Arguments <- sprintf "%s" target) (System.TimeSpan.FromMinutes 45.0)
        else
            ExecProcess (fun info ->
                info.FileName <- "build.cmd"
                info.WorkingDirectory <- "."
                info.Arguments <- sprintf "%s" target) (System.TimeSpan.FromMinutes 45.0)
    else
        // Copy packages to correct directory
        let version =
            let startStr= "FAKE_VERSION=${FAKE_VERSION:-\""
            match File.ReadLines("fake.sh")
                  |> Seq.tryFind (fun line -> line.StartsWith(startStr)) with
            | Some line ->
              let strtLen = startStr.Length
              line.Substring(strtLen, line.Length - strtLen - 2)
            | None -> failwith "Could not extract version from fake.sh."
        tracefn "Detected version in fake.sh '%s'" version
        CleanDir ".fake/current-packages/"
        !! (sprintf ".fake/bin/%s/packages/*.nupkg" version)
        |> Copy ".fake/current-packages"

        if isLinux then
            ExecProcess (fun info ->
                info.FileName <- "fake.sh"
                info.WorkingDirectory <- "."
                info.Arguments <- sprintf "--verbose run %s -t %s" buildScript target) (System.TimeSpan.FromMinutes 45.0)
        else
            ExecProcess (fun info ->
                info.FileName <- "fake.cmd"
                info.WorkingDirectory <- "."
                info.Arguments <- sprintf "run %s -t %s" buildScript target) (System.TimeSpan.FromMinutes 45.0)

    |> fun r -> if r <> 0 then failwith "dnc build failed!"

    // refresh packages
    CleanDir ".fake/current-packages/"
    !! "nuget/dotnetcore/*.nupkg"
    |> Copy ".fake/current-packages"
    //CopyDir ".fake/current-packages" "nuget/dotnetcore" (fun p -> not <| p.Contains("Fake.netcore"))
)

Target "PublishNuget" (fun _ ->
#if !DOTNETCORE
    Paket.Push(fun p ->
        { p with
            DegreeOfParallelism = 2
            WorkingDir = nugetDir })
#else
    printfn "We don't currently have Paket on dotnetcore."
#endif
)

Target "ReleaseDocs" (fun _ ->
#if !DOTNETCORE
    CleanDir "gh-pages"
    cloneSingleBranch "" "https://github.com/fsharp/FAKE.git" "gh-pages" "gh-pages"

    fullclean "gh-pages"
    CopyRecursive "docs" "gh-pages" true |> printfn "%A"
    CopyFile "gh-pages" "./Samples/FAKE-Calculator.zip"
    StageAll "gh-pages"
    Commit "gh-pages" (sprintf "Update generated documentation %s" release.NugetVersion)
    Branches.push "gh-pages"
#else
    printfn "We don't currently have Git on dotnetcore."
#endif
)

Target "Release" (fun _ ->
#if !DOTNETCORE
    StageAll ""
    Commit "" (sprintf "Bump version to %s" release.NugetVersion)
    Branches.push ""

    Branches.tag "" release.NugetVersion
    Branches.pushTag "" "origin" release.NugetVersion
#else
    printfn "We don't currently have Git on dotnetcore."
#endif
)
open System
Target "PrintColors" (fun s ->
  let color (color: ConsoleColor) (code : unit -> _) =
      let before = Console.ForegroundColor
      try
        Console.ForegroundColor <- color
        code ()
      finally
        Console.ForegroundColor <- before
  color ConsoleColor.Magenta (fun _ -> printfn "TestMagenta")
)
Target "FailFast" (fun _ -> failwith "fail fast")
Target "EnsureTestsRun" (fun _ ->
#if !DOTNETCORE
  if hasBuildParam "SkipIntegrationTests" || hasBuildParam "SkipTests" then
      let res = getUserInput "Are you really sure to continue without running tests (yes/no)?"
      if res <> "yes" then
          failwith "cannot continue without tests"
#endif
  ()
)
Target "Default" DoNothing
Target "StartDnc" DoNothing

"StartDnc"
    //==> "ConvertProjectJsonTemplates"
    ==> "InstallDotnetCore"
    ==> "DotnetRestore"
    ==> "DotnetPackage"

// Dependencies
"Clean"
    ==> "RenameFSharpCompilerService"
    ==> "SetAssemblyInfo"
    ==> "BuildSolution"
    ==> "BootstrapAndBuildDnc"
    ==> "DotnetCoreCreateZipPackages"
    =?> ("TestDotnetCore", not <| hasBuildParam "SkipIntegrationTests" && not <| hasBuildParam "SkipTests")
    //==> "ILRepack"
    =?> ("Test", not <| hasBuildParam "SkipTests")
    =?> ("BootstrapTest",not <| hasBuildParam "SkipTests")
    =?> ("BootstrapTestDotnetCore",not <| hasBuildParam "SkipTests")
    ==> "Default"
    ==> "EnsureTestsRun"
    ==> "CopyLicense"
    =?> ("GenerateDocs", isLocalBuild && not isLinux)
    =?> ("SourceLink", isLocalBuild && not isLinux)
    =?> ("CreateNuGet", not isLinux)
    =?> ("ReleaseDocs", isLocalBuild && not isLinux)
    ==> "DotnetCorePushNuGet"
    ==> "PublishNuget"
    ==> "Release"

// start build
RunTargetOrDefault "Default"
