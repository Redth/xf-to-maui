using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Linq;
using Cake.Common;
using Cake.Common.IO;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.FileHelpers;
using Cake.Frosting;
using System;
using OfficeOpenXml;

public static class Consts
{
    public static ICakeContext CakeContext { get;set; }
    public static string BasePath
        => CakeContext.Environment.Platform.Family == PlatformFamily.Windows ?
            "C:/xamarin/Xamarin.Forms/"
            : "/Users/redth/code/Xamarin.Forms/";

    public const string NameMappingsXlsx = "./NameMappings.xlsx";
}

public static class Program
{
    public static int Main(string[] args)
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        return new CakeHost()
            .UseContext<BuildContext>()
            .Run(args);
    }
}

public class BuildContext : FrostingContext
{
    public bool Delay { get; set; }

    public BuildContext(ICakeContext context)
        : base(context)
    {
        Consts.CakeContext = context;
        Delay = context.Arguments.HasArgument("delay");
    }
}

[TaskName("Default")]
public sealed class DefaultTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        // Clean up repo to start fresh
        context.StartProcess("git", new ProcessSettings{ Arguments = $"-C \"{Consts.BasePath}\" reset --hard" });
        context.StartProcess("git", new ProcessSettings{ Arguments = $"-C \"{Consts.BasePath}\" clean -xdf" });

        ReplaceMappings(context, Consts.NameMappingsXlsx, new [] {
            ("Maui.Essentials", Consts.BasePath + "src/Essentials/**/*.{cs,xaml}" ),
            ("Maui.Compatibility", Consts.BasePath + "src/Platform.Renderers/**/*.{cs,xaml}" ),
            ("Maui.Controls", Consts.BasePath + "src/Forms/**/*.{cs,xaml}" ),
            ("Maui.Controls", Consts.BasePath + "src/Controls/**/*.{cs,xaml}" ),
            ("Maui.Core", Consts.BasePath + "src/Platform.Handlers/**/*.{cs,xaml}" ),
        });


        var directoryMapping = ReadExcel(context, Consts.NameMappingsXlsx, "Directories", true)
            .Select(dm => (dm[0], dm[1]));

        var filenameMapping = ReadExcel(context, Consts.NameMappingsXlsx, "Files", true)
            .Select(dm => (dm[0], dm[1]));

        FixRelativeFileReferences(context, directoryMapping, filenameMapping);

        RenameFiles(context, filenameMapping);

        RenameFolders(context, directoryMapping);

        FixSlns(context, filenameMapping, directoryMapping);
        
        DeleteEmptyDirectories(context, Consts.BasePath);

        if (context.FileExists("./.github/CODEOWNERS"))
            context.DeleteFile("./.github/CODEOWNERS");


        FileFixupsHelper(context, Consts.BasePath + "src/**/*.cs",
            text => text.Replace("using FormsElement = Forms.", "using FormsElement = Maui.Controls."),
            text => text.Replace("Forms.Color", " Maui.Color"),
            text => text.Replace("Forms.Size", " Size")
        );

        FileFixupsHelper(context, Consts.BasePath + "src/Controls/src/**/*.cs",
            text => text.Replace("using Microsoft.Maui.Controls.Platform;", "")
                .Replace("using Microsoft.Maui.Controls.Platform.Layouts;", "")
                .Replace("Microsoft.Maui.Controls.Platform.Registrar.Handlers.Register", "Microsoft.Maui.Registrar.Handlers.Register")
                .Replace("Microsoft.Maui.Controls.Platform.ILayout", "Microsoft.Maui.ILayout")
                .Replace("Microsoft.Maui.Controls.Platform.IView", "Microsoft.Maui.IView")
                .Replace("public Platform.ILayoutHandler ", "public ILayoutHandler ")
                .Replace("Forms.MasterDetailPage", "Maui.Controls.MasterDetailPage")
                .Replace("Forms.ViewCell", "Maui.Controls.ViewCell")
                .Replace("Forms.Cell", "Maui.Controls.Cell")
                .Replace("Microsoft.Maui.Controls.Rectangle", "Microsoft.Maui.Rectangle")
                .Replace("Forms.Binding", "Maui.Controls.Binding")
                .Replace("Forms.Style", "Maui.Controls.Style")
                .Replace("Forms.Layout", "Maui.Controls.Layout")
                .Replace("Forms.ImageButton", "Maui.Controls.ImageButton")
                .Replace("using Microsoft.Maui.Controls.Platform.Handlers;", "using Microsoft.Maui.Handlers;")
                .Replace("Forms.VisualMarker", "Maui.Controls.VisualMarker"));

        FileFixupsHelper(context, Consts.BasePath + "src/Core/tests/**/*.cs",
            text => text.Replace("Forms.Button", "Maui.Controls.Button"));

        FileFixupsHelper(context, Consts.BasePath + "src/Controls/src/Xaml/**/*.cs",
            text => text.Replace("Forms.Internals", "Maui.Controls.Internals"));

        FileFixupsHelper(context, Consts.BasePath + "src/Core/tests/DeviceTests/**/*.cs",
            text => text.Replace("using Microsoft.Maui;\r\nusing Microsoft.Maui;", "using Microsoft.Maui;")
            );

        FileFixupsHelper(context, Consts.BasePath + "src/**/*.csproj",
            text => {
                var rnsmap = GetNamespaceMappings(context, Consts.NameMappingsXlsx, "RootNamespace");
                foreach (var item in rnsmap)
                    text = text.Replace($"<RootNamespace>{item.from}</RootNamespace>", $"<RootNamespace>{item.to}</RootNamespace>");
                return text;
            },
            text => {
                var anmap = GetNamespaceMappings(context, Consts.NameMappingsXlsx, "AssemblyName");
                foreach (var item in anmap)
                    text = text.Replace($"<AssemblyName>{item.from}</AssemblyName>", $"<AssemblyName>{item.to}</AssemblyName>");
                return text;
            }
        );

        AddAssemblyNameToCsproj(context, Consts.BasePath + "src/Controls/src/Core/Controls.Core.csproj", "Microsoft.Maui.Controls.Core");
        AddAssemblyNameToCsproj(context, Consts.BasePath + "src/Controls/src/Xaml/Controls.Xaml.csproj", "Microsoft.Maui.Controls.Xaml");
        AddAssemblyNameToCsproj(context, Consts.BasePath + "src/Controls/tests/Core.UnitTests/Controls.Core.UnitTests.csproj", "Microsoft.Maui.Controls.Core.UnitTests");
        AddAssemblyNameToCsproj(context, Consts.BasePath + "src/Compatibility/Maps/src/Core/Compatibility.Maps.csproj", "Microsoft.Maui.Controls.Compatibility.Maps");
        AddAssemblyNameToCsproj(context, Consts.BasePath + "src/Core/tests/UnitTests/Core.UnitTests.csproj", "Microsoft.Maui.UnitTests");

        AddUsingNamespaceToFiles(context, "Microsoft.Maui.Layouts",
            Consts.BasePath + "src/Controls/src/Core/View.cs",
            Consts.BasePath + "src/Controls/src/Core/Layout/Layout.cs",
            Consts.BasePath + "src/Controls/src/Core/Layout/HorizontalStackLayout.cs",
            Consts.BasePath + "src/Controls/src/Core/Layout/VerticalStackLayout.cs");

        AddUsingNamespaceToFiles(context, "Microsoft.Maui.Handlers",
            Consts.BasePath + "src/Core/tests/UnitTests/TestClasses/HandlerStub.cs",
            Consts.BasePath + "src/Core/tests/UnitTests/PropertyMapperTests.cs",
            Consts.BasePath + "src/Controls/src/Core/Layout/HorizontalStackLayout.cs",
            Consts.BasePath + "src/Controls/src/Core/Layout/VerticalStackLayout.cs");

        AddUsingNamespaceToFiles(context, "Microsoft.Maui.Controls", Consts.BasePath + "src/Core/tests/UnitTests/PropertyMapperTests.cs");

        AddUsingNamespaceToFiles(context, "Microsoft.Maui.Handlers", context.GetFiles(Consts.BasePath + "src/Core/tests/DeviceTests/Handlers/**/*HandlerTests*.cs").ToArray());

        context.FileAppendLines(Consts.BasePath + "src/Core/src/Properties/AssemblyInfo.cs", 
            new string[] { "[assembly: InternalsVisibleTo(\"Microsoft.Maui.Controls.Core.UnitTests\")]" });

        context.MoveFile(Consts.BasePath + "Xamarin.Forms.sln.DotSettings", Consts.BasePath + "Microsoft.Maui.sln.DotSettings");
    }

    static void AddAssemblyNameToCsproj(ICakeContext context, FilePath file, string assemblyName)
    {
        var text = context.FileReadText(file);

        if (text.Contains("</TargetFrameworks>"))
            text = text.Replace("</TargetFrameworks>", "</TargetFrameworks>\r\n\t\t<AssemblyName>" + assemblyName + "</AssemblyName>");
        else
            text = text.Replace("</TargetFramework>", "</TargetFramework>\r\n\t\t<AssemblyName>" + assemblyName + "</AssemblyName>");

        context.FileWriteText(file, text);
    }

    static void AddUsingNamespaceToFiles(ICakeContext context, string ns, params FilePath[] files)
    {
        foreach (var file in files)
        {
            var text = context.FileReadText(file);

            text = $"using {ns};\r\n" + text;

            context.FileWriteText(file, text);
        }
    }

    static void FileFixupsHelper(ICakeContext context, string glob, params Func<string, string>[] fixups)
    {
        foreach (var file in context.GetFiles(glob))
        {
            if (file.Segments.Any(s => s.Equals("ControlGallery")))
                continue;

            var text = context.FileReadText(file);

            foreach (var f in fixups)
            {
                text = f(text);
            }

            context.FileWriteText(file, text);
        }
    }

    static IEnumerable<(string from, string to)> GetNamespaceMappings(ICakeContext context, FilePath xlsxFile, params string[] sheets)
    {
        var namespaceMapping = new Dictionary<string, string>();

        foreach (var sheet in sheets)
        {
            var setData = ReadExcel(context, xlsxFile, sheet, true);

            foreach (var row in setData)
            {
                var fromNs = row?[0];
                var toNs = row?[1];

                if (!string.IsNullOrEmpty(fromNs) && !string.IsNullOrEmpty(toNs))
                    namespaceMapping.TryAdd(fromNs, toNs);
            }
        }

        var ordered = namespaceMapping.OrderByDescending(kvp => kvp.Key?.Split('.', 1000)?.Length ?? 0);

        return ordered.Select(o => (o.Key, o.Value));
    }

    static void FixRelativeFileReferences(ICakeContext context, IEnumerable<(string from, string to)> directoryMappings, IEnumerable<(string from, string to)> fileMappings)
    {
        var globPattern = Consts.BasePath + "**/*.{csproj,targets,props,shproj,fsproj,nuspec}";

        foreach (var file in context.GetFiles(globPattern))
        {
            var fileFullPath = PlatformFriendlyPath(context, file);

            if (file.Segments.Contains("ControlGallery"))
                continue;

            var text = context.FileReadText(file);

            var allMatches = new List<Match>();

            allMatches.AddRange(Regex.Matches(text, "Include=\"(?<path>.*?)\"", RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace));
            allMatches.AddRange(Regex.Matches(text, "Project=\"(?<path>.*?)\"", RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace));

            allMatches.AddRange(Regex.Matches(text, "src=\"(?<path>.*?)\"", RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace));
            allMatches.AddRange(Regex.Matches(text, "file=\"(?<path>.*?)\"", RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace));

            foreach (var m in allMatches)
            {
                var rp = m?.Groups?["path"]?.Value;

                if (!string.IsNullOrEmpty(rp))
                {
                    var refRelativePath = new FilePath(rp);

                    //if (file.Segments.Contains("eng"))
                    //    continue;
                    if (!context.FileExists(refRelativePath.MakeAbsolute(file.GetDirectory())))
                        continue;

                    // For the Include="" value get the current full path
                    var refFullPath = refRelativePath.MakeAbsolute(file.GetDirectory());

                    // Get the relative directory path for the current value
                    var refDirRelativePath = refFullPath.GetDirectory().FullPath.Replace(Consts.BasePath, "./").TrimEnd('/', '\\');

                    // TODO: the path may  be deeper than the mapping
                    // in other words the path needs to check if it 
                    // starts with the one in the mapping 
                    // then figure out the additional folders

                    // Find a mapping for the directory to see if we are changing it
                    // so we can figure out what the new directory will be and fix the reference
                    var refMapping = directoryMappings.FirstOrDefault(dm =>
                        IsSamePathOrDeeper(refDirRelativePath, dm.from));

                    // See if the reference is further into the folder path than just the mapping
                    // since it's possible the reference is inside of one or more subfolders 
                    // beyond what the mapping specified
                    var refPathSubDirs = new DirectoryPath("./");

                    if (refMapping != default)
                    {
                        var fromBase = new DirectoryPath(Consts.BasePath).Combine(refMapping.from);
                        refPathSubDirs = fromBase.GetRelativePath(refFullPath.GetDirectory());
                    }

                    // We only care about SUB-dirs, deeper than the original reference's path
                    // check if the relative difference would have us backing out to a higher path
                    // and ignore those
                    //if (refPathSubDirs.FullPath.StartsWith(".."))
                    //    refPathSubDirs = new DirectoryPath("./");
                        
                    var currentFilename = refFullPath.GetFilename();

                    var refFileMapping = fileMappings.FirstOrDefault(fm => fm.from.Equals(currentFilename.FullPath));

                    var renamedFilename = refFileMapping != default ? refFileMapping.to : currentFilename;


                    // The reference might have its file renamed
                    var newRefFull = refFullPath.GetDirectory().Combine(refPathSubDirs).CombineWithFilePath(renamedFilename);

                    // The reference also be getting moved to another folder
                    if (refMapping != default)
                        newRefFull = (new DirectoryPath(Consts.BasePath)).Combine(refMapping.to).Combine(refPathSubDirs).CombineWithFilePath(renamedFilename);

                    // Get the new file location path but relative to the base working dir
                    var newFileRelativePath = file.GetDirectory().FullPath.Replace(Consts.BasePath, "./").TrimEnd('/', '\\');
                    // Look up a mapping for where the new file location directory would be to see if it's also moving
                    var thisFileMapping = directoryMappings.FirstOrDefault(dm => dm.from.TrimEnd('/', '\\') == newFileRelativePath);
            
                    // By default if no mapping is found, it means the file's dir isn't moving so we can use
                    // a relative path to the new reference path for the existing file's location
                    var newRefRelativeToFile = file;

                    // If this file is also moving to a new folder, we need to make the new reference's relative path
                    // be relative to the NEW location that this file will be in
                    if (thisFileMapping != default)
                        newRefRelativeToFile = (new DirectoryPath(Consts.BasePath)).Combine(thisFileMapping.to).CombineWithFilePath(file.GetFilename());

                    var finalnewref = newRefRelativeToFile.GetRelativePath(newRefFull);

                    text = text.Replace(rp, MSBuildifyPath(finalnewref));
                }
            }

            context.FileWriteText(file, text);
        }
    }

    static void FixSlns(ICakeContext context, IEnumerable<(string from, string to)> fileMappings, IEnumerable<(string from, string to)> dirMappings)
    {
        var slnFiles = context.GetFiles(Consts.BasePath + "**/*.sln");

        foreach (var sln in slnFiles)
        {
            var text = context.FileReadText(sln);

            foreach (var dm in dirMappings)
            {
                var fromFormatted = dm.from.TrimStart('.', '/').Replace('/', '\\');
                var toFormatted = dm.to.TrimStart('.', '/').Replace('/', '\\');

                text = text.Replace(fromFormatted, toFormatted);
            }

            foreach (var fm in fileMappings)
            {
                text = text.Replace(fm.from, fm.to);
                text = text.Replace(System.IO.Path.GetFileNameWithoutExtension(fm.from), System.IO.Path.GetFileNameWithoutExtension(fm.to));
            }

            text = text.Replace("Platform.Handlers", "Platform.Handlers");
            text = text.Replace("\"Forms\"", "\"Forms\"");

            text = text.Replace(".nuspec\\Controls.DualScreen.nuspec = .nuspec\\Controls.DualScreen.nuspec", ".nuspec\\Microsoft.Maui.Controls.DualScreen.nuspec = .nuspec\\Microsoft.Maui.Controls.DualScreen.nuspec")
            .Replace(".nuspec\\Compatibility.Maps.GTK.nuspec = .nuspec\\Compatibility.Maps.GTK.nuspec",".nuspec\\Microsoft.Maui.Controls.Compatibility.Maps.GTK.nuspec = .nuspec\\Microsoft.Maui.Controls.Compatibility.Maps.GTK.nuspec")
            .Replace(".nuspec\\Compatibility.Maps.nuspec = .nuspec\\Compatibility.Maps.nuspec",".nuspec\\Microsoft.Maui.Controls.Compatibility.Maps.nuspec = .nuspec\\Microsoft.Maui.Controls.Compatibility.Maps.nuspec")
            .Replace(".nuspec\\Compatibility.Maps.WPF.nuspec = .nuspec\\Compatibility.Maps.WPF.nuspec",".nuspec\\Microsoft.Maui.Controls.Compatibility.Maps.WPF.nuspec = .nuspec\\Microsoft.Maui.Controls.Compatibility.Maps.WPF.nuspec")
            .Replace(".nuspec\\Compatibility.GTK.nuspec = .nuspec\\Compatibility.GTK.nuspec",".nuspec\\Microsoft.Maui.Controls.Compatibility.GTK.nuspec = .nuspec\\Microsoft.Maui.Controls.Compatibility.GTK.nuspec")
            .Replace(".nuspec\\Compatibility.WPF.nuspec = .nuspec\\Compatibility.WPF.nuspec",".nuspec\\Microsoft.Maui.Controls.Compatibility.WPF.nuspec = .nuspec\\Microsoft.Maui.Controls.Compatibility.WPF.nuspec")
            .Replace("Project(\"{2150E333-8FDC-42A3-9474-1A3956D46DE8}\") = \"Forms\", \"Forms\",", "Project(\"{2150E333-8FDC-42A3-9474-1A3956D46DE8}\") = \"Controls\", \"Controls\",")
            .Replace("Project(\"{2150E333-8FDC-42A3-9474-1A3956D46DE8}\") = \"Platform.Handlers\", \"Platform.Handlers\",", "Project(\"{2150E333-8FDC-42A3-9474-1A3956D46DE8}\") = \"Handlers\", \"Handlers\",")
            .Replace("Project(\"{2150E333-8FDC-42A3-9474-1A3956D46DE8}\") = \"Platform.Renderers\", \"Platform.Renderers\",", "Project(\"{2150E333-8FDC-42A3-9474-1A3956D46DE8}\") = \"Compatibility\", \"Compatibility\",")
            .Replace("Project(\"{2150E333-8FDC-42A3-9474-1A3956D46DE8}\") = \"Microsoft.Maui.Controls\", \"Microsoft.Maui.Controls\",", "Project(\"{2150E333-8FDC-42A3-9474-1A3956D46DE8}\") = \"Controls\", \"Controls\",")
            ;
            
            context.FileWriteText(sln, text);
        }
    }

    static void RenameFolders(ICakeContext context, IEnumerable<(string from, string to)> mappings)
    {
        var baseDir = new DirectoryPath(Consts.BasePath);

        foreach (var map in mappings)
            MoveDirectory(context, baseDir.Combine(map.from), baseDir.Combine(map.to));

        foreach (var map in mappings)
        {
            if (context.DirectoryExists(map.from))
                context.DeleteDirectory(map.from, new DeleteDirectorySettings { Recursive =true, Force = true });
        }
    }

    // ie:  path: './src/Folder/subfolder/file.txt', sameOrDeeperThan: './src/Folder'
    static bool IsSamePathOrDeeper(DirectoryPath path, DirectoryPath sameOrDeeperThan)
    {
        // If it's not at least as deep as the same or deeper, obvious false
        if (path.Segments.Length < sameOrDeeperThan.Segments.Length)
            return false;

        for (int i = 0; i < path.Segments.Length; i++)
        {
            // If we make it to where all segments matched so far
            // and we've compared all the segments in the sameOrDeeperThan
            // then we are deeper
            if (i >= sameOrDeeperThan.Segments.Length)
                return true;

            // If the current segments don't match, it's not the same path
            if (path.Segments[i] != sameOrDeeperThan.Segments[i])
                return false;
        }

        return true;
    }

    static string MSBuildifyPath(Path path)
    {
        return path.FullPath.Replace("/./", "/").Replace('/', '\\');
    }

    static string PlatformFriendlyPath(ICakeContext context, Path path)
    {
        if (context.Environment.Platform.Family == PlatformFamily.Windows)
            return  path.FullPath.Replace('/', '\\');
        return path.FullPath;
    }

    public static void MoveDirectory(ICakeContext context, DirectoryPath sourceDir, DirectoryPath targetDir)
    {
        var sourcePath = PlatformFriendlyPath(context, sourceDir).TrimEnd('\\', '/', ' ');
        var targetPath = PlatformFriendlyPath(context, targetDir).TrimEnd('\\', '/', ' ');
        var files = System.IO.Directory.EnumerateFiles(sourcePath, "*", System.IO.SearchOption.AllDirectories)
                            .GroupBy(s=> System.IO.Path.GetDirectoryName(s));
        foreach (var folder in files)
        {
            var targetFolder = folder.Key.Replace(sourcePath, targetPath);
            System.IO.Directory.CreateDirectory(targetFolder);
            foreach (var file in folder)
            {
                var targetFile = System.IO.Path.Combine(targetFolder, System.IO.Path.GetFileName(file));
                if (System.IO.File.Exists(targetFile))
                    System.IO.File.Delete(targetFile);
                System.IO.File.Move(file, targetFile);
            }
        }
        //System.IO.Directory.Delete(source, true);
    }

    static void DeleteEmptyDirectories(ICakeContext context, DirectoryPath root)
    {
        foreach (var directory in context.GetSubDirectories(root))
        {
            DeleteEmptyDirectories(context, directory);

            var path = PlatformFriendlyPath(context, directory);
            if (System.IO.Directory.GetFiles(path).Length == 0 && 
                System.IO.Directory.GetDirectories(path).Length == 0)
            {
                System.IO.Directory.Delete(path, false);
            }
        }
    }

    static void RenameFiles(ICakeContext context, IEnumerable<(string from, string to)> mappings)
    {
        var files = context.GetFiles(Consts.BasePath + "**/*");

        foreach (var file in files)
        {
            var filename = System.IO.Path.GetFileName(PlatformFriendlyPath(context, file));

            var map = mappings.FirstOrDefault(m => m.from.Equals(filename));

            if (map != default)
                context.MoveFile(file, file.GetDirectory().CombineWithFilePath(map.to));
        }
    }

    static void ReplaceMappings(ICakeContext context, FilePath xlsxFile, params (string sheetName, string fileGlobPattern)[] sets)
    {
        foreach (var set in sets)
        {
            var setData = ReadExcel(context, xlsxFile, set.sheetName, true);

            var namespaceMapping = new Dictionary<string, string>();

            foreach (var row in setData)
            {
                var fromNs = row?[0];
                var toNs = row?[1];

                if (!string.IsNullOrEmpty(fromNs) && !string.IsNullOrEmpty(toNs))
                    namespaceMapping.TryAdd(fromNs, toNs);
            }

            foreach (var file in context.GetFiles(set.fileGlobPattern))
            {
                var text = context.FileReadText(file);

                foreach (var mapping in namespaceMapping)
                {
                    text = text.Replace(mapping.Key, mapping.Value);
                }
                
                context.FileWriteText(file, text);
            }
        }
    }

    static IEnumerable<List<string>> ReadExcel(ICakeContext context, FilePath xlsxFile, string sheetName, bool hasHeader = true)
    {
        var result = new List<List<string>>();

        using(var package = new ExcelPackage(new System.IO.FileInfo(PlatformFriendlyPath(context, xlsxFile))))
        {
            var sheet = package.Workbook.Worksheets[sheetName];

            for (int row = hasHeader ? 2 : 1; row <= sheet.Dimension.Rows; row++)
            {
                var rowValues = new List<string>();

                for (int col = 1; col <= sheet.Dimension.Columns; col++)
                    rowValues.Add(sheet.GetValue<string>(row, col));

                result.Add(rowValues);
            }
        }

        return result;
    }

    static IEnumerable<string> GetNamespaceCodeDeclarations(ICakeContext context, string glob)
    {
        var csFiles = context.GetFiles(glob);

        var namespaces = new List<string>();

        foreach (var f in csFiles)
        {
            if (f.Segments.Contains("ControlGallery"))
                continue;

            var cs = context.FileReadText(f);


            var matchGroups = context.FindRegexMatchesGroupsInFile(f, "namespace\\s+(?<ns>Xamarin(.*))\\s?{", 
                RegexOptions.IgnorePatternWhitespace | RegexOptions.Multiline);

            foreach (var mg in matchGroups)
            {
                var nsg = mg?.FirstOrDefault(mgi => mgi.Name == "ns");

                var ns = nsg?.Value;

                if (!string.IsNullOrEmpty(ns) && !namespaces.Any(n => n.Equals(ns, StringComparison.OrdinalIgnoreCase)))
                    namespaces.Add(ns);
            }
        }


        var sortedNamespaces = namespaces.OrderByDescending(n => n.Split('.', 1000, StringSplitOptions.RemoveEmptyEntries)?.Length ?? 0);

        return sortedNamespaces;
    }
}
